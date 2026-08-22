using ACadSharp.Entities;
using ACadSharp.IO;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO;

/// <summary>
/// A solid and a body carry their geometry the same way a region does, and the writers already had a
/// method that takes the base type - but both writers listed Solid3D and CadBody as not implemented,
/// so every 3D solid in a drawing was dropped. A type census never showed it because the drawings it
/// was run on are 2D; the first round-trip test written for the AcDs section did, by counting three
/// entities in and one out.
/// </summary>
public class Solid3DWriteTests
{
	private static string sampleR2018 => Path.Combine(TestVariables.SamplesFolder, "sample_AC1032.dwg");

	[Theory]
	[InlineData(ACadVersion.AC1018)]
	[InlineData(ACadVersion.AC1024)]
	public void ASolidSurvivesADwgRoundTrip(ACadVersion version)
	{
		//Measured in AutoCAD 2027: the sample written at R2010 opens, audits 0, and AutoCAD's own DXF
		//export of our file has both solids and the region, exactly as its export of the source does.
		CadDocument doc = DwgReader.Read(sampleR2018);
		Solid3D[] before = this.solids(doc);
		Assert.NotEmpty(before);

		doc.Header.Version = version;
		using MemoryStream stream = new();
		using (DwgWriter writer = new(stream, doc))
		{
			writer.Write();
		}

		Solid3D[] after = this.solids(DwgReader.Read(new MemoryStream(stream.ToArray())));
		Assert.Equal(before.Length, after.Length);
		for (int i = 0; i < before.Length; i++)
		{
			Assert.Equal(before[i].AcisData, after[i].AcisData);
		}
	}

	[Fact]
	public void ASolidSurvivesADxfRoundTrip()
	{
		CadDocument doc = DwgReader.Read(sampleR2018);
		Solid3D[] before = this.solids(doc);
		Assert.NotEmpty(before);

		using MemoryStream stream = new();
		using (DxfWriter writer = new(stream, doc, false))
		{
			writer.Write();
		}

		Solid3D[] after = this.solids(DxfReader.Read(new MemoryStream(stream.ToArray())));
		Assert.Equal(before.Length, after.Length);
		for (int i = 0; i < before.Length; i++)
		{
			Assert.Equal(before[i].AcisData, after[i].AcisData);
		}
	}

	[Fact]
	public void ASolidCarriesItsSubclassAndHistoryHandleInADxf()
	{
		//Not cosmetic, and not guessed: written without the 350 group AutoCAD refuses the whole DXF -
		//not the entity, the file - and with it the same file opens and audits 0. The group 2 GUID
		//AutoCAD also writes is genuinely optional, as it is for a region.
		CadDocument doc = DwgReader.Read(sampleR2018);
		Assert.NotEmpty(this.solids(doc));

		string text = this.write(doc);
		int solid = text.IndexOf("\n3DSOLID\n");
		Assert.True(solid > 0);

		string record = text.Substring(solid, text.IndexOf("\n  0\n", solid + 1) - solid);
		Assert.Contains("AcDbModelerGeometry", record);
		Assert.Contains("AcDb3dSolid", record);
		Assert.Contains("\n350\n", record);
	}

	[Fact]
	public void ASolidWithNoGeometryIsLeftOutAndSaidSo()
	{
		//The same rule regions follow: an empty one is a handle with no shape, so it is left out and
		//the reason is reported rather than left to be found.
		CadDocument doc = DwgReader.Read(sampleR2018);
		foreach (Solid3D solid in this.solids(doc))
		{
			solid.AcisData = null;
		}

		string reported = null;
		using MemoryStream stream = new();
		using (DxfWriter writer = new(stream, doc, false))
		{
			writer.OnNotification += (s, e) => { if (e.Message.Contains("Solid3D") && e.Message.Contains("no geometry")) reported = e.Message; };
			writer.Write();
		}

		Assert.NotNull(reported);
		Assert.DoesNotContain("\n3DSOLID\n", System.Text.Encoding.UTF8.GetString(stream.ToArray()).Replace("\r", string.Empty));
	}

	private Solid3D[] solids(CadDocument doc)
	{
		return doc.BlockRecords
			.SelectMany(b => b.Entities.OfType<Solid3D>())
			.OrderBy(s => s.Handle)
			.ToArray();
	}

	private string write(CadDocument doc)
	{
		using MemoryStream stream = new();
		using (DxfWriter writer = new(stream, doc, false))
		{
			writer.Write();
		}

		return System.Text.Encoding.UTF8.GetString(stream.ToArray()).Replace("\r", string.Empty);
	}
}
