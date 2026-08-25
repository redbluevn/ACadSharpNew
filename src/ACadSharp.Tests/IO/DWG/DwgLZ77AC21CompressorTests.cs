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
