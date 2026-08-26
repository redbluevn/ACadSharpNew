using ACadSharp.IO.DWG.DwgStreamWriters;
using System;
using Xunit;

namespace ACadSharp.Tests.IO.DWG
{
	/// <summary>
	/// Pins the R2007 64-bit CRC against values a real AutoCAD file carries.
	/// The block ordering of ODA 5.12 is recursive - 4 is "2[2],2[0]" and 8 is "4[4],4[0]",
	/// each sub-block expanding by the same table - so eight bytes go in the order
	/// {6,7,4,5,2,3,0,1}. Reading the table as flat gives {4,5,6,7,0,1,2,3}, under which no
	/// field of a real file reproduces, which is what made the whole CRC family look decorative.
	/// </summary>
	public class Dwg21Crc64FieldTests
	{
		//A real file's header page carries the checking sequence's first value at block+0x08 and
		//its CRC at block+0x00 (ODA 5.2.1.4). Two independent files, two independent pairs.
		[Theory]
		[InlineData(0x0ded70157c3b7602UL, 0x7fe844dbc928b006UL)]
		[InlineData(0x1331f9e456ae9baaUL, 0x4f704f98c5aa401fUL)]
		public void CheckingSequenceCrcMatchesRealFile(ulong checkValue, ulong expected)
		{
			byte[] sequence = new byte[16];
			BitConverter.GetBytes(checkValue).CopyTo(sequence, 0);
			BitConverter.GetBytes(Dwg21FileHeaderCheckData.Encode(checkValue, checkValue)).CopyTo(sequence, 8);

			ulong crc = Dwg21Crc64.Normal(
				Dwg21Crc64.UpdateSeed1(0, (uint)sequence.Length), sequence, 0, sequence.Length);

			Assert.Equal(expected, crc);
		}

		[Fact]
		public void SeedUpdatesArePinned()
		{
			Assert.Equal(0x4211f0f5ffa5216cUL, Dwg21Crc64.UpdateSeed1(0, 16));
			Assert.Equal(0xfc61189a45a9e6e5UL, Dwg21Crc64.UpdateSeed2(0, 272));
		}

		//Exercises the eight-byte path on its own: under the flat ordering these two come out
		//different, so they are the guard that keeps the recursive table in place.
		[Fact]
		public void EightByteBlockOrderingIsPinned()
		{
			byte[] data = { 1, 2, 3, 4, 5, 6, 7, 8 };

			Assert.Equal(0x1ea46edb2e6440f2UL, Dwg21Crc64.Normal(0, data, 0, data.Length));
			Assert.Equal(0x016b2100efca1eb3UL, Dwg21Crc64.Mirrored(0, data, 0, data.Length));
		}

		//A length that is not a multiple of eight, so the remainder table is exercised too.
		[Fact]
		public void MixedLengthBufferIsPinned()
		{
			byte[] data = new byte[37];
			for (int i = 0; i < data.Length; i++)
			{
				data[i] = (byte)((i * 37) + 11);
			}

			Assert.Equal(0x375fc8a0ed35d126UL, Dwg21Crc64.Mirrored(
				Dwg21Crc64.UpdateSeed1(0, (uint)data.Length), data, 0, data.Length));
			Assert.Equal(0x4874cf1f1eff1bd7UL, Dwg21Crc64.Normal(
				Dwg21Crc64.UpdateSeed2(0, (uint)data.Length), data, 0, data.Length));
		}
	}
}
