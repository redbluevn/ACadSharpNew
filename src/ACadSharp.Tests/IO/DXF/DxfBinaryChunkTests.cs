using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.IO.DXF;
using ACadSharp.IO.DXF.DxfStreamWriter;
using ACadSharp.Objects;
using ACadSharp.Tables;
using ACadSharp.XData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace ACadSharp.Tests.IO.DXF;

public class DxfBinaryChunkTests
{
	public static TheoryData<bool, int, int> ChunkCases
	{
		get
		{
			TheoryData<bool, int, int> cases = new();
			foreach (bool binary in new[] { false, true })
			foreach (int code in new[] { 310, 1004 })
			foreach (int length in new[] { 0, 1, 127, 128, 254, 255, 256, 1024 })
			{
				cases.Add(binary, code, length);
			}

			return cases;
		}
	}

	[Theory]
	[MemberData(nameof(ChunkCases))]
	public void ChunksPreserveTheirBytesAndTheFollowingGroup(bool binary, int code, int length)
	{
		byte[] payload = createPayload(length);
		using MemoryStream stream = new();
		using (IDxfStreamWriter writer = binary
			? new DxfBinaryWriter(new BinaryWriter(stream, Encoding.ASCII, true))
			: new DxfAsciiWriter(new StreamWriter(stream, Encoding.ASCII, 1024, true)))
		{
			writer.Write(code, payload);
			writer.Write(0, "EOF");
			writer.Flush();
		}

		stream.Position = 0;
		List<byte> actual = new();
		int chunks = 0;
		// Inspect the emitted groups independently of the DXF reader: round-trip agreement alone
		// does not prove that the chunk lengths and the next group's byte boundary are valid.
		if (binary)
		{
			using BinaryReader reader = new(stream, Encoding.ASCII, true);
			Assert.Equal(DxfBinaryReader.Sentinel, Encoding.ASCII.GetString(reader.ReadBytes(22)));
			int nextCode;
			while ((nextCode = reader.ReadInt16()) == code)
			{
				int size = reader.ReadByte();
				Assert.InRange(size, 0, 127);
				byte[] chunk = reader.ReadBytes(size);
				Assert.Equal(size, chunk.Length);
				actual.AddRange(chunk);
				chunks++;
			}

			Assert.Equal(0, nextCode);
			Assert.Equal(new byte[] { (byte)'E', (byte)'O', (byte)'F', 0 }, reader.ReadBytes(4));
			Assert.Equal(stream.Length, stream.Position);
		}
		else
		{
			using StreamReader reader = new(stream, Encoding.ASCII, false, 1024, true);
			int nextCode;
			while ((nextCode = int.Parse(reader.ReadLine(), CultureInfo.InvariantCulture)) == code)
			{
				string hex = reader.ReadLine();
				Assert.InRange(hex.Length, 0, 254);
				Assert.Equal(0, hex.Length % 2);
				for (int i = 0; i < hex.Length; i += 2)
				{
					actual.Add(byte.Parse(hex.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
				}
				chunks++;
			}

			Assert.Equal(0, nextCode);
			Assert.Equal("EOF", reader.ReadLine());
			Assert.Null(reader.ReadLine());
		}

		Assert.True(chunks > 0, "An empty payload must still have its group/value pair.");
		Assert.Equal(payload, actual.ToArray());
	}

	[Theory]
	[InlineData(false, 0)]
	[InlineData(false, 1024)]
	[InlineData(true, 0)]
	[InlineData(true, 1024)]
	public void XRecordAndXDataPayloadsSurviveWithTheirFollowingValues(bool binary, int length)
	{
		byte[] payload = createPayload(length);
		CadDocument document = new(ACadVersion.AC1032);
		XRecord record = new();
		record.CreateEntry(310, payload);
		record.CreateEntry(1, "after the binary data");
		document.RootDictionary.Add("BINARY_DATA", record);

		Line line = new();
		AppId app = new("BINARY_DATA_APP");
		line.ExtendedData.Add(app, new ExtendedData(new ExtendedDataRecord[]
		{
			new ExtendedDataBinaryChunk(payload),
			new ExtendedDataString("after the binary data"),
		}));
		document.Entities.Add(line);

		using MemoryStream stream = new();
		using (DxfWriter writer = new(stream, document, binary))
		{
			writer.Write();
		}

		CadDocument result = DxfReader.Read(new MemoryStream(stream.ToArray()));
		Assert.True(result.RootDictionary.TryGetEntry("BINARY_DATA", out XRecord actual));
		Assert.Equal(payload, actual.Entries.Where(e => e.Code == 310).SelectMany(e => (byte[])e.Value).ToArray());
		Assert.Equal("after the binary data", actual.Entries.Single(e => e.Code == 1).Value);

		Line actualLine = Assert.Single(result.Entities.OfType<Line>());
		Assert.True(actualLine.ExtendedData.TryGet(app.Name, out ExtendedData actualData));
		Assert.NotEmpty(actualData.Records.OfType<ExtendedDataBinaryChunk>());
		Assert.Equal(payload, actualData.Records.OfType<ExtendedDataBinaryChunk>().SelectMany(c => c.Value).ToArray());
		Assert.Equal("after the binary data", actualData.Records.OfType<ExtendedDataString>().Single().Value);
	}

	[Fact]
	public void AsciiChunksKeepExactBoundariesAcrossSuccessivePayloads()
	{
		using MemoryStream stream = new();
		using (DxfAsciiWriter writer = new(new StreamWriter(stream, Encoding.ASCII, 1024, true)))
		{
			writer.Write(310, Enumerable.Repeat((byte)0xAB, 254).ToArray());
			writer.Write(1004, new byte[] { 0x01, 0xEF });
			writer.Write(310, Array.Empty<byte>());
			writer.Write(310, new byte[] { 0x90 });
			writer.Write(0, "EOF");
			writer.Flush();
		}

		stream.Position = 0;
		using StreamReader reader = new(stream, Encoding.ASCII);
		string fullChunk = string.Concat(Enumerable.Repeat("AB", 127));
		// A short or empty value after a full buffer must not retain the preceding payload's tail.
		foreach (string expected in new[] { "310", fullChunk, "310", fullChunk, "1004", "01EF", "310", "", "310", "90", "  0", "EOF" })
		{
			Assert.Equal(expected, reader.ReadLine());
		}
		Assert.Null(reader.ReadLine());
	}

	private static byte[] createPayload(int length)
	{
		return Enumerable.Range(0, length).Select(i => (byte)(i % 256)).ToArray();
	}
}
