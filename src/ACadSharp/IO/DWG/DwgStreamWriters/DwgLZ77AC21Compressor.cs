using System;
using System.IO;

namespace ACadSharp.IO.DWG
{
	/// <summary>
	/// A store-only compressor for the R2007 flavour of LZ77: it emits one literal run covering
	/// the whole payload and never a single match, so the "compressed" stream is a few opcode
	/// bytes plus the payload itself. That is a valid stream by construction - the decompressor's
	/// literal path is exercised for every length - and it sidesteps ever having to invert the
	/// match encoder. The literal bytes are stored pre-shuffled, because
	/// <see cref="DwgLZ77AC21Decompressor"/> copies literals through a byte permutation (32-byte
	/// chunks of swapped 4-byte groups, and a bespoke permutation per remainder length); the
	/// permutations are CALIBRATED at first use by running the decompressor itself over marker
	/// bytes, so this class cannot disagree with it.
	/// </summary>
	internal class DwgLZ77AC21Compressor : ICompressor
	{
		private static readonly Lazy<int[][]> _permutations = new(calibrate);

		public void Compress(byte[] source, int offset, int totalSize, Stream dest)
		{
			byte[] payload = new byte[totalSize];
			Array.Copy(source, offset, payload, 0, totalSize);

			byte[] header = literalHeader(totalSize);
			dest.Write(header, 0, header.Length);

			byte[] shuffled = shuffle(payload);
			dest.Write(shuffled, 0, shuffled.Length);
		}

		//The opcode bytes that make the decompressor read one literal run of exactly this length.
		private static byte[] literalHeader(int length)
		{
			if (length < 8)
			{
				//The short-literal path: an initial opcode with high nibble 2 makes Decompress
				//skip three bytes and take the length from the low three bits of the third.
				return new byte[] { 0x20, 0x00, 0x00, (byte)length };
			}

			if (length <= 0x16)
			{
				return new byte[] { (byte)(length - 8) };
			}

			//Opcode 0x0F: length starts at 0x17 and extends by a byte, then by 16-bit words for
			//as long as each word reads 0xFFFF.
			using MemoryStream ms = new();
			ms.WriteByte(0x0F);
			int rest = length - 0x17;
			if (rest < 0xFF)
			{
				ms.WriteByte((byte)rest);
			}
			else
			{
				ms.WriteByte(0xFF);
				rest -= 0xFF;
				while (true)
				{
					int word = Math.Min(rest, 0xFFFF);
					ms.WriteByte((byte)(word & 0xFF));
					ms.WriteByte((byte)(word >> 8));
					rest -= word;
					if (word != 0xFFFF)
					{
						break;
					}
				}
			}

			return ms.ToArray();
		}

		//data -> the byte order the decompressor's literal copy turns back into data.
		private static byte[] shuffle(byte[] data)
		{
			int[][] perms = _permutations.Value;
			byte[] output = new byte[data.Length];
			int chunkCount = data.Length / 32;
			int[] chunk = perms[32];
			for (int c = 0; c < chunkCount; c++)
			{
				int b = c * 32;
				for (int i = 0; i < 32; i++)
				{
					//The decompressor writes dst[i] = src[chunk[i]], so place data[i] at chunk[i].
					output[b + chunk[i]] = data[b + i];
				}
			}

			int remainder = data.Length - chunkCount * 32;
			if (remainder > 0)
			{
				int b = chunkCount * 32;
				int[] perm = perms[remainder];
				for (int i = 0; i < remainder; i++)
				{
					output[b + perm[i]] = data[b + i];
				}
			}

			return output;
		}

		//Learn dst->src maps by decompressing literal runs of marker bytes: for a run of length L
		//the decompressor produces out[i] = src[p(i)], so feeding src = 0..L-1 reads p directly.
		//Lengths 33..63 expose the 32-chunk map and every remainder map; length 32 exposes the
		//pure chunk map (which the 33..63 runs confirm).
		private static int[][] calibrate()
		{
			int[][] perms = new int[33][];

			for (int r = 1; r <= 32; r++)
			{
				int total = r == 32 ? 32 : 32 + r;
				byte[] src = new byte[total];
				for (int i = 0; i < total; i++)
				{
					src[i] = (byte)i;
				}

				byte[] header = literalHeader(total);
				byte[] stream = new byte[header.Length + total];
				header.CopyTo(stream, 0);
				src.CopyTo(stream, header.Length);

				byte[] output = new byte[total];
				new DwgLZ77AC21Decompressor().Decompress(stream, 0U, (uint)stream.Length, output);

				if (r == 32)
				{
					int[] p = new int[32];
					for (int i = 0; i < 32; i++)
					{
						p[i] = output[i];
					}

					perms[32] = p;
				}
				else
				{
					int[] p = new int[r];
					for (int i = 0; i < r; i++)
					{
						//The remainder starts after the first full chunk of 32.
						p[i] = output[32 + i] - 32;
					}

					perms[r] = p;
				}
			}

			return perms;
		}
	}
}
