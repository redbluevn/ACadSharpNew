using ACadSharp.IO.DWG;
using ACadSharp.IO.DWG.DwgStreamWriters;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO.DWG;

/// <summary>
/// The write-side R2007 codecs are validated against a real AC1021 file before any file is
/// written with them: the file header of samples/sample_AC1021.dwg stores the random seed, the
/// check data and the CRCs that AutoCAD wrote, so every codec must reproduce those exact values.
/// The spec's pseudocode carries typos (UInt3264, a stray |=), which is why measured ground truth
/// decides, not the book.
///
/// Two negatives and one tolerance, all measured on 2026-08-25 and recorded so nobody re-derives
/// them: (a) the CRC64 seed/ordering conventions of the stored header CRCs resisted 80+ variant
/// combinations AND an affine-algebra seed solve - the solved seeds match no repair of the
/// garbled UpdateSeed pseudocode; (b) flipping a bit in the header page's check-data tail leaves
/// AutoCAD 2027 opening the file with AUDIT 0 - those CRCs are DECORATIVE to AutoCAD; (c) a bit
/// error inside the RS-coded header payload is also tolerated. So the writer emits best-effort
/// CRC values and exact RS parity, and the two tests below pin the two codecs that are provably
/// load-bearing.
/// </summary>
public class Dwg21CodecProbeTests
{
	private static string samplePath => Path.Combine(TestVariables.SamplesFolder, "sample_AC1021.dwg");

	private static byte[] readFile() => File.ReadAllBytes(samplePath);

	//The reader's de-interleave, reproduced here in miniature: byte j of block b sits at
	//position b + factor * j.
	private static byte[] deinterleave(byte[] encoded, int factor, int blockSize, int outputLength)
	{
		byte[] output = new byte[outputLength];
		int index = 0;
		for (int b = 0; b < factor && index < outputLength; b++)
		{
			int take = Math.Min(outputLength - index, blockSize);
			for (int j = 0; j < take; j++)
			{
				output[index++] = encoded[b + factor * j];
			}
		}

		return output;
	}

	private static byte[] decodeFileHeaderPage(byte[] file)
	{
		//0x80..0x480: the header page. First 0x3D8 bytes are RS(255,239) encoded with factor 3.
		byte[] page = file.Skip(0x80).Take(0x400).ToArray();
		byte[] decoded = deinterleave(page, 3, 239, 3 * 239);

		//0x18 ComprLen, 0x20.. compressed data, decompressed size fixed 0x110.
		int comprLen = BitConverter.ToInt32(decoded, 24);
		Assert.True(comprLen > 0, "the sample's file header is compressed");
		byte[] header = new byte[0x110];
		new DwgLZ77AC21Decompressor().Decompress(decoded, 32U, (uint)comprLen, header);
		return header;
	}

	
	[Fact]
	public void TheRandomEncoderGeneratorMatchesARealFilesStream()
	{
		//The check data was written from the encoder's CURRENT state after an unknown amount of
		//consumption, so a fresh encoder cannot reproduce it positionally. What IS invariant: the
		//two stored randoms must be consecutive draws of the stream seeded with the stored
		//RandomSeed. Measured: random1 at table index 248, random2 at 250 - one NextUInt64 apart.
		byte[] file = readFile();
		byte[] header = decodeFileHeaderPage(file);
		ulong randomSeed = BitConverter.ToUInt64(header, 0x100);

		long checkBase = 0x80 + 0x3D8;
		ulong storedRandom1 = BitConverter.ToUInt64(file, (int)checkBase + 16);
		ulong storedRandom2 = BitConverter.ToUInt64(file, (int)checkBase + 24);

		var encoder = new Dwg21RandomEncoder(randomSeed);
		ulong previous = 0;
		bool found = false;
		for (int i = 0; i < 0x270; i++)
		{
			ulong draw = encoder.NextUInt64();
			if (previous == storedRandom1 && draw == storedRandom2)
			{
				found = true;
				break;
			}

			previous = draw;
		}

		Assert.True(found, "the stored check randoms are consecutive draws of the seeded stream");
	}

	
	
	[Fact]
	public void TheSystemPageReedSolomonParityMatchesARealFile()
	{
		//The header page's first 0x3D8 bytes are three interleaved RS(255,239) codewords: byte j
		//of block b at position b + 3j. Recompute each block's parity from its 239 data bytes and
		//compare with the parity AutoCAD wrote.
		byte[] file = readFile();
		byte[] page = file.Skip(0x80).Take(0x3D8).ToArray();

		for (int b = 0; b < 3; b++)
		{
			byte[] block = new byte[239];
			for (int j = 0; j < 239; j++)
			{
				block[j] = page[b + 3 * j];
			}

			byte[] parity = Dwg21ReedSolomon.SystemPages.BlockParity(block, 0);
			for (int j = 0; j < parity.Length; j++)
			{
				Assert.Equal(page[b + 3 * (239 + j)], parity[j]);
			}
		}
	}

	
	}
