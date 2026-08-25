using System;
using System.Collections.Generic;
using System.IO;

namespace ACadSharp.IO.DWG
{
	/// <summary>
	/// A real compressor for the R2007 flavour of LZ77, built as the exact inverse of
	/// <see cref="DwgLZ77AC21Decompressor"/>: greedy hash-chain matching emits the decompressor's
	/// own instruction forms, literals ride either in the three low bits of a match instruction
	/// (lengths 1..7) or as explicit literal opcodes (lengths 8 and up, with the 0x0F extension
	/// chain), and literal payload bytes are stored pre-shuffled through permutations CALIBRATED
	/// against the decompressor itself, so the two cannot disagree. A real AutoCAD file never
	/// stores a compressed size larger than the uncompressed one; with matches emitted this
	/// compressor gets below the input on real section data, and a caller that sees it fail to
	/// (incompressible data) can fall back to equal sizes the way the specification describes.
	/// </summary>
	internal class DwgLZ77AC21Compressor : ICompressor
	{
		private static readonly Lazy<int[][]> _permutations = new(calibrate);

		/// <summary>
		/// When true the stream continues an existing one: the 0x20 short-initial-literal header
		/// is never emitted (mid-stream, 0x20 would parse as a match opcode), so the first
		/// literal run is always the explicit form of length 8 or more.
		/// </summary>
		public bool MidStream { get; set; }

		public void Compress(byte[] source, int offset, int totalSize, Stream dest)
		{
			byte[] data = new byte[totalSize];
			Array.Copy(source, offset, data, 0, totalSize);

			//Greedy parse: longest match at each position via a hash chain over 4-byte prefixes.
			var head = new Dictionary<uint, int>();
			var prev = new int[totalSize];
			var output = new List<byte>(totalSize + 16);
			int literalStart = 0;
			int i = 0;
			bool anyMatchEmitted = false;
			int pendingEmbedIndex = -1; //the byte whose low 3 bits can carry the next literal run

			uint hashAt(int p) => (uint)(data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24));

			void insert(int p)
			{
				if (p + 4 <= totalSize)
				{
					uint h = hashAt(p);
					prev[p] = head.TryGetValue(h, out int prior) ? prior : -1;
					head[h] = p;
				}
			}

			void emitLiteral(int start, int count)
			{
				if (count == 0)
				{
					return;
				}

				if (pendingEmbedIndex >= 0 && count <= 7)
				{
					output[pendingEmbedIndex] = (byte)(output[pendingEmbedIndex] | count);
				}
				else if (!anyMatchEmitted && output.Count == 0 && count < 8 && !this.MidStream)
				{
					//The very start of the stream: the 0x20 short-literal header.
					output.Add(0x20);
					output.Add(0x00);
					output.Add(0x00);
					output.Add((byte)count);
				}
				else
				{
					//Explicit literal opcode: length 8..0x16 in one byte, 0x0F extension beyond.
					int len = count;
					if (len < 8)
					{
						//An explicit opcode cannot say less than 8; the caller arranges runs so
						//this cannot happen (short runs ride in a match's low bits).
						throw new InvalidOperationException("literal run under 8 with no carrier");
					}

					if (len <= 0x16)
					{
						output.Add((byte)(len - 8));
					}
					else
					{
						output.Add(0x0F);
						int rest = len - 0x17;
						if (rest < 0xFF)
						{
							output.Add((byte)rest);
						}
						else
						{
							output.Add(0xFF);
							rest -= 0xFF;
							while (true)
							{
								int word = Math.Min(rest, 0xFFFF);
								output.Add((byte)(word & 0xFF));
								output.Add((byte)(word >> 8));
								rest -= word;
								if (word != 0xFFFF)
								{
									break;
								}
							}
						}
					}
				}

				pendingEmbedIndex = -1;
				appendShuffled(output, data, start, count);
			}

			void emitMatch(int distance, int length)
			{
				pendingEmbedIndex = -1;
				if (length <= 18 && distance <= 0x2000)
				{
					//Form 1: [0x1t][o1][o2] - length = t+3, offset-1 = ((o2 & 0xF8) << 5) + o1.
					int off = distance - 1;
					output.Add((byte)(0x10 | (length - 3)));
					output.Add((byte)(off & 0xFF));
					pendingEmbedIndex = output.Count;
					output.Add((byte)((off >> 5) & 0xF8));
				}
				else if (length <= 255)
				{
					//Form 2 short: [0x2t][o1][o2][b3] - offset = o1|o2<<8,
					//length = (b3 & 0xF8) + (0x2t & 7).
					output.Add((byte)(0x20 | (length & 7)));
					output.Add((byte)(distance & 0xFF));
					output.Add((byte)((distance >> 8) & 0xFF));
					pendingEmbedIndex = output.Count;
					output.Add((byte)(length & 0xF8));
				}
				else
				{
					//Form 2 long: [0x28|t][o1][o2][b4][b5] - offset = (o1|o2<<8)+1,
					//length = 0x100 + (b4 << 3) + t + ((b5 & 0xF8) << 8).
					int len = length - 0x100;
					int t = len & 7;
					int b4 = (len >> 3) & 0xFF;
					int b5hi = (len >> 11) & 0x1F;
					output.Add((byte)(0x28 | t));
					int off = distance - 1;
					output.Add((byte)(off & 0xFF));
					output.Add((byte)((off >> 8) & 0xFF));
					output.Add((byte)b4);
					pendingEmbedIndex = output.Count;
					output.Add((byte)(b5hi << 3));
				}

				anyMatchEmitted = true;
			}

			while (i < totalSize)
			{
				int bestLength = 0, bestDistance = 0;
				if (i + 4 <= totalSize && head.TryGetValue(hashAt(i), out int candidate))
				{
					int chain = 0;
					while (candidate >= 0 && chain++ < 64)
					{
						int distance = i - candidate;
						if (distance > 0xFFFF)
						{
							break;
						}

						int length = 0;
						int max = totalSize - i;
						while (length < max && data[candidate + length] == data[i + length])
						{
							length++;
						}

						if (length > bestLength)
						{
							bestLength = length;
							bestDistance = distance;
						}

						candidate = prev[candidate];
					}
				}

				//A literal run that a match's low bits cannot carry must reach 8; hold matches
				//back when accepting one would strand a 1..7 literal with no carrier.
				int pendingRun = i - literalStart;
				bool carrierAvailable = pendingEmbedIndex >= 0
					|| (!anyMatchEmitted && output.Count == 0 && !this.MidStream);
				bool literalEncodable = pendingRun == 0 || pendingRun >= 8 || (pendingRun <= 7 && carrierAvailable);

				if (bestLength >= 4 && literalEncodable)
				{
					emitLiteral(literalStart, pendingRun);
					emitMatch(bestDistance, bestLength);
					int end = i + bestLength;
					while (i < end)
					{
						insert(i);
						i++;
					}

					literalStart = i;
				}
				else
				{
					insert(i);
					i++;
				}
			}

			emitLiteral(literalStart, i - literalStart);

			byte[] result = output.ToArray();
			dest.Write(result, 0, result.Length);
		}

		//data[start..start+count) appended to the stream in the byte order the decompressor's
		//literal copy turns back into the original.
		private static void appendShuffled(List<byte> output, byte[] data, int start, int count)
		{
			int[][] perms = _permutations.Value;
			byte[] chunkOut = new byte[count];
			int chunkCount = count / 32;
			int[] chunk = perms[32];
			for (int c = 0; c < chunkCount; c++)
			{
				int b = c * 32;
				for (int k = 0; k < 32; k++)
				{
					chunkOut[b + chunk[k]] = data[start + b + k];
				}
			}

			int remainder = count - chunkCount * 32;
			if (remainder > 0)
			{
				int b = chunkCount * 32;
				int[] perm = perms[remainder];
				for (int k = 0; k < remainder; k++)
				{
					chunkOut[b + perm[k]] = data[start + b + k];
				}
			}

			output.AddRange(chunkOut);
		}

		//Learn dst->src literal maps by decompressing marker runs - the decompressor calibrates
		//its own inverse, so the two cannot disagree.
		private static int[][] calibrate()
		{
			int[][] perms = new int[33][];

			for (int r = 1; r <= 32; r++)
			{
				int total = r == 32 ? 32 : 32 + r;
				byte[] src = new byte[total];
				for (int k = 0; k < total; k++)
				{
					src[k] = (byte)k;
				}

				byte[] header = total < 8
					? new byte[] { 0x20, 0x00, 0x00, (byte)total }
					: total <= 0x16
						? new byte[] { (byte)(total - 8) }
						: new byte[] { 0x0F, (byte)(total - 0x17) };
				byte[] stream = new byte[header.Length + total];
				header.CopyTo(stream, 0);
				src.CopyTo(stream, header.Length);

				byte[] output = new byte[total];
				new DwgLZ77AC21Decompressor().Decompress(stream, 0U, (uint)stream.Length, output);

				if (r == 32)
				{
					int[] p = new int[32];
					for (int k = 0; k < 32; k++)
					{
						p[k] = output[k];
					}

					perms[32] = p;
				}
				else
				{
					int[] p = new int[r];
					for (int k = 0; k < r; k++)
					{
						p[k] = output[32 + k] - 32;
					}

					perms[r] = p;
				}
			}

			return perms;
		}
	}
}
