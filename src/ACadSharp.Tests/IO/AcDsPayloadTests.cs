using ACadSharp.Entities;
using ACadSharp.IO;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace ACadSharp.Tests.IO;

/// <summary>
/// From R2013 the geometry of a solid, body or region is not kept in the entity. The entity carries
/// a bit saying its payload lives in the AcDs data section, and that section holds the bytes keyed by
/// the entity's handle. The section was parsed and the waiting entities were collected, but the two
/// were never introduced, so every modeler geometry entity read from an R2013+ DWG arrived with an
/// empty payload - a shape with no geometry, and nothing any writer could put back.
/// </summary>
public class AcDsPayloadTests
{
	private static string sampleR2018 => Path.Combine(TestVariables.SamplesFolder, "sample_AC1032.dwg");

	[Theory]
	[InlineData("sample_AC1027.dwg")]
	[InlineData("sample_AC1032.dwg")]
	public void AnR2013PlusDrawingCarriesItsAcisPayload(string sample)
	{
		CadDocument doc = DwgReader.Read(Path.Combine(TestVariables.SamplesFolder, sample));

		ModelerGeometry[] geometry = this.modelerGeometry(doc);
		Assert.NotEmpty(geometry);
		foreach (ModelerGeometry entity in geometry)
		{
			Assert.NotNull(entity.AcisData);
			Assert.NotEmpty(entity.AcisData);
		}
	}

	[Theory]
	[InlineData("sample_AC1027.dwg")]
	[InlineData("sample_AC1032.dwg")]
	public void APayloadFromTheSectionIsAWholeAcisStream(string sample)
	{
		//Non-empty is not the same as correct: a payload assembled from the wrong bytes, or from blob
		//pages put together in the wrong order, would still have a length. An ACIS stream ends with
		//its own end marker, so a payload that both starts with a modeler signature and ends with
		//that marker is one that was assembled right the whole way through.
		CadDocument doc = DwgReader.Read(Path.Combine(TestVariables.SamplesFolder, sample));

		ModelerGeometry[] geometry = this.modelerGeometry(doc);
		Assert.NotEmpty(geometry);
		foreach (ModelerGeometry entity in geometry)
		{
			Assert.True(entity.IsBinaryAcisData);

			string tail = new(Encoding.ASCII.GetString(entity.AcisData.Skip(entity.AcisData.Length - 32).ToArray())
				.Select(c => c >= ' ' && c < (char)127 ? c : '.')
				.ToArray());
			Assert.Contains("End", tail);
			Assert.Contains("data", tail);
		}
	}

	[Fact]
	public void APayloadFromTheAcDsSectionIsRecognisedAsBinary()
	{
		//The writers choose between SAT text and a binary payload from this flag, and the flag only
		//knew the "ACIS BinaryFile" signature. A payload that starts "ASM BinaryFile" - which is what
		//the AcDs section of a real R2018 drawing holds - was being called SAT text, so a binary
		//payload would have been written out as characters.
		CadDocument doc = DwgReader.Read(sampleR2018);

		foreach (ModelerGeometry entity in this.modelerGeometry(doc))
		{
			Assert.True(entity.IsBinaryAcisData, $"{entity.GetType().Name} payload starts with '{Encoding.ASCII.GetString(entity.AcisData.Take(14).ToArray())}'");
			Assert.Null(entity.GetAcisText());
		}
	}

	[Fact]
	public void AnAsmSignatureIsBinaryJustAsAnAcisOneIs()
	{
		Region region = new() { AcisData = Encoding.ASCII.GetBytes("ASM BinaryFile and then some bytes") };
		Assert.True(region.IsBinaryAcisData);

		region.AcisData = Encoding.ASCII.GetBytes("ACIS BinaryFile and then some bytes");
		Assert.True(region.IsBinaryAcisData);

		region.AcisData = Encoding.ASCII.GetBytes("400 0 0 0 1 \n plain SAT text");
		Assert.False(region.IsBinaryAcisData);
	}

	[Fact]
	public void APayloadReadFromTheSectionCanBeWrittenBackWhereItFits()
	{
		//The point of reading it: a version that embeds the payload can now carry geometry that came
		//from an R2018 drawing. Measured on a production drawing too - written at R2010 it audits to
		//the same 28 errors it did before, and AutoCAD's own export of our file has the region back.
		CadDocument doc = DwgReader.Read(sampleR2018);
		//Only regions, deliberately: Solid3D and CadBody are on the not-implemented list of both
		//writers, so they are dropped whatever their payload says. Reading the section does not
		//change that, and this test is about the reading.
		Region[] before = this.modelerGeometry(doc).OfType<Region>().ToArray();
		Assert.NotEmpty(before);

		doc.Header.Version = ACadVersion.AC1024;
		using MemoryStream stream = new();
		using (DwgWriter writer = new(stream, doc))
		{
			writer.Write();
		}

		Region[] after = this.modelerGeometry(DwgReader.Read(new MemoryStream(stream.ToArray()))).OfType<Region>().ToArray();
		Assert.Equal(before.Length, after.Length);
		for (int i = 0; i < before.Length; i++)
		{
			Assert.Equal(before[i].AcisData, after[i].AcisData);
		}
	}

	private ModelerGeometry[] modelerGeometry(CadDocument doc)
	{
		return doc.BlockRecords
			.SelectMany(b => b.Entities.OfType<ModelerGeometry>())
			.OrderBy(m => m.Handle)
			.ToArray();
	}
}
