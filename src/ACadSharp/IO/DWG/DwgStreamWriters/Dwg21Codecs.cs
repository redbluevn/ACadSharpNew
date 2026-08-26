using System;

namespace ACadSharp.IO.DWG.DwgStreamWriters;

//The write-side codecs of the R2007 (AC1021) file container, implemented from the Open Design
//Specification for .dwg files, chapter 5, and validated against the values a real AC1021 file
//stores (the reader parses them; these implementations must reproduce them before any file is
//written). The reader itself ignores every one of these check values - AutoCAD may not.

//5.11 CRC random encoding: an MT19937-style table of 0x270 values seeded from a 64-bit value;
//consumed two entries at a time for 64-bit randoms, and byte-wise for padding.
internal class Dwg21RandomEncoder
{
	private readonly uint[] _table = new uint[0x270];
	private int _index;

	private readonly uint[] _padding = new uint[0x80];

	public Dwg21RandomEncoder(ulong seed)
	{
		this._table[0] = (uint)seed * 0x343fd + 0x269ec3;
		this._table[1] = (uint)(seed >> 32) * 0x343fd + 0x269ec3;
		uint value = this._table[1];
		for (uint i = 2; i < 0x270; i++)
		{
			value = (((value >> 0x1e) ^ value) * 0x6c078965) + i;
			this._table[i] = value;
		}

		//5.11 Init ends by building the padding table, which CONSUMES the first 0x80 entries -
		//the random stream a writer then draws from starts at index 0x80. Measured against a real
		//file's check data: without this the whole stream is shifted.
		this._index = 0;
		for (int i = 0; i < 0x80; i++)
		{
			if (this._index >= 0x270)
			{
				this._index = 0;
			}

			this._padding[i] = this._table[this._index];
			this._index++;
		}
	}

	public ulong NextUInt64()
	{
		this._index += 2;
		if (this._index >= 0x270)
		{
			this._index = 0;
		}

		//Measured against a real file's check data: the table entry at the LOWER index becomes
		//the HIGH dword. The spec's pseudocode names them the other way around - and its
		//"low | (hi << 32)" with hi a UInt32 would shift to zero anyway; the file decides.
		return ((ulong)this._table[this._index] << 32) | this._table[this._index + 1];
	}

	//5.11: the 10 least significant bits of the value are spread over a 64-bit random.
	public ulong Encode(uint value)
	{
		ulong random = this.NextUInt64();
		uint lo = (uint)(random & 0x0df7df7df);
		uint hi = (uint)((random >> 32) & 0x0f7df7df7);
		if ((value & 0x200) != 0) { lo |= 0x20; }
		if ((value & 0x100) != 0) { lo |= 0x800; }
		if ((value & 0x80) != 0) { lo |= 0x20000; }
		if ((value & 0x40) != 0) { lo |= 0x800000; }
		if ((value & 0x20) != 0) { lo |= 0x20000000; }
		if ((value & 0x10) != 0) { hi |= 0x08; }
		if ((value & 0x8) != 0) { hi |= 0x200; }
		if ((value & 0x4) != 0) { hi |= 0x8000; }
		if ((value & 0x2) != 0) { hi |= 0x200000; }
		if ((value & 0x1) != 0) { hi |= 0x8000000; }
		return lo | ((ulong)hi << 32);
	}

	public void FillPadding(byte[] buffer, int offset, int count)
	{
		int written = 0;
		while (written < count)
		{
			ulong r = this.NextUInt64();
			for (int i = 0; i < 8 && written < count; i++, written++)
			{
				buffer[offset + written] = (byte)(r >> (8 * i));
			}
		}
	}
}

//5.4.1 data section page checksum - an Adler-style pair of sums seeded from the page seed.
internal static class Dwg21Checksum
{
	public static uint GetCheckSum(ulong seed, byte[] data, uint start, uint length)
	{
		seed = (seed + length) * 0x343fd + 0x269ec3;
		uint sum1 = (uint)(seed & 0xffff);
		uint sum2 = (uint)((seed >> 0x10) & 0xffff);

		uint index = start;
		while (length != 0)
		{
			uint chunk = Math.Min(0x15b0, length);
			length -= chunk;

			uint pairs = chunk >> 3;
			while (pairs-- > 0)
			{
				update2(data, index + 6, ref sum1, ref sum2);
				update2(data, index + 4, ref sum1, ref sum2);
				update2(data, index + 2, ref sum1, ref sum2);
				update2(data, index, ref sum1, ref sum2);
				index += 8;
			}

			uint rest = chunk & 7;
			if (rest > 0)
			{
				switch (rest)
				{
					case 1: update1(data, index, ref sum1, ref sum2); break;
					case 2: update2(data, index, ref sum1, ref sum2); break;
					case 3: update2(data, index, ref sum1, ref sum2); update1(data, index + 2, ref sum1, ref sum2); break;
					case 4: update2(data, index + 2, ref sum1, ref sum2); update2(data, index, ref sum1, ref sum2); break;
					case 5: update2(data, index + 2, ref sum1, ref sum2); update2(data, index, ref sum1, ref sum2); update1(data, index + 4, ref sum1, ref sum2); break;
					case 6: update2(data, index + 2, ref sum1, ref sum2); update2(data, index, ref sum1, ref sum2); update2(data, index + 4, ref sum1, ref sum2); break;
					case 7: update2(data, index + 2, ref sum1, ref sum2); update2(data, index, ref sum1, ref sum2); update2(data, index + 4, ref sum1, ref sum2); update1(data, index + 6, ref sum1, ref sum2); break;
				}

				index += rest;
			}

			sum1 %= 0xfff1;
			sum2 %= 0xfff1;
		}

		return (sum2 << 0x10) | (sum1 & 0xffff);
	}

	private static void update1(byte[] data, uint i, ref uint sum1, ref uint sum2)
	{
		sum1 += data[i];
		sum2 += sum1;
	}

	private static void update2(byte[] data, uint i, ref uint sum1, ref uint sum2)
	{
		update1(data, i, ref sum1, ref sum2);
		update1(data, i + 1, ref sum1, ref sum2);
	}
}

//5.12 64-bit CRC, in its two flavours. The block ordering for 1-8 bytes follows the spec's table;
//larger buffers go 8 bytes at a time.
internal static class Dwg21Crc64
{
	public static ulong UpdateSeed1(ulong seed, uint dataLength)
	{
		seed = (seed + dataLength) * 0x343fdUL + 0x269ec3UL;
		seed |= seed * (0x343fdUL << 32) + (0x269ec3UL << 32);
		return ~seed;
	}

	public static ulong UpdateSeed2(ulong seed, uint dataLength)
	{
		seed = (seed + dataLength) * 0x343fdUL + 0x269ec3UL;
		seed = seed * ((1UL << 32) + 0x343fdUL) + (dataLength + 0x269ec3UL);
		return ~seed;
	}

	public static ulong Normal(ulong crc, byte[] data, int start, int length)
	{
		return compute(crc, data, start, length, normalByte, true);
	}

	public static ulong Mirrored(ulong crc, byte[] data, int start, int length)
	{
		return compute(crc, data, start, length, mirroredByte, false);
	}

	private delegate ulong byteStep(byte value, ulong crc);

	internal static ulong NormalStep(byte value, ulong crc)
	{
		return _normal[(int)((value ^ (crc >> 56)) & 0xff)] ^ (crc << 8);
	}

	internal static ulong MirroredStep(byte value, ulong crc)
	{
		return _mirrored[(int)((crc ^ value) & 0xff)] ^ (crc >> 8);
	}

	private static ulong normalByte(byte value, ulong crc)
	{
		return NormalStep(value, crc);
	}

	private static ulong mirroredByte(byte value, ulong crc)
	{
		return MirroredStep(value, crc);
	}

	private static ulong compute(ulong crc, byte[] data, int start, int length, byteStep step, bool invertAtEnd)
	{
		int i = start;
		while (length >= 8)
		{
			crc = block(crc, data, i, 8, step);
			i += 8;
			length -= 8;
		}

		if (length > 0)
		{
			crc = block(crc, data, i, length, step);
		}

		return invertAtEnd ? ~crc : crc;
	}

	//The spec's 1-8 byte ordering table is RECURSIVE: it gives 4 as "2[2], 2[0]" and 8 as
	//"4[4], 4[0]", and each sub-block expands by the same table. Flattened, 8 is therefore
	//{6,7,4,5,2,3,0,1} - reading the table as a flat "blocks in table order, bytes ascending"
	//gives {4,5,6,7,0,1,2,3} and every CRC over 8 bytes or more comes out wrong. Measured: with
	//the flat order NO field of a real R2007 file's header reproduces; with this one, all of
	//them do (check CRC, compressed CRC, header CRC64 and the four map CRCs, on four files).
	private static readonly int[][] _order =
	{
		new int[0],
		new[] { 0 },
		new[] { 0, 1 },
		new[] { 0, 1, 2 },
		new[] { 2, 3, 0, 1 },
		new[] { 2, 3, 0, 1, 4 },
		new[] { 2, 3, 0, 1, 4, 5 },
		new[] { 2, 3, 0, 1, 4, 5, 6 },
		new[] { 6, 7, 4, 5, 2, 3, 0, 1 },
	};

	private static ulong block(ulong crc, byte[] data, int start, int count, byteStep step)
	{
		foreach (int offset in _order[count])
		{
			crc = step(data[start + offset], crc);
		}

		return crc;
	}

	private static readonly ulong[] _normal =
	{
		0x0000000000000000UL, 0x42f0e1eba9ea3693UL, 0x85e1c3d753d46d26UL, 0xc711223cfa3e5bb5UL,
		0x493366450e42ecdfUL, 0x0bc387aea7a8da4cUL, 0xccd2a5925d9681f9UL, 0x8e224479f47cb76aUL,
		0x9266cc8a1c85d9beUL, 0xd0962d61b56fef2dUL, 0x17870f5d4f51b498UL, 0x5577eeb6e6bb820bUL,
		0xdb55aacf12c73561UL, 0x99a54b24bb2d03f2UL, 0x5eb4691841135847UL, 0x1c4488f3e8f96ed4UL,
		0x663d78ff90e185efUL, 0x24cd9914390bb37cUL, 0xe3dcbb28c335e8c9UL, 0xa12c5ac36adfde5aUL,
		0x2f0e1eba9ea36930UL, 0x6dfeff5137495fa3UL, 0xaaefdd6dcd770416UL, 0xe81f3c86649d3285UL,
		0xf45bb4758c645c51UL, 0xb6ab559e258e6ac2UL, 0x71ba77a2dfb03177UL, 0x334a9649765a07e4UL,
		0xbd68d2308226b08eUL, 0xff9833db2bcc861dUL, 0x388911e7d1f2dda8UL, 0x7a79f00c7818eb3bUL,
		0xcc7af1ff21c30bdeUL, 0x8e8a101488293d4dUL, 0x499b3228721766f8UL, 0x0b6bd3c3dbfd506bUL,
		0x854997ba2f81e701UL, 0xc7b97651866bd192UL, 0x00a8546d7c558a27UL, 0x4258b586d5bfbcb4UL,
		0x5e1c3d753d46d260UL, 0x1cecdc9e94ace4f3UL, 0xdbfdfea26e92bf46UL, 0x990d1f49c77889d5UL,
		0x172f5b3033043ebfUL, 0x55dfbadb9aee082cUL, 0x92ce98e760d05399UL, 0xd03e790cc93a650aUL,
		0xaa478900b1228e31UL, 0xe8b768eb18c8b8a2UL, 0x2fa64ad7e2f6e317UL, 0x6d56ab3c4b1cd584UL,
		0xe374ef45bf6062eeUL, 0xa1840eae168a547dUL, 0x66952c92ecb40fc8UL, 0x2465cd79455e395bUL,
		0x3821458aada7578fUL, 0x7ad1a461044d611cUL, 0xbdc0865dfe733aa9UL, 0xff3067b657990c3aUL,
		0x711223cfa3e5bb50UL, 0x33e2c2240a0f8dc3UL, 0xf4f3e018f031d676UL, 0xb60301f359dbe0e5UL,
		0xda050215ea6c212fUL, 0x98f5e3fe438617bcUL, 0x5fe4c1c2b9b84c09UL, 0x1d14202910527a9aUL,
		0x93366450e42ecdf0UL, 0xd1c685bb4dc4fb63UL, 0x16d7a787b7faa0d6UL, 0x5427466c1e109645UL,
		0x4863ce9ff6e9f891UL, 0x0a932f745f03ce02UL, 0xcd820d48a53d95b7UL, 0x8f72eca30cd7a324UL,
		0x0150a8daf8ab144eUL, 0x43a04931514122ddUL, 0x84b16b0dab7f7968UL, 0xc6418ae602954ffbUL,
		0xbc387aea7a8da4c0UL, 0xfec89b01d3679253UL, 0x39d9b93d2959c9e6UL, 0x7b2958d680b3ff75UL,
		0xf50b1caf74cf481fUL, 0xb7fbfd44dd257e8cUL, 0x70eadf78271b2539UL, 0x321a3e938ef113aaUL,
		0x2e5eb66066087d7eUL, 0x6cae578bcfe24bedUL, 0xabbf75b735dc1058UL, 0xe94f945c9c3626cbUL,
		0x676dd025684a91a1UL, 0x259d31cec1a0a732UL, 0xe28c13f23b9efc87UL, 0xa07cf2199274ca14UL,
		0x167ff3eacbaf2af1UL, 0x548f120162451c62UL, 0x939e303d987b47d7UL, 0xd16ed1d631917144UL,
		0x5f4c95afc5edc62eUL, 0x1dbc74446c07f0bdUL, 0xdaad56789639ab08UL, 0x985db7933fd39d9bUL,
		0x84193f60d72af34fUL, 0xc6e9de8b7ec0c5dcUL, 0x01f8fcb784fe9e69UL, 0x43081d5c2d14a8faUL,
		0xcd2a5925d9681f90UL, 0x8fdab8ce70822903UL, 0x48cb9af28abc72b6UL, 0x0a3b7b1923564425UL,
		0x70428b155b4eaf1eUL, 0x32b26afef2a4998dUL, 0xf5a348c2089ac238UL, 0xb753a929a170f4abUL,
		0x3971ed50550c43c1UL, 0x7b810cbbfce67552UL, 0xbc902e8706d82ee7UL, 0xfe60cf6caf321874UL,
		0xe224479f47cb76a0UL, 0xa0d4a674ee214033UL, 0x67c58448141f1b86UL, 0x253565a3bdf52d15UL,
		0xab1721da49899a7fUL, 0xe9e7c031e063acecUL, 0x2ef6e20d1a5df759UL, 0x6c0603e6b3b7c1caUL,
		0xf6fae5c07d3274cdUL, 0xb40a042bd4d8425eUL, 0x731b26172ee619ebUL, 0x31ebc7fc870c2f78UL,
		0xbfc9838573709812UL, 0xfd39626eda9aae81UL, 0x3a28405220a4f534UL, 0x78d8a1b9894ec3a7UL,
		0x649c294a61b7ad73UL, 0x266cc8a1c85d9be0UL, 0xe17dea9d3263c055UL, 0xa38d0b769b89f6c6UL,
		0x2daf4f0f6ff541acUL, 0x6f5faee4c61f773fUL, 0xa84e8cd83c212c8aUL, 0xeabe6d3395cb1a19UL,
		0x90c79d3fedd3f122UL, 0xd2377cd44439c7b1UL, 0x15265ee8be079c04UL, 0x57d6bf0317edaa97UL,
		0xd9f4fb7ae3911dfdUL, 0x9b041a914a7b2b6eUL, 0x5c1538adb04570dbUL, 0x1ee5d94619af4648UL,
		0x02a151b5f156289cUL, 0x4051b05e58bc1e0fUL, 0x87409262a28245baUL, 0xc5b073890b687329UL,
		0x4b9237f0ff14c443UL, 0x0962d61b56fef2d0UL, 0xce73f427acc0a965UL, 0x8c8315cc052a9ff6UL,
		0x3a80143f5cf17f13UL, 0x7870f5d4f51b4980UL, 0xbf61d7e80f251235UL, 0xfd913603a6cf24a6UL,
		0x73b3727a52b393ccUL, 0x31439391fb59a55fUL, 0xf652b1ad0167feeaUL, 0xb4a25046a88dc879UL,
		0xa8e6d8b54074a6adUL, 0xea16395ee99e903eUL, 0x2d071b6213a0cb8bUL, 0x6ff7fa89ba4afd18UL,
		0xe1d5bef04e364a72UL, 0xa3255f1be7dc7ce1UL, 0x64347d271de22754UL, 0x26c49cccb40811c7UL,
		0x5cbd6cc0cc10fafcUL, 0x1e4d8d2b65facc6fUL, 0xd95caf179fc497daUL, 0x9bac4efc362ea149UL,
		0x158e0a85c2521623UL, 0x577eeb6e6bb820b0UL, 0x906fc95291867b05UL, 0xd29f28b9386c4d96UL,
		0xcedba04ad0952342UL, 0x8c2b41a1797f15d1UL, 0x4b3a639d83414e64UL, 0x09ca82762aab78f7UL,
		0x87e8c60fded7cf9dUL, 0xc51827e4773df90eUL, 0x020905d88d03a2bbUL, 0x40f9e43324e99428UL,
		0x2cffe7d5975e55e2UL, 0x6e0f063e3eb46371UL, 0xa91e2402c48a38c4UL, 0xebeec5e96d600e57UL,
		0x65cc8190991cb93dUL, 0x273c607b30f68faeUL, 0xe02d4247cac8d41bUL, 0xa2dda3ac6322e288UL,
		0xbe992b5f8bdb8c5cUL, 0xfc69cab42231bacfUL, 0x3b78e888d80fe17aUL, 0x7988096371e5d7e9UL,
		0xf7aa4d1a85996083UL, 0xb55aacf12c735610UL, 0x724b8ecdd64d0da5UL, 0x30bb6f267fa73b36UL,
		0x4ac29f2a07bfd00dUL, 0x08327ec1ae55e69eUL, 0xcf235cfd546bbd2bUL, 0x8dd3bd16fd818bb8UL,
		0x03f1f96f09fd3cd2UL, 0x41011884a0170a41UL, 0x86103ab85a2951f4UL, 0xc4e0db53f3c36767UL,
		0xd8a453a01b3a09b3UL, 0x9a54b24bb2d03f20UL, 0x5d45907748ee6495UL, 0x1fb5719ce1045206UL,
		0x919735e51578e56cUL, 0xd367d40ebc92d3ffUL, 0x1476f63246ac884aUL, 0x568617d9ef46bed9UL,
		0xe085162ab69d5e3cUL, 0xa275f7c11f7768afUL, 0x6564d5fde549331aUL, 0x279434164ca30589UL,
		0xa9b6706fb8dfb2e3UL, 0xeb46918411358470UL, 0x2c57b3b8eb0bdfc5UL, 0x6ea7525342e1e956UL,
		0x72e3daa0aa188782UL, 0x30133b4b03f2b111UL, 0xf7021977f9cceaa4UL, 0xb5f2f89c5026dc37UL,
		0x3bd0bce5a45a6b5dUL, 0x79205d0e0db05dceUL, 0xbe317f32f78e067bUL, 0xfcc19ed95e6430e8UL,
		0x86b86ed5267cdbd3UL, 0xc4488f3e8f96ed40UL, 0x0359ad0275a8b6f5UL, 0x41a94ce9dc428066UL,
		0xcf8b0890283e370cUL, 0x8d7be97b81d4019fUL, 0x4a6acb477bea5a2aUL, 0x089a2aacd2006cb9UL,
		0x14dea25f3af9026dUL, 0x562e43b4931334feUL, 0x913f6188692d6f4bUL, 0xd3cf8063c0c759d8UL,
		0x5dedc41a34bbeeb2UL, 0x1f1d25f19d51d821UL, 0xd80c07cd676f8394UL, 0x9afce626ce85b507UL,
	};

	private static readonly ulong[] _mirrored =
	{
		0x0000000000000000UL, 0x7ad870c830358979UL, 0xf5b0e190606b12f2UL, 0x8f689158505e9b8bUL,
		0xc038e5739841b68fUL, 0xbae095bba8743ff6UL, 0x358804e3f82aa47dUL, 0x4f50742bc81f2d04UL,
		0xab28ecb46814fe75UL, 0xd1f09c7c5821770cUL, 0x5e980d24087fec87UL, 0x24407dec384a65feUL,
		0x6b1009c7f05548faUL, 0x11c8790fc060c183UL, 0x9ea0e857903e5a08UL, 0xe478989fa00bd371UL,
		0x7d08ff3b88be6f81UL, 0x07d08ff3b88be6f8UL, 0x88b81eabe8d57d73UL, 0xf2606e63d8e0f40aUL,
		0xbd301a4810ffd90eUL, 0xc7e86a8020ca5077UL, 0x4880fbd87094cbfcUL, 0x32588b1040a14285UL,
		0xd620138fe0aa91f4UL, 0xacf86347d09f188dUL, 0x2390f21f80c18306UL, 0x594882d7b0f40a7fUL,
		0x1618f6fc78eb277bUL, 0x6cc0863448deae02UL, 0xe3a8176c18803589UL, 0x997067a428b5bcf0UL,
		0xfa11fe77117cdf02UL, 0x80c98ebf2149567bUL, 0x0fa11fe77117cdf0UL, 0x75796f2f41224489UL,
		0x3a291b04893d698dUL, 0x40f16bccb908e0f4UL, 0xcf99fa94e9567b7fUL, 0xb5418a5cd963f206UL,
		0x513912c379682177UL, 0x2be1620b495da80eUL, 0xa489f35319033385UL, 0xde51839b2936bafcUL,
		0x9101f7b0e12997f8UL, 0xebd98778d11c1e81UL, 0x64b116208142850aUL, 0x1e6966e8b1770c73UL,
		0x8719014c99c2b083UL, 0xfdc17184a9f739faUL, 0x72a9e0dcf9a9a271UL, 0x08719014c99c2b08UL,
		0x4721e43f0183060cUL, 0x3df994f731b68f75UL, 0xb29105af61e814feUL, 0xc849756751dd9d87UL,
		0x2c31edf8f1d64ef6UL, 0x56e99d30c1e3c78fUL, 0xd9810c6891bd5c04UL, 0xa3597ca0a188d57dUL,
		0xec09088b6997f879UL, 0x96d1784359a27100UL, 0x19b9e91b09fcea8bUL, 0x636199d339c963f2UL,
		0xdf7adabd7a6e2d6fUL, 0xa5a2aa754a5ba416UL, 0x2aca3b2d1a053f9dUL, 0x50124be52a30b6e4UL,
		0x1f423fcee22f9be0UL, 0x659a4f06d21a1299UL, 0xeaf2de5e82448912UL, 0x902aae96b271006bUL,
		0x74523609127ad31aUL, 0x0e8a46c1224f5a63UL, 0x81e2d7997211c1e8UL, 0xfb3aa75142244891UL,
		0xb46ad37a8a3b6595UL, 0xceb2a3b2ba0eececUL, 0x41da32eaea507767UL, 0x3b024222da65fe1eUL,
		0xa2722586f2d042eeUL, 0xd8aa554ec2e5cb97UL, 0x57c2c41692bb501cUL, 0x2d1ab4dea28ed965UL,
		0x624ac0f56a91f461UL, 0x1892b03d5aa47d18UL, 0x97fa21650afae693UL, 0xed2251ad3acf6feaUL,
		0x095ac9329ac4bc9bUL, 0x7382b9faaaf135e2UL, 0xfcea28a2faafae69UL, 0x8632586aca9a2710UL,
		0xc9622c4102850a14UL, 0xb3ba5c8932b0836dUL, 0x3cd2cdd162ee18e6UL, 0x460abd1952db919fUL,
		0x256b24ca6b12f26dUL, 0x5fb354025b277b14UL, 0xd0dbc55a0b79e09fUL, 0xaa03b5923b4c69e6UL,
		0xe553c1b9f35344e2UL, 0x9f8bb171c366cd9bUL, 0x10e3202993385610UL, 0x6a3b50e1a30ddf69UL,
		0x8e43c87e03060c18UL, 0xf49bb8b633338561UL, 0x7bf329ee636d1eeaUL, 0x012b592653589793UL,
		0x4e7b2d0d9b47ba97UL, 0x34a35dc5ab7233eeUL, 0xbbcbcc9dfb2ca865UL, 0xc113bc55cb19211cUL,
		0x5863dbf1e3ac9decUL, 0x22bbab39d3991495UL, 0xadd33a6183c78f1eUL, 0xd70b4aa9b3f20667UL,
		0x985b3e827bed2b63UL, 0xe2834e4a4bd8a21aUL, 0x6debdf121b863991UL, 0x1733afda2bb3b0e8UL,
		0xf34b37458bb86399UL, 0x8993478dbb8deae0UL, 0x06fbd6d5ebd3716bUL, 0x7c23a61ddbe6f812UL,
		0x3373d23613f9d516UL, 0x49aba2fe23cc5c6fUL, 0xc6c333a67392c7e4UL, 0xbc1b436e43a74e9dUL,
		0x95ac9329ac4bc9b5UL, 0xef74e3e19c7e40ccUL, 0x601c72b9cc20db47UL, 0x1ac40271fc15523eUL,
		0x5594765a340a7f3aUL, 0x2f4c0692043ff643UL, 0xa02497ca54616dc8UL, 0xdafce7026454e4b1UL,
		0x3e847f9dc45f37c0UL, 0x445c0f55f46abeb9UL, 0xcb349e0da4342532UL, 0xb1eceec59401ac4bUL,
		0xfebc9aee5c1e814fUL, 0x8464ea266c2b0836UL, 0x0b0c7b7e3c7593bdUL, 0x71d40bb60c401ac4UL,
		0xe8a46c1224f5a634UL, 0x927c1cda14c02f4dUL, 0x1d148d82449eb4c6UL, 0x67ccfd4a74ab3dbfUL,
		0x289c8961bcb410bbUL, 0x5244f9a98c8199c2UL, 0xdd2c68f1dcdf0249UL, 0xa7f41839ecea8b30UL,
		0x438c80a64ce15841UL, 0x3954f06e7cd4d138UL, 0xb63c61362c8a4ab3UL, 0xcce411fe1cbfc3caUL,
		0x83b465d5d4a0eeceUL, 0xf96c151de49567b7UL, 0x76048445b4cbfc3cUL, 0x0cdcf48d84fe7545UL,
		0x6fbd6d5ebd3716b7UL, 0x15651d968d029fceUL, 0x9a0d8ccedd5c0445UL, 0xe0d5fc06ed698d3cUL,
		0xaf85882d2576a038UL, 0xd55df8e515432941UL, 0x5a3569bd451db2caUL, 0x20ed197575283bb3UL,
		0xc49581ead523e8c2UL, 0xbe4df122e51661bbUL, 0x3125607ab548fa30UL, 0x4bfd10b2857d7349UL,
		0x04ad64994d625e4dUL, 0x7e7514517d57d734UL, 0xf11d85092d094cbfUL, 0x8bc5f5c11d3cc5c6UL,
		0x12b5926535897936UL, 0x686de2ad05bcf04fUL, 0xe70573f555e26bc4UL, 0x9ddd033d65d7e2bdUL,
		0xd28d7716adc8cfb9UL, 0xa85507de9dfd46c0UL, 0x273d9686cda3dd4bUL, 0x5de5e64efd965432UL,
		0xb99d7ed15d9d8743UL, 0xc3450e196da80e3aUL, 0x4c2d9f413df695b1UL, 0x36f5ef890dc31cc8UL,
		0x79a59ba2c5dc31ccUL, 0x037deb6af5e9b8b5UL, 0x8c157a32a5b7233eUL, 0xf6cd0afa9582aa47UL,
		0x4ad64994d625e4daUL, 0x300e395ce6106da3UL, 0xbf66a804b64ef628UL, 0xc5bed8cc867b7f51UL,
		0x8aeeace74e645255UL, 0xf036dc2f7e51db2cUL, 0x7f5e4d772e0f40a7UL, 0x05863dbf1e3ac9deUL,
		0xe1fea520be311aafUL, 0x9b26d5e88e0493d6UL, 0x144e44b0de5a085dUL, 0x6e963478ee6f8124UL,
		0x21c640532670ac20UL, 0x5b1e309b16452559UL, 0xd476a1c3461bbed2UL, 0xaeaed10b762e37abUL,
		0x37deb6af5e9b8b5bUL, 0x4d06c6676eae0222UL, 0xc26e573f3ef099a9UL, 0xb8b627f70ec510d0UL,
		0xf7e653dcc6da3dd4UL, 0x8d3e2314f6efb4adUL, 0x0256b24ca6b12f26UL, 0x788ec2849684a65fUL,
		0x9cf65a1b368f752eUL, 0xe62e2ad306bafc57UL, 0x6946bb8b56e467dcUL, 0x139ecb4366d1eea5UL,
		0x5ccebf68aecec3a1UL, 0x2616cfa09efb4ad8UL, 0xa97e5ef8cea5d153UL, 0xd3a62e30fe90582aUL,
		0xb0c7b7e3c7593bd8UL, 0xca1fc72bf76cb2a1UL, 0x45775673a732292aUL, 0x3faf26bb9707a053UL,
		0x70ff52905f188d57UL, 0x0a2722586f2d042eUL, 0x854fb3003f739fa5UL, 0xff97c3c80f4616dcUL,
		0x1bef5b57af4dc5adUL, 0x61372b9f9f784cd4UL, 0xee5fbac7cf26d75fUL, 0x9487ca0fff135e26UL,
		0xdbd7be24370c7322UL, 0xa10fceec0739fa5bUL, 0x2e675fb4576761d0UL, 0x54bf2f7c6752e8a9UL,
		0xcdcf48d84fe75459UL, 0xb71738107fd2dd20UL, 0x387fa9482f8c46abUL, 0x42a7d9801fb9cfd2UL,
		0x0df7adabd7a6e2d6UL, 0x772fdd63e7936bafUL, 0xf8474c3bb7cdf024UL, 0x829f3cf387f8795dUL,
		0x66e7a46c27f3aa2cUL, 0x1c3fd4a417c62355UL, 0x935745fc4798b8deUL, 0xe98f353477ad31a7UL,
		0xa6df411fbfb21ca3UL, 0xdc0731d78f8795daUL, 0x536fa08fdfd90e51UL, 0x29b7d047efec8728UL,
	};
}

//5.2.1.1.5: the file header page ends in 0x28 bytes of check data - two CRCs derived from two
//random draws, then the draws themselves, then the encoded CRC seed. The two CRCs are a pure
//function of the two randoms; validated against the values a real file stores.
internal static class Dwg21FileHeaderCheckData
{
	//5.2.1.1.5 Encode: a rotate whose amount is the low 5 bits of the control value.
	public static ulong Encode(ulong value, ulong control)
	{
		int shift = (int)(control & 0x1f);
		return shift == 0 ? value : (value << shift) | (value >> (64 - shift));
	}

	public static (ulong normalCrc, ulong mirroredCrc) Calculate(ulong random1, ulong random2)
	{
		static ulong encode(ulong value, ulong control) => Encode(value, control);

		ulong[] buffer = new ulong[8];
		buffer[0] = encode(random1, random2);
		buffer[1] = encode(buffer[0], buffer[0]);
		buffer[2] = encode(random2, buffer[1]);
		buffer[3] = encode(buffer[2], buffer[2]);
		buffer[4] = encode(random1, buffer[3]);
		buffer[5] = encode(buffer[4], buffer[4]);
		buffer[6] = encode(buffer[5], buffer[5]);
		buffer[7] = encode(buffer[6], buffer[6]);
		byte[] bytes = new byte[64];
		for (int i = 0; i < 8; i++)
		{
			BitConverter.GetBytes(buffer[i]).CopyTo(bytes, i * 8);
		}

		ulong normalCrc = Dwg21Crc64.Normal(~random2, bytes, 0, bytes.Length);

		ulong[] buffer2 = new ulong[8];
		buffer2[0] = encode(random1, random2);
		buffer2[1] = encode(normalCrc, buffer2[0]);
		buffer2[2] = encode(random2, buffer2[1]);
		buffer2[3] = encode(normalCrc, buffer2[2]);
		buffer2[4] = encode(random1, buffer2[3]);
		buffer2[5] = encode(normalCrc, buffer2[4]);
		buffer2[6] = encode(random2, buffer2[5]);
		buffer2[7] = encode(buffer2[6], buffer2[6]);
		byte[] bytes2 = new byte[64];
		for (int i = 0; i < 8; i++)
		{
			BitConverter.GetBytes(buffer2[i]).CopyTo(bytes2, i * 8);
		}

		ulong mirroredCrc = Dwg21Crc64.Mirrored(~random1, bytes2, 0, bytes2.Length);

		return (normalCrc, mirroredCrc);
	}
}

//5.13 Reed-Solomon over GF(256). Two configurations: (255,251) with primitive polynomial 0x11D
//for data pages, (255,239) with 0x169 for system pages. Encoding is plain LFSR division by the
//generator polynomial whose roots are alpha^1..alpha^(n-k) - the Rockliff construction the spec
//points at. Output is interleaved by block index, the layout the reader's de-interleaver strides
//through.
internal class Dwg21ReedSolomon
{
	public const int CodewordSize = 255;

	private readonly int _k;
	private readonly byte[] _gfExp = new byte[512];
	private readonly byte[] _gfLog = new byte[256];
	private readonly byte[] _generator;

	public static Dwg21ReedSolomon DataPages { get; } = new Dwg21ReedSolomon(251, 0x11D);

	public static Dwg21ReedSolomon SystemPages { get; } = new Dwg21ReedSolomon(239, 0x169);

	public int K { get { return this._k; } }

	private readonly byte[] _invXk;

	private Dwg21ReedSolomon(int k, int primitive)
	{
		this._k = k;

		int x = 1;
		for (int i = 0; i < 255; i++)
		{
			this._gfExp[i] = (byte)x;
			this._gfLog[x] = (byte)i;
			x <<= 1;
			if ((x & 0x100) != 0)
			{
				x ^= primitive;
			}
		}

		for (int i = 255; i < 512; i++)
		{
			this._gfExp[i] = this._gfExp[i - 255];
		}

		int parity = CodewordSize - k;
		byte[] g = new byte[] { 1 };
		for (int i = 1; i <= parity; i++)
		{
			//g = g * (x + alpha^i) - roots alpha^1..alpha^(n-k), measured on a real file's
			//codewords (16 of 16 zero syndromes at b0=1).
			byte[] next = new byte[g.Length + 1];
			for (int j = 0; j < g.Length; j++)
			{
				next[j] ^= this.mul(g[j], this._gfExp[i]);
				next[j + 1] ^= g[j];
			}

			g = next;
		}

		this._generator = g;
		this._invXk = this.invertModG(this.xPowModG(k));
	}

	private byte mul(byte a, byte b)
	{
		if (a == 0 || b == 0)
		{
			return 0;
		}

		return this._gfExp[this._gfLog[a] + this._gfLog[b]];
	}

	private byte inv(byte a)
	{
		return this._gfExp[255 - this._gfLog[a]];
	}

	//x^power mod g, coefficients ascending.
	private byte[] xPowModG(int power)
	{
		byte[] r = new byte[] { 1 };
		for (int i = 0; i < power; i++)
		{
			r = this.modG(this.shift(r));
		}

		return r;
	}

	private byte[] shift(byte[] p)
	{
		byte[] r = new byte[p.Length + 1];
		p.CopyTo(r, 1);
		return r;
	}

	private byte[] modG(byte[] p)
	{
		byte[] r = (byte[])p.Clone();
		int degG = this._generator.Length - 1;
		for (int d = r.Length - 1; d >= degG; d--)
		{
			byte c = r[d];
			if (c == 0)
			{
				continue;
			}

			for (int j = 0; j <= degG; j++)
			{
				r[d - degG + j] ^= this.mul(c, this._generator[j]);
			}
		}

		Array.Resize(ref r, degG);
		return r;
	}

	private byte[] mulModG(byte[] a, byte[] b)
	{
		byte[] r = new byte[a.Length + b.Length - 1];
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] == 0)
			{
				continue;
			}

			for (int j = 0; j < b.Length; j++)
			{
				r[i + j] ^= this.mul(a[i], b[j]);
			}
		}

		return this.modG(r);
	}

	//Extended Euclid over GF(256)[x] for the inverse of a mod g (a and g are coprime: g's roots
	//are powers of alpha and a = x^k has only the root 0).
	private byte[] invertModG(byte[] a)
	{
		byte[] r0 = (byte[])this._generator.Clone(), r1 = trim(a);
		byte[] t0 = new byte[] { 0 }, t1 = new byte[] { 1 };
		while (degree(r1) > 0 || r1[0] != 0)
		{
			if (degree(r1) == 0)
			{
				break;
			}

			(byte[] q, byte[] rem) = this.divide(r0, r1);
			byte[] t2 = xor(t0, this.mulPlain(q, t1));
			r0 = r1;
			r1 = rem;
			t0 = t1;
			t1 = t2;
		}

		//r1 is a nonzero constant; scale t1 by its inverse.
		byte scale = this.inv(r1[0]);
		byte[] result = new byte[t1.Length];
		for (int i = 0; i < t1.Length; i++)
		{
			result[i] = this.mul(t1[i], scale);
		}

		return this.modG(result);

		static int degree(byte[] p)
		{
			for (int i = p.Length - 1; i >= 0; i--)
			{
				if (p[i] != 0)
				{
					return i;
				}
			}

			return 0;
		}

		static byte[] trim(byte[] p)
		{
			int d = degree(p);
			byte[] r = new byte[d + 1];
			Array.Copy(p, r, d + 1);
			return r;
		}

		static byte[] xor(byte[] a, byte[] b)
		{
			byte[] r = new byte[Math.Max(a.Length, b.Length)];
			for (int i = 0; i < r.Length; i++)
			{
				byte va = i < a.Length ? a[i] : (byte)0;
				byte vb = i < b.Length ? b[i] : (byte)0;
				r[i] = (byte)(va ^ vb);
			}

			return r;
		}
	}

	private byte[] mulPlain(byte[] a, byte[] b)
	{
		byte[] r = new byte[a.Length + b.Length - 1];
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] == 0)
			{
				continue;
			}

			for (int j = 0; j < b.Length; j++)
			{
				r[i + j] ^= this.mul(a[i], b[j]);
			}
		}

		return r;
	}

	private (byte[] q, byte[] r) divide(byte[] a, byte[] b)
	{
		byte[] r = (byte[])a.Clone();
		int degB = b.Length - 1;
		while (degB > 0 && b[degB] == 0)
		{
			degB--;
		}

		byte invLead = this.inv(b[degB]);
		byte[] q = new byte[Math.Max(1, r.Length - degB)];
		for (int d = r.Length - 1; d >= degB; d--)
		{
			byte c = this.mul(r[d], invLead);
			if (c == 0)
			{
				continue;
			}

			q[d - degB] = c;
			for (int j = 0; j <= degB; j++)
			{
				r[d - degB + j] ^= this.mul(c, b[j]);
			}
		}

		int degR = r.Length - 1;
		while (degR > 0 && r[degR] == 0)
		{
			degR--;
		}

		Array.Resize(ref r, Math.Min(r.Length, Math.Max(degR + 1, 1)));
		return (q, r);
	}

	/// <summary>
	/// Parity bytes for one block of exactly K data bytes. The file's convention, measured rather
	/// than taken from any book: the first written byte is the LOWEST power - data byte j is the
	/// coefficient of x^j, parity byte i the coefficient of x^(K+i), and the codeword vanishes at
	/// alpha^1..alpha^(n-k). So parity = D(x) * (x^K)^-1 mod g, which over GF(2) makes
	/// D + x^K * parity a multiple of g.
	/// </summary>
	public byte[] BlockParity(byte[] data, int offset)
	{
		//D(x) mod g
		byte[] d = new byte[this._k];
		Array.Copy(data, offset, d, 0, this._k);
		byte[] rem = this.modG(d);
		byte[] p = this.mulModG(rem, this._invXk);

		int parityLength = CodewordSize - this._k;
		byte[] parity = new byte[parityLength];
		for (int i = 0; i < parityLength && i < p.Length; i++)
		{
			parity[i] = p[i];
		}

		return parity;
	}

	/// <summary>
	/// Encodes blockCount blocks of K bytes each (the source must be at least that long) into
	/// blockCount x 255 bytes, interleaved by block: byte j of block b sits at position
	/// b + blockCount * j - the layout the reader strides through.
	/// </summary>
	public byte[] EncodeInterleaved(byte[] source, int blockCount)
	{
		byte[] output = new byte[blockCount * CodewordSize];
		for (int b = 0; b < blockCount; b++)
		{
			byte[] parity = this.BlockParity(source, b * this._k);
			for (int j = 0; j < this._k; j++)
			{
				output[b + blockCount * j] = source[b * this._k + j];
			}

			for (int j = 0; j < parity.Length; j++)
			{
				output[b + blockCount * (this._k + j)] = parity[j];
			}
		}

		return output;
	}
}
