namespace ACadSharp.Tests.IO;

using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// A book colour - RAL, Pantone and the like - is named "&lt;book&gt;$&lt;colour&gt;" and an entity
/// points at it by that name. The DXF entity writer has always written the name as it stands; the
/// object writer appended the book a second time, so the two ends disagreed and every entity using
/// a book colour lost it on a DXF round trip.
/// </summary>
public class BookColorRoundTripTests
{
	[Fact]
	public void TheColourTableKeepsItsNameThroughDxf()
	{
		CadDocument doc = DwgReader.Read(Path.Combine(TestVariables.SamplesFolder, "sample_AC1032.dwg"));
		string[] before = this.colourNames(doc);
		Assert.NotEmpty(before);

		Assert.Equal(before, this.colourNames(this.roundTripDxf(doc)));
	}

	[Fact]
	public void AnEntityKeepsItsBookColourThroughDxf()
	{
		CadDocument doc = DwgReader.Read(Path.Combine(TestVariables.SamplesFolder, "sample_AC1032.dwg"));
		Entity source = doc.Entities.First(e => e.BookColor != null);
		string name = source.BookColor.Name;
		Assert.Contains("$", name);

		CadDocument back = this.roundTripDxf(doc);

		Entity[] coloured = back.Entities.Where(e => e.BookColor != null).ToArray();
		Assert.NotEmpty(coloured);
		Assert.Contains(coloured, e => e.BookColor.Name == name);
	}

	[Fact]
	public void TheBookAndTheColourAreNotConfusedForEachOther()
	{
		//The failure this guards is not "the name is missing" but "the name came back as the book
		//twice", which a count of colours would not notice.
		CadDocument doc = DwgReader.Read(Path.Combine(TestVariables.SamplesFolder, "sample_AC1032.dwg"));

		foreach (BookColor c in this.roundTripDxf(doc).Colors)
		{
			Assert.NotEqual(c.BookName, c.ColorName);
		}
	}

	private string[] colourNames(CadDocument doc)
	{
		return doc.Colors.Select(c => c.Name).OrderBy(n => n).ToArray();
	}

	private CadDocument roundTripDxf(CadDocument doc)
	{
		using MemoryStream stream = new();
		using (DxfWriter writer = new(stream, doc, false))
		{
			writer.Write();
		}

		return DxfReader.Read(new MemoryStream(stream.ToArray()));
	}
}
