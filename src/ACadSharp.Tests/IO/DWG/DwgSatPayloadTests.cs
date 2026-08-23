using ACadSharp.Entities;
using ACadSharp.IO;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO.DWG;

/// <summary>
/// The other form of an ACIS payload: character-swapped SAT text, in length-prefixed blocks, inside
/// the entity. It is the only form an R2000 file has, and the DWG writer used to leave every entity
/// carrying one out of the drawing - written the ways tried before, AutoCAD refused to open the file
/// at all.
/// </summary>
/// <remarks>
/// What was missing was one undocumented bit. Read off AutoCAD's own R2000 drawings, the bit in
/// front of the version number is 1 for the SAT text form and 0 for the binary one; this writer
/// always wrote 0, and AutoCAD refuses a version 1 payload behind a 0 - measured by writing the same
/// drawing both ways, one opens and audits 0, the other does not open. The same reading gave the
/// block size AutoCAD splits at, 4,096 bytes, and showed that the wireframe block follows the
/// payload rather than the entity ending with it.
/// </remarks>
public class DwgSatPayloadTests
{
	//The upstream R2000 sample: two solids and a region, all three with SAT text payloads.
	private static string sampleR2000 => Path.Combine(TestVariables.SamplesFolder, "sample_AC1015.dwg");

	//The same drawing saved at R2004, where the payload is binary instead.
	private static string sampleR2004 => Path.Combine(TestVariables.SamplesFolder, "sample_AC1018.dwg");

	[Fact]
	public void TheUpstreamR2000SampleCarriesSatText()
	{
		ModelerGeometry[] geometry = this.modelerGeometry(DwgReader.Read(sampleR2000));

		Assert.Equal(3, geometry.Length);
		foreach (ModelerGeometry entity in geometry)
		{
			Assert.NotEmpty(entity.AcisData);
			Assert.False(entity.IsBinaryAcisData);
		}
	}

	[Theory]
	[InlineData(ACadVersion.AC1015)]
	[InlineData(ACadVersion.AC1018)]
	[InlineData(ACadVersion.AC1024)]
	public void SatTextSurvivesADwgRoundTrip(ACadVersion version)
	{
		//Measured in AutoCAD 2027 at all three: the drawing opens, audits 0, and AutoCAD's own DXF
		//export of it counts the same entities and the same ASM_Data records as its export of the
		//source. R2000 is the version this could never be written at before.
		CadDocument doc = DwgReader.Read(sampleR2000);
		ModelerGeometry[] before = this.modelerGeometry(doc);
		Assert.NotEmpty(before);

		ModelerGeometry[] after = this.modelerGeometry(this.roundTrip(doc, version));

		Assert.Equal(before.Length, after.Length);
		for (int i = 0; i < before.Length; i++)
		{
			Assert.Equal(before[i].GetType(), after[i].GetType());
			Assert.Equal(before[i].AcisData, after[i].AcisData);
			Assert.False(after[i].IsBinaryAcisData);
		}
	}

	[Fact]
	public void APayloadLongerThanOneBlockIsSplitAndPutBackTogether()
	{
		//AutoCAD cuts at 4,096 bytes - its own R2000 file splits a 5,041 byte payload into a block of
		//4,096 and one of 945 - so anything longer than that exercises the join on the way back.
		CadDocument doc = DwgReader.Read(sampleR2000);
		ModelerGeometry longest = this.modelerGeometry(doc).OrderByDescending(g => g.AcisData.Length).First();
		Assert.True(longest.AcisData.Length > 4096);

		ModelerGeometry[] after = this.modelerGeometry(this.roundTrip(doc, ACadVersion.AC1015));

		Assert.Contains(after, g => g.AcisData.SequenceEqual(longest.AcisData));
	}

	[Fact]
	public void TheWireframeBlockBehindAPayloadIsReadAndWrittenBack()
	{
		//Before R2013 the reader stopped at the end of the payload and returned, so the block that
		//follows it - the point AutoCAD draws the shape about, and the isoline count - was read by
		//nothing and could be written back by nothing either. The R2000 sample has a point on every
		//one of its three entities.
		CadDocument doc = DwgReader.Read(sampleR2000);
		ModelerGeometry[] before = this.modelerGeometry(doc);
		Assert.Contains(before, g => g.Point != CSMath.XYZ.Zero);

		ModelerGeometry[] after = this.modelerGeometry(this.roundTrip(doc, ACadVersion.AC1015));

		for (int i = 0; i < before.Length; i++)
		{
			Assert.Equal(before[i].Point, after[i].Point);
			Assert.Equal(before[i].IsoLinesCount, after[i].IsoLinesCount);
		}
	}

	[Fact]
	public void ABinaryPayloadIsLeftOutOfAnR2000FileAndSaidSo()
	{
		//The one case that stays refused, and it is a limit of the format: R2000 predates the binary
		//form of the modeler. An empty region is a handle with no shape, so the entity is left out
		//and the reason is reported.
		CadDocument doc = DwgReader.Read(sampleR2004);
		Assert.NotEmpty(this.modelerGeometry(doc));
		Assert.True(this.modelerGeometry(doc).First().IsBinaryAcisData);

		doc.Header.Version = ACadVersion.AC1015;
		string reported = null;
		using MemoryStream stream = new();
		using (DwgWriter writer = new(stream, doc))
		{
			writer.OnNotification += (s, e) => { if (e.Message.Contains("is not written to a")) reported = e.Message; };
			writer.Write();
		}

		Assert.NotNull(reported);
		Assert.Contains("binary payload", reported);
		Assert.Empty(this.modelerGeometry(DwgReader.Read(new MemoryStream(stream.ToArray()))));
	}

	[Fact]
	public void SatTextIsLeftOutOfAnR2013PlusFileAndSaidSo()
	{
		//From R2013 the payload lives in the AcDs data section, and the only form that section is
		//read from is the binary one. Putting the SAT text there was measured: AutoCAD opens the
		//drawing and audits 0, but its own save of that file down to R2010 hands back a region with
		//no geometry at all, where the same save of a drawing AutoCAD wrote gives 833 bytes. An
		//entity AutoCAD keeps but cannot read is worse than one that says it was left out.
		CadDocument doc = DwgReader.Read(sampleR2000);
		Assert.NotEmpty(this.modelerGeometry(doc));

		doc.Header.Version = ACadVersion.AC1032;
		string reported = null;
		using MemoryStream stream = new();
		using (DwgWriter writer = new(stream, doc))
		{
			writer.OnNotification += (s, e) => { if (e.Message.Contains("is not written to a")) reported = e.Message; };
			writer.Write();
		}

		Assert.NotNull(reported);
		Assert.Contains("SAT text", reported);
		Assert.Empty(this.modelerGeometry(DwgReader.Read(new MemoryStream(stream.ToArray()))));
	}

	[Theory]
	[InlineData(ACadVersion.AC1024)]
	[InlineData(ACadVersion.AC1027)]
	[InlineData(ACadVersion.AC1032)]
	public void ASolidCarriesItsHistoryHandleFromR2007(ACadVersion version)
	{
		//The reader has read a 350 history handle for every solid since R2007, and the writer wrote
		//one only from R2013 - so every R2007-R2010 solid this library wrote was a handle short, and
		//reading it back took whatever padding followed the handle stream for a reference code. It
		//survived on alignment luck: the drawing below lost its first solid the moment the entity
		//grew by a wireframe block.
		CadDocument doc = DwgReader.Read(sampleR2004);
		ModelerGeometry[] before = this.modelerGeometry(doc);
		Assert.Equal(3, before.Length);

		ModelerGeometry[] after = this.modelerGeometry(this.roundTrip(doc, version));

		Assert.Equal(before.Length, after.Length);
		for (int i = 0; i < before.Length; i++)
		{
			Assert.Equal(before[i].AcisData, after[i].AcisData);
		}
	}

	private CadDocument roundTrip(CadDocument doc, ACadVersion version)
	{
		doc.Header.Version = version;
		using MemoryStream stream = new();
		using (DwgWriter writer = new(stream, doc))
		{
			writer.Write();
		}

		return DwgReader.Read(new MemoryStream(stream.ToArray()));
	}

	private ModelerGeometry[] modelerGeometry(CadDocument doc)
	{
		return doc.BlockRecords
			.SelectMany(b => b.Entities.OfType<ModelerGeometry>())
			.OrderBy(g => g.Handle)
			.ToArray();
	}
}
