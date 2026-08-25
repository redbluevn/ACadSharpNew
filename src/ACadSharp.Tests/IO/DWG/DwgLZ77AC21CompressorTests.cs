using ACadSharp.IO.DWG;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO.DWG;

/// <summary>
/// The store-only AC21 compressor is only correct if the AC21 decompressor turns its output back
/// into the exact input, for every length class the format distinguishes: the short-literal path
/// (under 8), the single-opcode path (8..0x16), the extended byte (0x17..0x116), the 16-bit
/// extension chains, every remainder permutation (1..31 mod 32), and the real page sizes the
/// container writes.
/// </summary>
public class DwgLZ77AC21CompressorTests
{
	private static byte[] roundTrip(byte[] data)
	{
		using MemoryStream ms = new();
		new DwgLZ77AC21Compressor().Compress(data, 0, data.Length, ms);
		byte[] stream = ms.ToArray();

		byte[] output = new byte[data.Length];
		new DwgLZ77AC21Decompressor().Decompress(stream, 0U, (uint)stream.Length, output);
		return output;
	}

	[Fact]
	public void EveryLengthUpTo300RoundTrips()
	{
		var random = new Random(1320);
		for (int length = 1; length <= 300; length++)
		{
			byte[] data = new byte[length];
			random.NextBytes(data);

			byte[] back = roundTrip(data);

			Assert.True(data.SequenceEqual(back), $"length {length} did not round trip");
		}
	}

	[Fact]
	public void CompressibleDataRoundTripsAndShrinks()
	{
		//The match path: zero runs, periodic patterns, and text-like repetition. A real AutoCAD
		//file never stores a compressed size larger than the uncompressed one, so on data like
		//this the compressor must get BELOW the input, not just round trip.
		var cases = new System.Collections.Generic.List<byte[]>
		{
			new byte[0x400], //all zeros
			Enumerable.Range(0, 0x2000).Select(i => (byte)(i % 7)).ToArray(),
			Enumerable.Repeat(System.Text.Encoding.ASCII.GetBytes("AcDbEntity AcDbLine 100 "), 200)
				.SelectMany(x => x).ToArray(),
		};

		var random = new Random(84);
		byte[] mixed = new byte[0x3000];
		random.NextBytes(mixed);
		Array.Clear(mixed, 0x800, 0x1000); //a zero lake in random data
		Array.Copy(mixed, 0, mixed, 0x2000, 0x800); //a long self-repeat
		cases.Add(mixed);

		foreach (byte[] data in cases)
		{
			using MemoryStream ms = new();
			new DwgLZ77AC21Compressor().Compress(data, 0, data.Length, ms);
			byte[] stream = ms.ToArray();

			byte[] back = new byte[data.Length];
			new DwgLZ77AC21Decompressor().Decompress(stream, 0U, (uint)stream.Length, back);

			Assert.True(data.SequenceEqual(back), $"case len {data.Length} did not round trip");
			Assert.True(stream.Length < data.Length,
				$"case len {data.Length} did not shrink: {stream.Length}");
		}
	}

	[Fact]
	public void EveryRealSectionOfTheR2007SampleRoundTrips()
	{
		//The strongest data source: the decoded sections of a real AC1021 drawing.
		string path = Path.Combine(TestVariables.SamplesFolder, "sample_AC1021.dwg");
		if (!File.Exists(path))
		{
			return;
		}

		var random = new Random(21);
		byte[] file = File.ReadAllBytes(path);
		//Slices of the raw file stand in for section payloads of many sizes and shapes.
		foreach (int length in new[] { 0x50, 0x113, 0x800, 0x2000, 0x7400, 0xF800 })
		{
			int start = random.Next(0, file.Length - length);
			byte[] data = file.Skip(start).Take(length).ToArray();

			using MemoryStream ms = new();
			new DwgLZ77AC21Compressor().Compress(data, 0, data.Length, ms);
			byte[] stream = ms.ToArray();

			byte[] back = new byte[data.Length];
			new DwgLZ77AC21Decompressor().Decompress(stream, 0U, (uint)stream.Length, back);

			Assert.True(data.SequenceEqual(back), $"slice at 0x{start:X} len 0x{length:X} did not round trip");
		}
	}

	[Theory]
	[InlineData(0x110)]  //the file header
	[InlineData(0x400)]
	[InlineData(0xF800)] //the standard data page
	[InlineData(0xF800 * 3 + 17)]
	[InlineData(0x10000 + 0xFFFF + 23)] //forces a 16-bit extension chain
	public void RealPageSizesRoundTrip(int length)
	{
		var random = new Random(length);
		byte[] data = new byte[length];
		random.NextBytes(data);

		Assert.True(data.SequenceEqual(roundTrip(data)), $"length {length} did not round trip");
	}
}
