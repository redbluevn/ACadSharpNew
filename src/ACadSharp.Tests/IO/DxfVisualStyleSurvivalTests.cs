namespace ACadSharp.Tests.IO;

using ACadSharp.IO;
using ACadSharp.Objects;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// A visual style read from a drawing older than R2013 keeps its named fields instead of the
/// positional property list, and DXF stores only the list. The DWG writer has assembled the list
/// from those fields since the day a drawing carried forward from an older version was found to
/// lose every visual style it had; the DXF writer kept dropping them, on a comment saying there was
/// "nothing to write".
/// </summary>
public class DxfVisualStyleSurvivalTests
{
	[Theory]
	[InlineData("sample_AC1018.dwg")]   //R2004: named fields, no list at all
	[InlineData("sample_AC1024.dwg")]   //R2010: a 28 entry list, which is not the 58 DXF wants
	[InlineData("sample_AC1032.dwg")]   //R2018: already the full list
	public void VisualStylesSurviveADxfRoundTripFromEveryVersion(string sample)
	{
		CadDocument doc = DwgReader.Read(Path.Combine(TestVariables.SamplesFolder, sample));
		int before = this.count(doc);
		Assert.Equal(24, before);

		using MemoryStream stream = new();
		using (DxfWriter writer = new(stream, doc, false))
		{
			writer.Write();
		}

		Assert.Equal(before, this.count(DxfReader.Read(new MemoryStream(stream.ToArray()))));
	}

	[Fact]
	public void TheStylesKeepTheirNamesAndNotJustTheirCount()
	{
		//A count alone would pass if the writer emitted twenty-four empty styles.
		CadDocument doc = DwgReader.Read(Path.Combine(TestVariables.SamplesFolder, "sample_AC1018.dwg"));
		string[] before = this.names(doc);

		using MemoryStream stream = new();
		using (DxfWriter writer = new(stream, doc, false))
		{
			writer.Write();
		}

		Assert.Equal(before, this.names(DxfReader.Read(new MemoryStream(stream.ToArray()))));
	}

	private string[] names(CadDocument doc)
	{
		doc.RootDictionary.TryGetEntry(CadDictionary.AcadVisualStyle, out CadDictionary d);
		return d.OfType<VisualStyle>().Select(v => v.Name).OrderBy(n => n).ToArray();
	}

	private int count(CadDocument doc)
	{
		if (!doc.RootDictionary.TryGetEntry(CadDictionary.AcadVisualStyle, out CadDictionary d))
		{
			return 0;
		}

		return d.OfType<VisualStyle>().Count();
	}
}
