using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Tables;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO;

public class XRecordNullReferenceTests
{
	public static TheoryData<ACadVersion> Versions => new TheoryData<ACadVersion>
	{
		ACadVersion.AC1015,
		ACadVersion.AC1018,
		ACadVersion.AC1024,
		ACadVersion.AC1032,
	};

	[Theory]
	[MemberData(nameof(Versions))]
	public void DwgKeepsAnEntryWithNoReference(ACadVersion version)
	{
		CadDocument doc = this.document(version);

		MemoryStream ms = new MemoryStream();
		using (DwgWriter writer = new DwgWriter(ms, doc))
		{
			writer.Write();
		}

		this.assertRecord(DwgReader.Read(new MemoryStream(ms.ToArray())));
	}

	[Theory]
	[MemberData(nameof(Versions))]
	public void DxfKeepsAnEntryWithNoReference(ACadVersion version)
	{
		//A record is positional: an entry that links to nothing still holds its place, and AutoCAD
		//writes the handle 0 for it. Dropping the entry moves every entry after it one place up.
		CadDocument doc = this.document(version);

		MemoryStream ms = new MemoryStream();
		using (DxfWriter writer = new DxfWriter(ms, doc, false))
		{
			writer.Write();
		}

		this.assertRecord(DxfReader.Read(new MemoryStream(ms.ToArray())));
	}

	private CadDocument document(ACadVersion version)
	{
		CadDocument doc = new CadDocument();
		doc.Header.Version = version;

		Layer layer = new Layer("my_layer");
		doc.Layers.Add(layer);

		CadDictionary states = new CadDictionary("ACAD_LAYERSTATES");
		layer.CreateExtendedDictionary().Add(states);

		XRecord record = new XRecord("test");
		record.CreateEntry(90, 1);
		record.CreateEntry(330, null);
		record.CreateEntry(1, "after the null");
		record.CreateEntry(330, doc.Layers);
		states.Add(record);

		return doc;
	}

	private void assertRecord(CadDocument doc)
	{
		Layer layer = doc.Layers["my_layer"];
		CadDictionary states = (CadDictionary)layer.XDictionary["ACAD_LAYERSTATES"];
		XRecord record = (XRecord)states["test"];

		XRecord.Entry[] entries = record.Entries.ToArray();
		Assert.Equal(new[] { 90, 330, 1, 330 }, entries.Select(e => e.Code).ToArray());
		Assert.Null(entries[1].Value);
		Assert.Equal("after the null", entries[2].Value);
		Assert.NotNull(entries[3].GetReference());
	}
}
