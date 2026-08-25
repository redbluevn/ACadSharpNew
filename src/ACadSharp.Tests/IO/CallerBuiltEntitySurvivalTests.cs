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
		//A default MultiLeader carries no leader root, which is exactly the shape that used to be
		//dropped from every DWG. The leader roots themselves are covered version by version in
		//ADwgKeepsAMultiLeaderBuiltInCode; here it joins the sweep so the DXF side is covered too.
		["MultiLeader"] = () => new MultiLeader
		{
			ArrowheadSize = 3.5,
			LandingDistance = 7.25,
		},
	};

	/// <summary>
	/// What has to still be true of each entity afterwards, beyond it being there at all.
	/// </summary>
	/// <remarks>
	/// Survival was the weakest useful assertion and it earned its place - a caller-built
	/// TableEntity used to vanish entirely. But three defects found on 2026-08-24 and 2026-08-25
	/// were entities that survived with their contents changed: a hatch whose bounding box grew a
	/// height made of weights, a book colour whose name doubled, and text whose line breaks came
	/// back as the two characters ^ and J. An existence check passes all three.
	/// </remarks>
	private static readonly Dictionary<string, Action<Entity>> _checks = new()
	{
		["AttributeDefinition"] = e =>
		{
			AttributeDefinition a = (AttributeDefinition)e;
			Assert.Equal("TAG", a.Tag);
			Assert.Equal("value", a.Value);
			Assert.Equal(2.5, a.Height, 9);
		},
		["DimensionAligned"] = e =>
		{
			DimensionAligned d = (DimensionAligned)e;
			Assert.Equal(0, d.FirstPoint.X, 9);
			Assert.Equal(10, d.SecondPoint.X, 9);
		},
		["Ray"] = e => Assert.Equal(1, ((Ray)e).StartPoint.X, 9),
		["XLine"] = e => Assert.Equal(1, ((XLine)e).FirstPoint.Y, 9),
		["Solid"] = e =>
		{
			Solid solid = (Solid)e;
			Assert.Equal(10, solid.SecondCorner.X, 9);
			Assert.Equal(10, solid.ThirdCorner.Y, 9);
		},
		["Tolerance"] = e => Assert.Equal("%%v0.5", ((Tolerance)e).Text),
		["MLine"] = e => Assert.Equal(2, ((MLine)e).Vertices.Count),
		["MultiLeader"] = e =>
		{
			MultiLeader m = (MultiLeader)e;
			Assert.Equal(3.5, m.ArrowheadSize, 9);
			Assert.Equal(7.25, m.LandingDistance, 9);
			Assert.Empty(m.ContextData.LeaderRoots);
		},
		["Viewport"] = e =>
		{
			Viewport v = (Viewport)e;
			Assert.Equal(10, v.Width, 9);
			Assert.Equal(8, v.Height, 9);
		},
	};

	public static IEnumerable<object[]> DwgCases =>
		_cases.Keys.SelectMany(name => new[]
		{
			new object[] { name, ACadVersion.AC1015 },
			new object[] { name, ACadVersion.AC1032 },
		});

	public static IEnumerable<object[]> DxfCases => _cases.Keys.Select(name => new object[] { name });

	[Theory]
	[InlineData(ACadVersion.AC1015, 0)]
	[InlineData(ACadVersion.AC1015, 1)]
	[InlineData(ACadVersion.AC1015, 3)]
	[InlineData(ACadVersion.AC1032, 0)]
	[InlineData(ACadVersion.AC1032, 1)]
	[InlineData(ACadVersion.AC1032, 3)]
	public void ADwgKeepsAMultiLeaderBuiltInCode(ACadVersion version, int leaderRoots)
	{
		//A MultiLeader built in code starts with no leader roots at all, and that alone used to
		//lose it: the writer emitted the plain count, the reader read the zero and then consumed
		//seven bits and a whole leader root that were never written, ran off the end of the entity
		//and dropped it. The two zero-root cases here are the ones that were red; one and three
		//roots passed before the fix and are kept as the controls that say the reader still reads
		//what it always read. What a count of zero means was settled against AutoCAD 2027 rather
		//than guessed - see the comment in DwgObjectReader.readMultiLeaderAnnotContext.
		CadDocument doc = new CadDocument(version);
		MultiLeader multiLeader = new MultiLeader();
		for (int i = 0; i < leaderRoots; i++)
		{
			multiLeader.ContextData.LeaderRoots.Add(new ACadSharp.Objects.MultiLeaderObjectContextData.LeaderRoot
			{
				LeaderIndex = i,
				ConnectionPoint = new XYZ(i, i + 1, 0),
			});
		}

		doc.Entities.Add(multiLeader);

		MemoryStream stream = new();
		DwgWriter.Write(stream, doc);

		CadDocument back = DwgReader.Read(new MemoryStream(stream.ToArray()));
		MultiLeader got = Assert.Single(back.Entities.OfType<MultiLeader>());
		Assert.Equal(leaderRoots, got.ContextData.LeaderRoots.Count);
		for (int i = 0; i < leaderRoots; i++)
		{
			Assert.Equal(i, got.ContextData.LeaderRoots[i].LeaderIndex);
			Assert.Equal(new XYZ(i, i + 1, 0), got.ContextData.LeaderRoots[i].ConnectionPoint);
		}
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
		Entity got = Assert.Single(back.Entities.Where(e => e.GetType() == type));
		if (_checks.TryGetValue(name, out Action<Entity> check))
		{
			check(got);
		}
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
		Entity got = Assert.Single(back.Entities.Where(e => e.GetType() == type));
		if (_checks.TryGetValue(name, out Action<Entity> check))
		{
			check(got);
		}
	}
}
