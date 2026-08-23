using ACadSharp.IO;
using ACadSharp.IO.DWG;
using ACadSharp.IO.DWG.DwgStreamReaders;
using ACadSharp.IO.DWG.DwgStreamWriters;
using ACadSharp.Prototype1b;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace ACadSharp.Tests.IO;

/// <summary>
/// The AcDs data section on its own, without a drawing around it: built by the writer and read back
/// by the reader that has always parsed AutoCAD's. Keeping this apart from a full DWG round trip is
/// what separates "the section is malformed" from "the section never reached the file intact".
/// </summary>
public class AcDsSectionWriteTests
{
	[Fact]
	public void TheSectionIsReadBackByTheReaderThatParsesAutoCadsOwn()
	{
		var payloads = new List<DwgPrototype1bWriter.Payload>
		{
			new(0x2A, Encoding.ASCII.GetBytes("ASM BinaryFile first payload")),
			new(0x2B, Encoding.ASCII.GetBytes("ASM BinaryFile second payload, a little longer")),
		};

		DataStorage storage = this.roundTrip(payloads);

		Assert.NotNull(storage.SchemaFields);
		Assert.NotNull(storage.DataFields);
		Assert.NotNull(storage.SchemaSearch);

		Schema acis = storage.GetSchemaByName(Schema.ACIS);
		Assert.NotNull(acis);

		List<DataEntry> entries = storage.GetSchemaData(acis);
		Assert.Equal(payloads.Count, entries.Count);
		foreach (DwgPrototype1bWriter.Payload payload in payloads)
		{
			DataEntry entry = entries.Single(e => e.Header.Handle == payload.Handle);
			Assert.Equal(payload.Data, entry.Value.Data);
		}
	}

	[Fact]
	public void EverySchemaAutoCadDefinesIsPresent()
	{
		//A store carrying only the ASM schema is not what AutoCAD writes, and the DXF form of this
		//section was already measured to want the whole set.
		DataStorage storage = this.roundTrip(new List<DwgPrototype1bWriter.Payload>
		{
			new(0x2A, Encoding.ASCII.GetBytes("ASM BinaryFile payload")),
		});

		foreach (string name in new[]
		{
			Schema.THUMBNAIL,
			Schema.TREATED_AS_OBJECT_DATA,
			Schema.LEGACY,
			Schema.INDEXED_PROPERTY,
			Schema.HANDLE_ATTRIBUTE,
			Schema.ACIS,
		})
		{
			Assert.NotNull(storage.GetSchemaByName(name));
		}
	}

	[Fact]
	public void APayloadOfAnyLengthKeepsItsBytes()
	{
		//Segment sizes are rounded up to a boundary and the records are padded apart, so a length
		//that lands exactly on a boundary is the one most likely to be read back a few bytes short.
		var payloads = new List<DwgPrototype1bWriter.Payload>();
		foreach (int length in new[] { 1, 127, 128, 129, 4096, 40000 })
		{
			byte[] data = new byte[length];
			for (int i = 0; i < length; i++)
			{
				data[i] = (byte)(i % 251);
			}

			payloads.Add(new DwgPrototype1bWriter.Payload((ulong)(0x100 + length), data));
		}

		DataStorage storage = this.roundTrip(payloads);
		List<DataEntry> entries = storage.GetSchemaData(Schema.ACIS);

		Assert.Equal(payloads.Count, entries.Count);
		foreach (DwgPrototype1bWriter.Payload payload in payloads)
		{
			DataEntry entry = entries.Single(e => e.Header.Handle == payload.Handle);
			Assert.Equal(payload.Data, entry.Value.Data);
		}
	}

	private DataStorage roundTrip(IList<DwgPrototype1bWriter.Payload> payloads)
	{
		MemoryStream written = new DwgPrototype1bWriter(payloads).Write();

		MemoryStream input = new(written.ToArray());
		var builder = new DwgDocumentBuilder(ACadVersion.AC1032, new CadDocument(), new DwgReaderConfiguration { Failsafe = false });
		var reader = new DwgPrototype1bReader(
			ACadVersion.AC1032,
			builder,
			DwgStreamReaderBase.GetStreamHandler(ACadVersion.AC1032, input));

		return reader.Read();
	}
}
