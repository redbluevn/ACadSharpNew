namespace ACadSharp.Tests.IO;

using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.IO;
using CSMath;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// DXF carries a control character inside a string as a caret pair - a line break is written
/// <c>^J</c> - and AutoCAD writes them: its own export of sample_AC1032 holds
/// <c>this is a Mtext^Jwith multiple lines in it</c>. The writer here has always encoded them; the
/// reader decoded them in ValueAsString and then the class-map assignment path read Value instead,
/// so every string it assigned kept the carets.
/// </summary>
public class DxfControlCharacterTests
{
	[Fact]
	public void ALineBreakInAToleranceSurvivesDxf()
	{
		CadDocument doc = DwgReader.Read(Path.Combine(TestVariables.SamplesFolder, "sample_AC1032.dwg"));
		string[] before = this.tolerances(doc);
		Assert.Contains(before, t => t.Contains('\n'));

		Assert.Equal(before, this.tolerances(this.roundTripDxf(doc)));
	}

	[Theory]
	[InlineData("two\nlines")]
	[InlineData("tab\there")]
	[InlineData("a caret ^ on its own")]
	[InlineData("^J looking like an escape")]
	public void AnMTextKeepsItsExactCharactersThroughDxf(string value)
	{
		//The last case is the one that makes the encoding worth having: text that genuinely reads
		//"^J" has to come back as those two characters, not as a line break.
		CadDocument doc = new CadDocument(ACadVersion.AC1032);
		doc.Entities.Add(new MText { Value = value, InsertPoint = XYZ.Zero });

		MText back = this.roundTripDxf(doc).Entities.OfType<MText>().Single();

		Assert.Equal(value, back.Value);
	}

	[Fact]
	public void TheCharactersAreCountedNotJustCompared()
	{
		//A length check catches the failure this guards even if the comparison above were loosened:
		//the tolerance grew from 63 characters to 66, two of them standing where each break was.
		CadDocument doc = DwgReader.Read(Path.Combine(TestVariables.SamplesFolder, "sample_AC1032.dwg"));
		int[] before = this.tolerances(doc).Select(t => t.Length).OrderBy(n => n).ToArray();

		int[] after = this.tolerances(this.roundTripDxf(doc)).Select(t => t.Length).OrderBy(n => n).ToArray();

		Assert.Equal(before, after);
	}

	[Theory]
	[InlineData("plain")]
	[InlineData("two\nlines")]
	[InlineData("caret ^ inside")]
	[InlineData("^J looking like an escape")]
	public void AnXRecordStringKeepsItsExactCharactersThroughDxf(string value)
	{
		//An XRecord is where a dynamic block keeps its state: the active visibility state of an
		//instance is code 1 of a record under ACAD_ENHANCEDBLOCKDATA. Before this, a value holding a
		//caret came back with a space after it and one holding a line break came back holding the
		//two characters ^ and J, so any application reading that state got a name that did not match
		//the one the definition declares.
		CadDocument doc = new CadDocument(ACadVersion.AC1032);
		XRecord record = new XRecord();
		record.CreateEntry(1, value);
		doc.RootDictionary.Add("CONTROL_CHARS", record);

		CadDocument back = this.roundTripDxf(doc);

		Assert.True(back.RootDictionary.TryGetEntry("CONTROL_CHARS", out XRecord got));
		Assert.Equal(value, got.Entries.Single(e => e.Code == 1).Value);
	}

	private string[] tolerances(CadDocument doc)
	{
		return doc.Entities.OfType<Tolerance>().Select(t => t.Text).OrderBy(t => t).ToArray();
	}

	private CadDocument roundTripDxf(CadDocument doc)
	{
		doc.Header.Version = ACadVersion.AC1032;
		using MemoryStream stream = new();
		using (DxfWriter writer = new(stream, doc, false))
		{
			writer.Write();
		}

		return DxfReader.Read(new MemoryStream(stream.ToArray()));
	}
}
