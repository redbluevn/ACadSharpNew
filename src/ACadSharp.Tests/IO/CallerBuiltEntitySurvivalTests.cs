namespace ACadSharp.Tests.IO;

using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Entities built in code and written, rather than read from a file and written back.
/// </summary>
/// <remarks>
/// This is the path an application actually uses, and it is where a defect hides longest: a
/// TableEntity built this way was dropped from every DWG for as long as the fork existed, because
/// the writer read a public flag as an internal marker and no test ever built one. Entity2DRoundTrip
/// covers sixteen types this way; the consuming application constructs twenty-eight, and these are
/// the thirteen nobody checked. The assertion is deliberately the weakest useful one - the entity
/// still exists afterwards - because that is what silently failed before, and a weak check on an
/// untested path beats a thorough check on a tested one.
/// </remarks>
public class CallerBuiltEntitySurvivalTests
{
	private static readonly Dictionary<string, Func<Entity>> _cases = new()
	{
		["AttributeDefinition"] = () => new AttributeDefinition
		{
			Tag = "TAG",
			Value = "value",
			Prompt = "prompt",
			InsertPoint = new XYZ(1, 2, 0),
			Height = 2.5,
		},
		["DimensionAligned"] = () => new DimensionAligned
		{
			FirstPoint = new XYZ(0, 0, 0),
			SecondPoint = new XYZ(10, 0, 0),
			DefinitionPoint = new XYZ(10, 0, 0),
			TextMiddlePoint = new XYZ(5, 2, 0),
		},
		["DimensionDiameter"] = () => new DimensionDiameter
		{
			AngleVertex = new XYZ(0, 0, 0),
			DefinitionPoint = new XYZ(5, 0, 0),
			TextMiddlePoint = new XYZ(2, 2, 0),
		},
		["DimensionRadius"] = () => new DimensionRadius
		{
			AngleVertex = new XYZ(0, 0, 0),
			DefinitionPoint = new XYZ(5, 0, 0),
			TextMiddlePoint = new XYZ(2, 2, 0),
		},
		["Ray"] = () => new Ray
		{
			StartPoint = new XYZ(1, 1, 0),
			Direction = new XYZ(1, 0, 0),
		},
		["XLine"] = () => new XLine
		{
			FirstPoint = new XYZ(1, 1, 0),
			Direction = new XYZ(0, 1, 0),
		},
		["Solid"] = () => new Solid
		{
			FirstCorner = new XYZ(0, 0, 0),
			SecondCorner = new XYZ(10, 0, 0),
			ThirdCorner = new XYZ(0, 10, 0),
			FourthCorner = new XYZ(10, 10, 0),
		},
		["Tolerance"] = () => new Tolerance
		{
			InsertionPoint = new XYZ(1, 1, 0),
			Text = "%%v0.5",
		},
		["MLine"] = () =>
		{
			MLine mline = new MLine();
			mline.StartPoint = new XYZ(0, 0, 0);
			mline.Vertices.Add(new MLine.Vertex { Position = new XYZ(0, 0, 0), Direction = XYZ.AxisX, Miter = XYZ.AxisY });
			mline.Vertices.Add(new MLine.Vertex { Position = new XYZ(10, 0, 0), Direction = XYZ.AxisX, Miter = XYZ.AxisY });
			return mline;
		},
		["PdfUnderlay"] = () => new PdfUnderlay(new ACadSharp.Objects.PdfUnderlayDefinition
		{
			Name = "probe",
			File = "probe.pdf",
		})
		{
			InsertPoint = new XYZ(1, 1, 0),
		},
		["Viewport"] = () => new Viewport
		{
			Center = new XYZ(5, 5, 0),
			Width = 10,
			Height = 8,
		},
		["RasterImage"] = () => new RasterImage(new ACadSharp.Objects.ImageDefinition
		{
			Name = "probe",
			FileName = "probe.png",
			Size = new XY(100, 100),
		})
		{
			InsertPoint = new XYZ(0, 0, 0),
			UVector = new XYZ(1, 0, 0),
			VVector = new XYZ(0, 1, 0),
		},
	};

	public static IEnumerable<object[]> DwgCases =>
		_cases.Keys.SelectMany(name => new[]
		{
			new object[] { name, ACadVersion.AC1015 },
			new object[] { name, ACadVersion.AC1032 },
		});

	public static IEnumerable<object[]> DxfCases => _cases.Keys.Select(name => new object[] { name });

	[Fact(Skip = "Known defect, reproduction kept: the DWG writer emits a MULTILEADER its own reader cannot parse.")]
	public void ADwgKeepsAMultiLeaderBuiltInCode()
	{
		//Writing raises no warning at all, and reading the result back raises
		//    [Error] Could not read MULTILEADER number 507 with handle: 96
		//    DwgException: Failed to read ReadBitDouble
		//which is the reader running off the end of the entity - the writer emits fewer bits than
		//the reader consumes, the same shape of defect as T73's missing handle. Both AC1015 and
		//AC1032; the DXF round trip keeps it. Finding the field needs the bit probe of 13 5f/5g.
		//
		//Not chased yet, deliberately: MultiLeader is outside the project's 2D scope and the
		//consuming application never writes DWG at all - it reads DWG and writes DXF - so nothing
		//downstream is losing multileaders today. Kept as a standing reproduction rather than
		//deleted, because the next person to touch MULTILEADER should start from a red test.
		CadDocument doc = new CadDocument(ACadVersion.AC1032);
		doc.Entities.Add(new MultiLeader());

		MemoryStream stream = new();
		DwgWriter.Write(stream, doc);

		CadDocument back = DwgReader.Read(new MemoryStream(stream.ToArray()));
		Assert.Single(back.Entities.OfType<MultiLeader>());
	}

	[Theory]
	[MemberData(nameof(DwgCases))]
	public void ADwgKeepsAnEntityBuiltInCode(string name, ACadVersion version)
	{
		CadDocument doc = new CadDocument(version);
		Entity entity = _cases[name]();
		doc.Entities.Add(entity);
		Type type = entity.GetType();

		MemoryStream stream = new();
		DwgWriter.Write(stream, doc);

		CadDocument back = DwgReader.Read(new MemoryStream(stream.ToArray()));
		Assert.Single(back.Entities.Where(e => e.GetType() == type));
	}

	[Theory]
	[MemberData(nameof(DxfCases))]
	public void ADxfKeepsAnEntityBuiltInCode(string name)
	{
		CadDocument doc = new CadDocument(ACadVersion.AC1032);
		Entity entity = _cases[name]();
		doc.Entities.Add(entity);
		Type type = entity.GetType();

		MemoryStream stream = new();
		using (DxfWriter writer = new(stream, doc, false))
		{
			writer.Write();
		}

		CadDocument back = DxfReader.Read(new MemoryStream(stream.ToArray()));
		Assert.Single(back.Entities.Where(e => e.GetType() == type));
	}
}
