using ACadSharp.Entities;
using ACadSharp.IO;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO.DWG;

/// <summary>
/// T87: the R2007 container is written for the first time. These are the self-consistency rungs
/// of the oracle ladder - this library's own reader, which is proven against real AutoCAD files,
/// must read back what the writer produces before AutoCAD is asked anything.
/// </summary>
public class DwgWriterAC21Tests
{
	private static string samplePath => Path.Combine(TestVariables.SamplesFolder, "sample_AC1021.dwg");

	private static CadDocument roundTrip(CadDocument doc)
	{
		using MemoryStream stream = new();
		using (DwgWriter writer = new(stream, doc))
		{
			writer.Write();
		}

		return DwgReader.Read(new MemoryStream(stream.ToArray()));
	}

	[Fact]
	public void AnEmptyDocumentRoundTripsAtR2007()
	{
		CadDocument doc = new CadDocument(ACadVersion.AC1021);

		CadDocument back = roundTrip(doc);

		Assert.Equal(ACadVersion.AC1021, back.Header.Version);
		Assert.NotNull(back.ModelSpace);
	}

	[Fact]
	public void TheSampleRoundTripsAtR2007()
	{
		CadDocument doc = DwgReader.Read(samplePath);
		int entitiesBefore = doc.BlockRecords.SelectMany(b => b.Entities).Count();

		CadDocument back = roundTrip(doc);

		Assert.Equal(ACadVersion.AC1021, back.Header.Version);
		int entitiesAfter = back.BlockRecords.SelectMany(b => b.Entities).Count();
		Assert.True(entitiesAfter > 0, "the round trip kept no entity at all");
		Assert.True(entitiesAfter >= entitiesBefore - entitiesBefore / 10,
			$"the round trip lost too much: {entitiesBefore} -> {entitiesAfter}");
	}
}
