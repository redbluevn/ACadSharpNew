using ACadSharp.IO.DWG.FileHeaders;
using CSUtilities.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ACadSharp.IO.DWG.DwgStreamWriters;

/// <summary>
/// The R2007 (AC1021) file container: meta data, an RS-encoded compressed file header page, data
/// section pages, a section map and a pages map as RS-encoded system pages, and a copy of the
/// header page at the end of the stream. Built against two contracts at once: the Open Design
/// Specification chapter 5 (whose write-side pseudocode carries typos), and this library's own
/// AC21 reader, which is proven against real AutoCAD files - where the two disagreed, a
/// measurement on a real file decided (see Dwg21CodecProbeTests).
///
/// Layout choices that keep every reader interpretation safe:
/// - Data section pages are written UNCOMPRESSED with equal compressed/decompressed sizes, the
///   signal both this reader and a spec-following one use to skip decompression.
/// - System pages and the file header are written with the store-only compressor, whose output is
///   LONGER than its input: a size-comparing reader sees "compressed" and decompresses, and a
///   reader that always decompresses (like ours) is right too. Raw bytes there would break the
///   always-decompressing reader.
/// - The stored CRC64 and checksum fields are best-effort: AutoCAD 2027 was measured to open a
///   file with a flipped check-data bit, so they are not load-bearing; the RS parity is exact.
/// </summary>
internal class DwgFileHeaderWriterAC21 : DwgFileHeaderWriterBase<DwgFileHeaderAC21>
{
	public override int FileHeaderSize { get { return 0x480; } }

	public override int HandleSectionOffset { get { return 0; } }

	private class PendingSection
	{
		public string Name;
		public byte[] Data;
		public int PageMaxSize;
	}

	private class WrittenPage
	{
		public int Id;
		public long StoredSize;
		public ulong OffsetInSection;
		public int DataLength;
		public int CompressedLength;
		public byte[] Payload;
	}

	private class WrittenSection
	{
		public PendingSection Source;
		public ulong HashCode;
		public ulong Encoding;
		public List<WrittenPage> Pages = new();
	}

	private readonly List<PendingSection> _sections = new();

	//Section properties from the specification's table (5.2): hash code and encoding per name.
	//Encoding 4 means the page bytes are Reed-Solomon interleaved; 1 means plain.
	private static readonly Dictionary<string, (ulong hash, ulong encoding)> _sectionProperties = new()
	{
		[DwgSectionDefinition.FileDepList] = (0x6c4205ca, 1),
		[DwgSectionDefinition.XrefManifest] = (0x7ae40662, 4),
		[DwgSectionDefinition.AppInfoHistory] = (0x96de0737, 1),
		[DwgSectionDefinition.AppInfo] = (0x3fa0043e, 1),
		[DwgSectionDefinition.Preview] = (0x40aa0473, 1),
		[DwgSectionDefinition.SummaryInfo] = (0x717a060f, 1),
		[DwgSectionDefinition.RevHistory] = (0x60a205b3, 4),
		[DwgSectionDefinition.AcDbObjects] = (0x674c05a9, 4),
		[DwgSectionDefinition.ObjFreeSpace] = (0x77e2061f, 4),
		[DwgSectionDefinition.Template] = (0x4a1404ce, 4),
		[DwgSectionDefinition.Handles] = (0x3f6e0450, 4),
		[DwgSectionDefinition.Classes] = (0x3f54045f, 4),
		[DwgSectionDefinition.AuxHeader] = (0x54f0050a, 4),
		[DwgSectionDefinition.Header] = (0x32b803d9, 4),
	};

	public DwgFileHeaderWriterAC21(Stream stream, Encoding encoding, CadDocument model)
		: base(stream, encoding, model)
	{
	}

	//The canonical page size per section, from the specification's table (5.2) and confirmed by
	//dumping real files: AutoCAD writes these exact values into the section map's Max size field,
	//and this writer follows them instead of the caller's default page size.
	private static readonly Dictionary<string, int> _canonicalPageSize = new()
	{
		[DwgSectionDefinition.AppInfo] = 0x300,
		[DwgSectionDefinition.Preview] = 0x400,
		[DwgSectionDefinition.SummaryInfo] = 0x80,
		[DwgSectionDefinition.RevHistory] = 0x1000,
		[DwgSectionDefinition.AcDbObjects] = 0xF800,
		[DwgSectionDefinition.ObjFreeSpace] = 0xF800,
		[DwgSectionDefinition.Template] = 0x400,
		[DwgSectionDefinition.Handles] = 0xF800,
		[DwgSectionDefinition.Classes] = 0xF800,
		[DwgSectionDefinition.AuxHeader] = 0x800,
		[DwgSectionDefinition.Header] = 0x800,
		[DwgSectionDefinition.FileDepList] = 0x100,
		[DwgSectionDefinition.XrefManifest] = 0xF800,
		[DwgSectionDefinition.AppInfoHistory] = 0x600,
	};

	public override void AddSection(string name, MemoryStream stream, bool isCompressed, int decompsize = 0x7400)
	{
		byte[] data = stream.ToArray();
		string dirEnv = System.Environment.GetEnvironmentVariable("MOREDWG_SECTION_DIR");
		if (!string.IsNullOrEmpty(dirEnv))
		{
			string f0 = System.IO.Path.Combine(dirEnv, name.Replace(':', '_') + ".bin");
			if (System.IO.File.Exists(f0)) { data = System.IO.File.ReadAllBytes(f0); }
		}
		int pageMax = _canonicalPageSize.TryGetValue(name, out int canonical) ? canonical : decompsize;

		//A section stored raw (encoding 1) gets a page sized to its own data, rounded up to the
		//next 0x80 - measured on two real R2007 files, 8 sections out of 8: SummaryInfo 70 -> 128,
		//AppInfo 472 -> 512 and 718 -> 768, AppInfoHistory 1390 -> 1408 and 1470 -> 1536, Preview
		//38319 -> 38912.  A fixed table gets this right only for the drawing it was measured on.
		if (_sectionProperties.TryGetValue(name, out var props) && props.encoding == 1)
		{
			//Preview rounds to the next 0x800, the rest to the next 0x80 - both measured on two
			//real files: 38319 -> 38912, and 70 -> 128, 472 -> 512, 718 -> 768, 1390 -> 1408,
			//1470 -> 1536.
			int grain = name == DwgSectionDefinition.Preview ? 0x800 : 0x80;
			pageMax = (data.Length + grain - 1) & ~(grain - 1);
		}

		this._sections.Add(new PendingSection
		{
			Name = name,
			Data = data,
			PageMaxSize = pageMax,
		});
	}

	private string _probeName;
	private int _probeIndex;

	public override void WriteFile()
	{
		//One source of truth: the value stored as RandomSeed must be the one the encoder ran from,
		//or the file claims a seed that does not produce its own encoded fields.
		ulong randomSeed = 0x4D6F7265447767UL;
		string sEnv = System.Environment.GetEnvironmentVariable("MOREDWG_R2007_SEED");
		if (!string.IsNullOrEmpty(sEnv)) { randomSeed = System.Convert.ToUInt64(sEnv, 16); }
		var rng = new Dwg21RandomEncoder(randomSeed);
		var written = new List<WrittenSection>();
		//A minted minimal file from AutoCAD 2027 assigns ids 3.. to the data pages in file
		//order, the next two to the section map copies, and - after a gap of two - the last two
		//to the pages map copies; this writer follows that scheme exactly.
		int nextId = 3;

		//1. Build the data pages in memory first; ids start after the four map pages get theirs
		//assigned below, so ids are assigned in two passes: data pages first, then the maps -
		//mirroring a real file, where the maps carry the highest ids but the pages map sits FIRST
		//in the stream at relative offset 0.
		var dataPages = new List<(WrittenPage page, byte[] bytes)>();
		var dataPageSection = new List<string>();
		foreach (PendingSection section in orderPages(this._sections))
		{
			//The hash is computed, not looked up: it is the checksum routine over the name as
			//UTF-16, started at (character count, 0) - see Dwg21Checksum.GetSectionNameHash.
			ulong encoding = _sectionProperties.TryGetValue(section.Name, out var p) ? p.encoding : 1UL;
			ulong hash = Dwg21Checksum.GetSectionNameHash(section.Name);

			var ws = new WrittenSection { Source = section, HashCode = hash, Encoding = encoding };
			written.Add(ws);

			ulong offsetInSection = 0;
			int dataOffset = 0;
			while (dataOffset == 0 || dataOffset < section.Data.Length)
			{
				int chunkLength = Math.Min(section.PageMaxSize, section.Data.Length - dataOffset);
				string splitEnv = System.Environment.GetEnvironmentVariable("MOREDWG_OBJ_SPLIT");
				if (!string.IsNullOrEmpty(splitEnv) && section.Name == DwgSectionDefinition.AcDbObjects)
				{
					string[] sp = splitEnv.Split(',');
					int si = ws.Pages.Count;
					if (si < sp.Length) { chunkLength = Math.Min(int.Parse(sp[si]), section.Data.Length - dataOffset); }
				}
				_probeName = section.Name; _probeIndex = ws.Pages.Count;
				byte[] pageBytes = this.buildDataPage(section.Data, dataOffset, chunkLength, encoding, rng, section.PageMaxSize, out long storedSize, out int compressedLength, out byte[] payloadBytes);

				var page = new WrittenPage
				{
					Id = nextId++,
					StoredSize = storedSize,
					OffsetInSection = offsetInSection,
					DataLength = chunkLength,
					CompressedLength = compressedLength,
					Payload = payloadBytes,
				};
				ws.Pages.Add(page);
				dataPages.Add((page, pageBytes));
				dataPageSection.Add(section.Name);

				offsetInSection += (ulong)chunkLength;
				dataOffset += chunkLength;
				if (chunkLength == 0)
				{
					break; //an empty section still gets one page
				}
			}
		}

		string physEnv = System.Environment.GetEnvironmentVariable("MOREDWG_PHYS_ORDER");
		if (!string.IsNullOrEmpty(physEnv))
		{
			var slots = new List<int>();
			for (int i = 0; i < dataPageSection.Count; i++)
			{
				if (dataPageSection[i] == DwgSectionDefinition.AcDbObjects) { slots.Add(i); }
			}
			var perm = physEnv.Split(',').Select(int.Parse).ToList();
			var orig = slots.Select(i => dataPages[i]).ToList();
			for (int j = 0; j < slots.Count && j < perm.Count; j++) { dataPages[slots[j]] = orig[perm[j]]; }
			//AutoCAD numbers the data pages in FILE order; renumber after the permutation so both
			//hold at once - the twin never had them right together before.
			int renum = 3;
			foreach (var dp in dataPages) { dp.page.Id = renum++; }
		}

		int dataPageCount = dataPages.Count;

		//2. Section map (two copies, distinct ids - a real file carries both).
		byte[] sectionMapData = this.buildSectionMap(written);
		int sectionMapId1 = nextId++;
		int sectionMapId2 = nextId++;
		byte[] sectionMapPage = buildSystemPage(sectionMapData, rng,
			out ulong smComp, out ulong smFactor, out long smStored,
			out ulong smCrcComp, out ulong smCrcUncomp);

		//3. Pages map (two copies), after a two-id gap the way a minted real file numbers them.
		//It lists every page including both of its own copies, and its stored size depends only
		//on the entry count, so it can be sized before it is filled.
		nextId += 2;
		int pagesMapId1 = nextId++;
		int pagesMapId2 = nextId++;
		int totalPageCount = dataPageCount + 4;
		byte[] pagesMapProbe = new byte[totalPageCount * 16];
		buildSystemPage(pagesMapProbe, null, out _, out _, out long pmStoredProbe, out _, out _);

		//File order, mirroring a minted minimal real file: the two pages map copies ADJACENT at
		//relative offset 0, then the data pages, then the two section map copies at the end,
		//then the header copy.
		var fileOrder = new List<(int id, long size)>
		{
			(pagesMapId1, pmStoredProbe),
			(pagesMapId2, pmStoredProbe),
		};
		fileOrder.AddRange(dataPages.Select(d => (d.page.Id, d.page.StoredSize)));
		fileOrder.Add((sectionMapId1, smStored));
		fileOrder.Add((sectionMapId2, smStored));

		byte[] pagesMapData = new byte[totalPageCount * 16];
		using (var pm = new MemoryStream(pagesMapData, true))
		{
			foreach ((int id, long size) in fileOrder)
			{
				pm.Write(LittleEndianConverter.Instance.GetBytes((long)size), 0, 8);
				pm.Write(LittleEndianConverter.Instance.GetBytes((long)id), 0, 8);
			}
		}

		byte[] pagesMapPage = buildSystemPage(pagesMapData, rng,
			out ulong pmComp, out ulong pmFactor, out long pmStored,
			out ulong pmCrcComp, out ulong pmCrcUncomp);
		if (pmStored != pmStoredProbe)
		{
			throw new InvalidOperationException("pages map sizing is not content independent");
		}

		//4. Assemble the body and record the offsets the metadata needs.
		using var body = new MemoryStream();
		long pagesMap1Offset = body.Length;
		body.Write(pagesMapPage, 0, pagesMapPage.Length);
		long pagesMap2Offset = body.Length;
		body.Write(pagesMapPage, 0, pagesMapPage.Length);
		var pageAbsoluteOffsets = new Dictionary<int, long>();
		foreach ((WrittenPage page, byte[] bytes) in dataPages)
		{
			pageAbsoluteOffsets[page.Id] = 0x480 + body.Length;
			body.Write(bytes, 0, bytes.Length);
		}

		long sectionMap1Offset = body.Length;
		body.Write(sectionMapPage, 0, sectionMapPage.Length);
		body.Write(sectionMapPage, 0, sectionMapPage.Length);
		long headerCopyOffset = body.Length;

		//5. File header metadata (0x110) - now every number is known.
		//The seven values a real file takes from the random encoder, in the order it takes them.
		//Measured on a real R2007 file: from its stored RandomSeed the stream reproduces, back to
		//back, SectionsMapCrcSeed, PagesMapCrcSeed, the check data's random1 and random2, the
		//check data's encoded seed, CrcSeedEncoded and the header block's check value - seven
		//values, exact. They must all come from ONE encoder: a second encoder over the same seed
		//restarts the stream and makes the file disagree with the seed it declares.
		ulong sectionsMapCrcSeed = rng.Encode(0);
		ulong pagesMapCrcSeed = rng.Encode(0);
		ulong checkRandom1 = rng.NextUInt64();
		ulong checkRandom2 = rng.NextUInt64();
		ulong checkEncodedSeed = rng.Encode(0);
		ulong crcSeedEncoded = rng.Encode(0);
		ulong headerCheckValue = rng.NextUInt64();

		ulong fileSize = 0x480UL + (ulong)body.Length + 0x400UL;
		byte[] metadata = this.buildMetadata(
			fileSize, rngSeed: randomSeed,
			pagesMap1Offset: (ulong)pagesMap1Offset, pagesMapId1: pagesMapId1,
			pagesMap2Offset: (ulong)pagesMap2Offset, pagesMapId2: pagesMapId2,
			pmComp: pmComp, pmUncomp: (ulong)pagesMapData.Length, pmFactor: pmFactor,
			pagesAmount: (ulong)fileOrder.Count, pagesMaxId: (ulong)(nextId - 1),
			sectionsAmount: (ulong)written.Count + 1,
			sectionsMapId1: sectionMapId1, sectionsMapId2: sectionMapId2,
			smComp: smComp, smUncomp: (ulong)sectionMapData.Length, smFactor: smFactor,
			header2Offset: (ulong)headerCopyOffset,
			pmCrcComp: pmCrcComp, pmCrcUncomp: pmCrcUncomp,
			smCrcComp: smCrcComp, smCrcUncomp: smCrcUncomp,
			pagesMapCrcSeed: pagesMapCrcSeed, sectionsMapCrcSeed: sectionsMapCrcSeed,
			crcSeedEncoded: crcSeedEncoded);

		byte[] headerPage = this.buildHeaderPage(
			metadata, rng, headerCheckValue, checkRandom1, checkRandom2, checkEncodedSeed);

		//6. Assemble the stream: meta, header page, body, header page copy.
		this._stream.Position = 0;
		this.writeMetaData(written, pageAbsoluteOffsets);
		this._stream.Position = 0x80;
		this._stream.Write(headerPage, 0, headerPage.Length);
		this._stream.Position = 0x480;
		body.Position = 0;
		body.CopyTo(this._stream);
		this._stream.Write(headerPage, 0, headerPage.Length);
	}

	private byte[] buildDataPage(byte[] data, int offset, int length, ulong encoding, Dwg21RandomEncoder rng, int pageMaxSize, out long storedSize, out int compressedLength, out byte[] payloadBytes)
	{
		if (encoding == 4)
		{
			//Really compressed, the way every AutoCAD-written page is (a real file never stores
			//a compressed size larger than the uncompressed one). When the data will not shrink,
			//the raw bytes go in with equal sizes - the shape a real file's tiny Template page
			//carries - and the Reed-Solomon layer applies either way.
			byte[] compressed;
			using (var ms = new MemoryStream())
			{
				new DwgLZ77AC21Compressor().Compress(data, offset, length, ms);
				compressed = ms.ToArray();
			}

			byte[] payload;
			if (compressed.Length < length)
			{
				payload = compressed;
			}
			else
			{
				payload = new byte[length];
				Array.Copy(data, offset, payload, 0, length);
			}

			string pageDir = System.Environment.GetEnvironmentVariable("MOREDWG_PAGE_DIR");
			if (!string.IsNullOrEmpty(pageDir) && _probeName != null)
			{
				string pf = System.IO.Path.Combine(pageDir, _probeName.Replace(':', '_') + "_" + _probeIndex + ".lz");
				if (System.IO.File.Exists(pf)) { payload = System.IO.File.ReadAllBytes(pf); }
			}
			compressedLength = payload.Length;
			payloadBytes = payload;
			var rs = Dwg21ReedSolomon.DataPages;
			int aligned = (payload.Length + 7) & ~7;
			int blocks = (aligned + rs.K - 1) / rs.K;
			byte[] source = new byte[blocks * rs.K];
			payload.CopyTo(source, 0);
			byte[] encoded = rs.EncodeInterleaved(source, blocks);

			storedSize = align0x20(encoded.Length);
			byte[] page = new byte[storedSize];
			encoded.CopyTo(page, 0);
			return page;
		}
		else
		{
			//Encoding-1 pages are stored raw and padded to the section's page size, the way a
			//real file stores them - its SummaryInfo slot is the full 0x80, not the 0x46 the
			//data needs. The section map's per-page size field carries the same padded value,
			//and a page shorter than that leaves the two maps contradicting each other.
			compressedLength = length;
			payloadBytes = new byte[length];
			Array.Copy(data, offset, payloadBytes, 0, length);
			//A real file leaves an encoding-1 page 0x20 more room than the page size it declares.
			storedSize = align0x20(Math.Max(length, pageMaxSize) + 0x20);
			string psEnv = System.Environment.GetEnvironmentVariable("MOREDWG_PREVIEW_SLOT");
			if (!string.IsNullOrEmpty(psEnv) && pageMaxSize > 0x8000) { storedSize = System.Convert.ToInt64(psEnv, 16); }
			byte[] page = new byte[storedSize];
			Array.Copy(data, offset, page, 0, length);
			return page;
		}
	}

	//A system page: store-only compressed, then RS(255,239) interleaved with the data repeated
	//`factor` times, padded to the page size the specification's formula assigns.
	private static byte[] buildSystemPage(byte[] data, Dwg21RandomEncoder rng2, out ulong compressedSize, out ulong factor, out long storedSize, out ulong crcCompressed, out ulong crcUncompressed)
	{
		byte[] compressed;
		using (var ms = new MemoryStream())
		{
			new DwgLZ77AC21Compressor().Compress(data, 0, data.Length, ms);
			compressed = ms.ToArray();
		}

		compressedSize = (ulong)compressed.Length;

		//5.3: both CRCs of a system page are MIRRORED and seeded with UpdateSeed1 over the
		//buffer's own length - verified byte-for-byte against four real files for the pages
		//map and the section map, compressed and decompressed alike.
		crcUncompressed = Dwg21Crc64.Mirrored(
			Dwg21Crc64.UpdateSeed1(0, (uint)data.Length), data, 0, data.Length);
		crcCompressed = Dwg21Crc64.Mirrored(
			Dwg21Crc64.UpdateSeed1(0, (uint)compressed.Length), compressed, 0, compressed.Length);

		int alignedComp = (compressed.Length + 7) & ~7;
		long pageSize = getSystemPageSize(data.Length);
		var rs = Dwg21ReedSolomon.SystemPages;
		long maxBlocks = pageSize / Dwg21ReedSolomon.CodewordSize;
		long maxPre = maxBlocks * rs.K;
		while (maxPre < alignedComp)
		{
			pageSize = align0x20(pageSize + Dwg21ReedSolomon.CodewordSize);
			maxBlocks = pageSize / Dwg21ReedSolomon.CodewordSize;
			maxPre = maxBlocks * rs.K;
		}

		//The Reed-Solomon interleave stride MUST equal the count AutoCAD derives from the page
		//size (slot / 255) - measured on a real file by patching its SECOND section map copy,
		//the copy AutoCAD actually reads (patching the first is invisible to it): a page encoded
		//at a data-derived stride different from slot/255 reads as garbage there. This library's
		//reader derives the stride from aligned*factor instead, so the page size is chosen to
		//make BOTH derivations agree: blocks = ceil(aligned*factor / K), page = align32(blocks
		//* 255), whose floor(/255) is blocks again since the padding is under one codeword.
		factor = (ulong)(maxPre / alignedComp);
		long total = alignedComp * (long)factor;
		int blocks = (int)((total + rs.K - 1) / rs.K);
		pageSize = align0x20((long)blocks * Dwg21ReedSolomon.CodewordSize);

		byte[] source = new byte[blocks * rs.K];
		for (int r = 0; r < (int)factor; r++)
		{
			Array.Copy(compressed, 0, source, r * alignedComp, compressed.Length);
		}

		byte[] encoded = rs.EncodeInterleaved(source, blocks);
		storedSize = pageSize;
		byte[] page = new byte[pageSize];
		encoded.CopyTo(page, 0);
		rng2?.FillPadding(page, encoded.Length, (int)(pageSize - encoded.Length));
		return page;
	}

	private static long getSystemPageSize(long dataSize)
	{
		long aligned = (dataSize + 7) & ~7L;
		long pageSize = (aligned * 2 + Dwg21ReedSolomon.SystemPages.K - 1)
			/ Dwg21ReedSolomon.SystemPages.K * Dwg21ReedSolomon.CodewordSize;
		return pageSize < 0x400 ? 0x400 : align0x20(pageSize);
	}

	private static long align0x20(long value)
	{
		return (value + 0x1F) & ~0x1FL;
	}

	//The order a real AutoCAD file lays the pages out IN THE FILE, which is not the order it lists
	//them in the section map: measured by walking a real file's page map.  Sections this writer
	//emits and a real file does not keep their own order, after these.
	private static readonly string[] _pageOrder =
	{
		DwgSectionDefinition.SummaryInfo,
		DwgSectionDefinition.Preview,
		DwgSectionDefinition.AppInfo,
		DwgSectionDefinition.AppInfoHistory,
		DwgSectionDefinition.XrefManifest,
		DwgSectionDefinition.RevHistory,
		DwgSectionDefinition.AcDbObjects,
		DwgSectionDefinition.ObjFreeSpace,
		DwgSectionDefinition.Template,
		DwgSectionDefinition.Handles,
		DwgSectionDefinition.Classes,
		DwgSectionDefinition.AuxHeader,
		DwgSectionDefinition.Header,
	};

	private static IEnumerable<PendingSection> orderPages(IEnumerable<PendingSection> sections)
	{
		return sections.OrderBy(s =>
		{
			int i = Array.IndexOf(_pageOrder, s.Name);
			return i < 0 ? _pageOrder.Length : i;
		});
	}

	//The order a real AutoCAD file lists the sections in its section map, measured by dumping
	//one: AppInfoHistory, AppInfo, Preview, SummaryInfo, RevHistory, AcDbObjects, ObjFreeSpace,
	//Template, Handles, Classes, AuxHeader, Header - and the map ends with a nameless empty
	//terminator entry, which is the +1 in SectionsAmount.
	private static readonly string[] _sectionMapOrder =
	{
		DwgSectionDefinition.XrefManifest,
		DwgSectionDefinition.AppInfoHistory,
		DwgSectionDefinition.FileDepList,
		DwgSectionDefinition.AppInfo,
		DwgSectionDefinition.Preview,
		DwgSectionDefinition.SummaryInfo,
		DwgSectionDefinition.RevHistory,
		DwgSectionDefinition.AcDbObjects,
		DwgSectionDefinition.ObjFreeSpace,
		DwgSectionDefinition.Template,
		DwgSectionDefinition.Handles,
		DwgSectionDefinition.Classes,
		DwgSectionDefinition.AuxHeader,
		DwgSectionDefinition.Header,
	};

	private byte[] buildSectionMap(List<WrittenSection> sections)
	{
		using var ms = new MemoryStream();
		void writeUlong(ulong value) => ms.Write(LittleEndianConverter.Instance.GetBytes(value), 0, 8);

		IEnumerable<WrittenSection> ordered = sections
			.OrderBy(x =>
			{
				int i = Array.IndexOf(_sectionMapOrder, x.Source.Name);
				return i < 0 ? int.MaxValue : i;
			});

		foreach (WrittenSection section in ordered)
		{
			byte[] name = Encoding.Unicode.GetBytes(section.Source.Name);

			//0x00 Data size: the section's total uncompressed length.
			writeUlong((ulong)section.Source.Data.Length);
			//0x08 Max size: the page size the section was split by.
			writeUlong((ulong)section.Source.PageMaxSize);
			//0x10 Encryption
			writeUlong(0);
			//0x18 HashCode
			writeUlong(section.HashCode);
			//0x20 SectionNameLength - a byte count including the two-byte terminator, which is
			//what the reader consumes (it reads exactly this many bytes and strips the zeros).
			writeUlong((ulong)(name.Length + 2));
			//0x28 Unknown
			writeUlong(0);
			//0x30 Encoding
			writeUlong(section.Encoding);
			//0x38 NumPages
			writeUlong((ulong)section.Pages.Count);

			ms.Write(name, 0, name.Length);
			ms.WriteByte(0);
			ms.WriteByte(0);

			foreach (WrittenPage page in section.Pages)
			{
				//Page data offset (in decompressed section space).
				writeUlong(page.OffsetInSection);
				//Page Size: a real file stores the page's UNCOMPRESSED length here for RS-encoded
				//sections and the section's max size for plain ones - NOT the stored slot size,
				//which lives in the pages map. Measured by dumping a real section map.
				writeUlong(section.Encoding == 4 ? (ulong)page.DataLength : (ulong)section.Source.PageMaxSize);
				//Page ID.
				writeUlong((ulong)page.Id);
				//Uncompressed size, then the compressed size the page really carries.
				writeUlong((ulong)page.DataLength);
				writeUlong((ulong)page.CompressedLength);
				//5.4: the checksum is taken over the page's DECOMPRESSED data, the 64-bit CRC
				//over the bytes the page really stores (compressed when they shrank), seeded
				//from that stored length. Both verified against a real file's own values; both
				//are load-bearing - flipping either one in the section map gets the file
				//refused (the earlier "not load-bearing" reading came from probes that patched
				//only the first of the two map copies).
				uint checksum = Dwg21Checksum.GetCheckSum(0,
					this.sectionChunk(section, page), 0, (uint)page.DataLength);
				writeUlong(checksum);
				writeUlong(Dwg21Crc64.Mirrored(
					Dwg21Crc64.UpdateSeed1(0, (uint)page.Payload.Length),
					page.Payload, 0, page.Payload.Length));
			}
		}

		//The terminator: a nameless empty section, mirrored from a real file.
		writeUlong(0);        //data size
		writeUlong(0xF800);   //max size
		writeUlong(0);        //encryption
		writeUlong(0);        //hash
		writeUlong(0);        //name length
		writeUlong(0);        //unknown
		writeUlong(4);        //encoding
		writeUlong(0);        //pages

		return ms.ToArray();
	}

	private byte[] sectionChunk(WrittenSection section, WrittenPage page)
	{
		byte[] chunk = new byte[page.DataLength];
		Array.Copy(section.Source.Data, (long)page.OffsetInSection, chunk, 0, page.DataLength);
		return chunk;
	}

	private byte[] buildMetadata(
		ulong fileSize, ulong rngSeed,
		ulong pagesMap1Offset, int pagesMapId1, ulong pagesMap2Offset, int pagesMapId2,
		ulong pmComp, ulong pmUncomp, ulong pmFactor,
		ulong pagesAmount, ulong pagesMaxId,
		ulong sectionsAmount, int sectionsMapId1, int sectionsMapId2,
		ulong smComp, ulong smUncomp, ulong smFactor,
		ulong header2Offset,
		ulong pmCrcComp, ulong pmCrcUncomp, ulong smCrcComp, ulong smCrcUncomp,
		ulong pagesMapCrcSeed, ulong sectionsMapCrcSeed, ulong crcSeedEncoded)
	{
		byte[] buffer = new byte[0x110];
		using var ms = new MemoryStream(buffer, true);
		void writeUlong(ulong value) => ms.Write(LittleEndianConverter.Instance.GetBytes(value), 0, 8);

		writeUlong(0x70);                 //0x00 header size
		writeUlong(fileSize);             //0x08
		writeUlong(pmCrcComp);            //0x10 PagesMapCrcCompressed
		writeUlong(pmFactor);             //0x18 PagesMapCorrectionFactor
		writeUlong(pagesMapCrcSeed);      //0x20 PagesMapCrcSeed - crcSeed through the encoder
		writeUlong(pagesMap2Offset);      //0x28 Map2Offset - the second, distinct copy
		writeUlong((ulong)pagesMapId2);   //0x30 Map2Id
		writeUlong(pagesMap1Offset);      //0x38 PagesMapOffset - a real file puts this at 0
		writeUlong((ulong)pagesMapId1);   //0x40 PagesMapId
		writeUlong(header2Offset);        //0x48 Header2offset
		writeUlong(pmComp);               //0x50 PagesMapSizeCompressed
		writeUlong(pmUncomp);             //0x58 PagesMapSizeUncompressed
		writeUlong(pagesAmount);          //0x60 PagesAmount - the DATA pages, not the maps
		writeUlong(pagesMaxId);           //0x68 PagesMaxId
		writeUlong(0x20);                 //0x70
		writeUlong(0x40);                 //0x78
		writeUlong(pmCrcUncomp);          //0x80 PagesMapCrcUncompressed
		writeUlong(0xf800);               //0x88
		writeUlong(4);                    //0x90
		writeUlong(1);                    //0x98
		writeUlong(sectionsAmount);       //0xA0
		writeUlong(smCrcUncomp);          //0xA8 SectionsMapCrcUncompressed
		writeUlong(smComp);               //0xB0 SectionsMapSizeCompressed
		writeUlong((ulong)sectionsMapId2);//0xB8 SectionsMap2Id
		writeUlong((ulong)sectionsMapId1);//0xC0 SectionsMapId
		writeUlong(smUncomp);             //0xC8 SectionsMapSizeUncompressed
		writeUlong(smCrcComp);            //0xD0 SectionsMapCrcCompressed
		writeUlong(smFactor);             //0xD8 SectionsMapCorrectionFactor
		writeUlong(sectionsMapCrcSeed);   //0xE0 SectionsMapCrcSeed - crcSeed through the encoder
		writeUlong(0x60100);              //0xE8 StreamVersion
		writeUlong(0);                    //0xF0 CrcSeed
		writeUlong(crcSeedEncoded);       //0xF8 CrcSeedEncoded
		writeUlong(rngSeed);              //0x100 RandomSeed
		writeUlong(0);                    //0x108 Header CRC64 - filled after the fields

		ulong crcSeed = Dwg21Crc64.UpdateSeed2(0, (uint)buffer.Length);
		ulong headerCrc = Dwg21Crc64.Normal(crcSeed, buffer, 0, buffer.Length);
		BitConverter.GetBytes(headerCrc).CopyTo(buffer, 0x108);

		return buffer;
	}

	private byte[] buildHeaderPage(byte[] metadata, Dwg21RandomEncoder rng,
		ulong checkValue, ulong random1, ulong random2, ulong encodedSeed)
	{
		byte[] compressed;
		using (var ms = new MemoryStream())
		{
			new DwgLZ77AC21Compressor().Compress(metadata, 0, metadata.Length, ms);
			compressed = ms.ToArray();
		}

		//5.2.1.5: one block of [check CRC, check value, compressed CRC, ComprLen, Length2,
		//compressed data, pad to 8], repeated within 3 x 239 bytes, remainder random-padded.
		//5.2.1.4: the checking sequence is the pair [checkValue, Encode(checkValue, checkValue)]
		//in little endian, CRC'd normal with UpdateSeed1 over its 16 bytes. 5.2.1.3's compressed
		//CRC is normal too but seeded with UpdateSeed2. Both reproduce a real file's own values.
		byte[] sequence = new byte[16];
		BitConverter.GetBytes(checkValue).CopyTo(sequence, 0);
		BitConverter.GetBytes(Dwg21FileHeaderCheckData.Encode(checkValue, checkValue)).CopyTo(sequence, 8);
		ulong checkCrc = Dwg21Crc64.Normal(
			Dwg21Crc64.UpdateSeed1(0, (uint)sequence.Length), sequence, 0, sequence.Length);
		ulong compressedCrc = Dwg21Crc64.Normal(
			Dwg21Crc64.UpdateSeed2(0, (uint)compressed.Length), compressed, 0, compressed.Length);

		int blockLength = (32 + compressed.Length + 7) & ~7;
		var rs = Dwg21ReedSolomon.SystemPages;
		byte[] pre = new byte[3 * rs.K];
		int repeats = pre.Length / blockLength;
		if (repeats == 0)
		{
			throw new InvalidOperationException("file header metadata does not fit its page");
		}

		for (int r = 0; r < repeats; r++)
		{
			int b = r * blockLength;
			BitConverter.GetBytes(checkCrc).CopyTo(pre, b + 0);
			BitConverter.GetBytes(checkValue).CopyTo(pre, b + 8);
			BitConverter.GetBytes(compressedCrc).CopyTo(pre, b + 16);
			//One Int64: a real file carries the compressed size here and zero in the upper half.
			BitConverter.GetBytes((long)compressed.Length).CopyTo(pre, b + 24);
			compressed.CopyTo(pre, b + 32);
		}

		rng.FillPadding(pre, repeats * blockLength, pre.Length - repeats * blockLength);

		byte[] encoded = rs.EncodeInterleaved(pre, 3);

		byte[] page = new byte[0x400];
		encoded.CopyTo(page, 0);
		rng.FillPadding(page, encoded.Length, 0x400 - 0x28 - encoded.Length);

		//5.2.1.1.5 check data tail: normal CRC, mirrored CRC, random1, random2, encoded seed.
		//Both CRCs reproduce a real file's own values from its random1/random2, so the pair is
		//verified rather than guessed.
		(ulong normalCrc, ulong mirroredCrc) = Dwg21FileHeaderCheckData.Calculate(random1, random2);
		BitConverter.GetBytes(normalCrc).CopyTo(page, 0x3D8);
		BitConverter.GetBytes(mirroredCrc).CopyTo(page, 0x3E0);
		BitConverter.GetBytes(random1).CopyTo(page, 0x3E8);
		BitConverter.GetBytes(random2).CopyTo(page, 0x3F0);
		BitConverter.GetBytes(encodedSeed).CopyTo(page, 0x3F8);

		return page;
	}

	private void writeMetaData(List<WrittenSection> sections, Dictionary<int, long> pageAbsoluteOffsets)
	{
		void writeInt(int value) => this._stream.Write(LittleEndianConverter.Instance.GetBytes(value), 0, 4);
		void writeShort(ushort value) => this._stream.Write(LittleEndianConverter.Instance.GetBytes(value), 0, 2);

		int addressOf(string sectionName)
		{
			WrittenSection section = sections.FirstOrDefault(x => x.Source.Name == sectionName);
			if (section == null || section.Pages.Count == 0)
			{
				return 0;
			}

			return (int)pageAbsoluteOffsets[section.Pages[0].Id];
		}

		//0x00 version string
		this._stream.Write(Encoding.ASCII.GetBytes(this._document.Header.VersionString), 0, 6);
		//0x06 5 zeroes
		this._stream.Write(new byte[5], 0, 5);
		//0x0B maintenance version
		this._stream.WriteByte((byte)this._document.Header.MaintenanceVersion);
		//0x0C one byte: 0x00, 0x01 or 0x03
		this._stream.WriteByte(0x03);
		//0x0D preview address - the first page of the preview section
		writeInt(addressOf(DwgSectionDefinition.Preview));
		//0x11 dwg version, 0x12 app maintenance version - a real AC1021 file carries 33 and 0xFF
		this._stream.WriteByte(33);
		this._stream.WriteByte(0xFF);
		//0x13 codepage
		writeShort(this.getFileCodePage());
		//0x15 three unknown bytes - a real file carries 00 21 FF where the ODA writes zeroes
		this._stream.WriteByte(0x00);
		this._stream.WriteByte(0x21);
		this._stream.WriteByte(0xFF);
		//0x18 security type
		writeInt(0);
		//0x1C unknown long
		writeInt(0);
		//0x20 summary info address, 0x24 vba project address, 0x28 constant, 0x2C app info address
		writeInt(addressOf(DwgSectionDefinition.SummaryInfo));
		writeInt(0);
		writeInt(0x00000080);
		writeInt(addressOf(DwgSectionDefinition.AppInfo));
		//pad the rest of the 0x80 bytes with zeroes
		long remaining = 0x80 - this._stream.Position;
		this._stream.Write(new byte[remaining], 0, (int)remaining);
	}
}
