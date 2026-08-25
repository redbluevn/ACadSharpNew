using ACadSharp.IO.DWG;
using ACadSharp.IO.DWG.DwgStreamWriters;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
	public void DiagnosticHybridHeader()
	{
		string outDir = Environment.GetEnvironmentVariable("MOREDWG_AC21_HYBRID");
		if (string.IsNullOrEmpty(outDir))
		{
			return;
		}

		//H1: the real file with only its header page (0x80..0x480) rebuilt by our write-side
		//pipeline from its own decoded metadata. If AutoCAD accepts this, the header packaging
		//(store-only compression framing, RS, check tail) is sound and the fault is elsewhere.
		byte[] real = readFile();
		byte[] metadata = decodeFileHeaderPage(real);

		byte[] compressed;
		using (var ms = new MemoryStream())
		{
			new DwgLZ77AC21Compressor().Compress(metadata, 0, metadata.Length, ms);
			compressed = ms.ToArray();
		}

		var rng = new ACadSharp.IO.DWG.DwgStreamWriters.Dwg21RandomEncoder(0x1234);
		ulong checkValue = rng.NextUInt64();
		(ulong checkCrc, _) = ACadSharp.IO.DWG.DwgStreamWriters.Dwg21FileHeaderCheckData.Calculate(checkValue, checkValue);
		ulong compressedCrc = ACadSharp.IO.DWG.DwgStreamWriters.Dwg21Crc64.Normal(
			ACadSharp.IO.DWG.DwgStreamWriters.Dwg21Crc64.UpdateSeed1(0, (uint)compressed.Length),
			compressed, 0, compressed.Length);

		int blockLength = (32 + compressed.Length + 7) & ~7;
		var rs = ACadSharp.IO.DWG.DwgStreamWriters.Dwg21ReedSolomon.SystemPages;
		byte[] pre = new byte[3 * 239];
		int repeats = pre.Length / blockLength;
		for (int r = 0; r < repeats; r++)
		{
			int b = r * blockLength;
			BitConverter.GetBytes(checkCrc).CopyTo(pre, b + 0);
			BitConverter.GetBytes(checkValue).CopyTo(pre, b + 8);
			BitConverter.GetBytes(compressedCrc).CopyTo(pre, b + 16);
			BitConverter.GetBytes(compressed.Length).CopyTo(pre, b + 24);
			BitConverter.GetBytes(metadata.Length).CopyTo(pre, b + 28);
			compressed.CopyTo(pre, b + 32);
		}

		rng.FillPadding(pre, repeats * blockLength, pre.Length - repeats * blockLength);
		byte[] encoded = rs.EncodeInterleaved(pre, 3);

		byte[] hybrid = real.ToArray();
		Array.Clear(hybrid, 0x80, 0x400);
		encoded.CopyTo(hybrid, 0x80);
		//Keep the real check-data tail: measured decorative, but keep one variable per probe.
		Array.Copy(real, 0x80 + 0x3D8, hybrid, 0x80 + 0x3D8, 0x28);
		File.WriteAllBytes(Path.Combine(outDir, "h1_reheader.dwg"), hybrid);

		//Self-check: our own decode of the hybrid header must yield the same metadata.
		byte[] roundTrip = decodeFileHeaderPage(hybrid);
		Assert.True(metadata.SequenceEqual(roundTrip), "the rebuilt header does not decode back");
	}

	[Fact]
	public void DiagnosticHybridPagesMap()
	{
		string outDir = Environment.GetEnvironmentVariable("MOREDWG_AC21_HYBRID");
		if (string.IsNullOrEmpty(outDir))
		{
			return;
		}

		//H2: the real file, with its pages map re-encoded through our system-page pipeline
		//(store-only compression + RS), appended at the end and pointed at by a rebuilt header.
		//The original pages map bytes stay in place, stale; every other page keeps its position.
		byte[] real = readFile();
		byte[] metadata = decodeFileHeaderPage(real);

		ulong pmOffset = BitConverter.ToUInt64(metadata, 0x38);
		ulong pmComp = BitConverter.ToUInt64(metadata, 0x50);
		ulong pmUncomp = BitConverter.ToUInt64(metadata, 0x58);
		ulong pmFactor = BitConverter.ToUInt64(metadata, 0x18);

		//Decode the real pages map data.
		ulong aligned = (pmComp + 7) & ~7UL;
		uint totalSize = (uint)(aligned * pmFactor);
		int blocks = (int)(totalSize + 238) / 239;
		byte[] raw = real.Skip((int)(0x480 + pmOffset)).Take(blocks * 255).ToArray();
		byte[] compData = deinterleave(raw, blocks, 239, (int)totalSize);
		byte[] pmData = new byte[pmUncomp];
		new DwgLZ77AC21Decompressor().Decompress(compData, 0U, (uint)pmComp, pmData);

		//Re-encode with the store-only pipeline.
		byte[] myCompressed;
		using (var ms = new MemoryStream())
		{
			new DwgLZ77AC21Compressor().Compress(pmData, 0, pmData.Length, ms);
			myCompressed = ms.ToArray();
		}

		int myAligned = (myCompressed.Length + 7) & ~7;
		var rs = ACadSharp.IO.DWG.DwgStreamWriters.Dwg21ReedSolomon.SystemPages;
		long pageSize = 0x400;
		while (pageSize / 255 * rs.K < myAligned)
		{
			pageSize += 0x20;
		}

		long myFactor = pageSize / 255 * rs.K / myAligned;
		long total = myAligned * myFactor;
		int myBlocks = (int)((total + rs.K - 1) / rs.K);
		byte[] source = new byte[myBlocks * rs.K];
		for (int r = 0; r < myFactor; r++)
		{
			myCompressed.CopyTo(source, r * myAligned);
		}

		byte[] page = rs.EncodeInterleaved(source, myBlocks);

		//Append and repoint.
		byte[] hybrid = new byte[real.Length + page.Length];
		real.CopyTo(hybrid, 0);
		page.CopyTo(hybrid, real.Length);
		ulong newOffset = (ulong)(real.Length - 0x480);

		byte[] newMeta = metadata.ToArray();
		BitConverter.GetBytes(newOffset).CopyTo(newMeta, 0x38); //PagesMapOffset
		BitConverter.GetBytes(newOffset).CopyTo(newMeta, 0x28); //Map2Offset - same copy
		BitConverter.GetBytes((ulong)myCompressed.Length).CopyTo(newMeta, 0x50);
		BitConverter.GetBytes((ulong)myFactor).CopyTo(newMeta, 0x18);
		BitConverter.GetBytes((ulong)(hybrid.Length)).CopyTo(newMeta, 0x08); //FileSize

		byte[] headerPage = rebuildHeaderPage(newMeta);
		Array.Clear(hybrid, 0x80, 0x400);
		headerPage.CopyTo(hybrid, 0x80);
		Array.Copy(real, 0x80 + 0x3D8, hybrid, 0x80 + 0x3D8, 0x28);

		File.WriteAllBytes(Path.Combine(outDir, "h2_repagesmap.dwg"), hybrid);

		//H2b: identical surgery, but the appended page carries the REAL compressed bytes
		//(genuinely compressed, comp < uncomp) re-RS-encoded by us. Separates "store-only
		//compression is rejected in a system page" from "the RS/placement itself is rejected".
		byte[] realComp = compData.Take((int)pmComp).ToArray();
		int alignedB = (realComp.Length + 7) & ~7;
		long pageSizeB = 0x400;
		long factorB = pageSizeB / 255 * rs.K / alignedB;
		long totalB = alignedB * factorB;
		int blocksB = (int)((totalB + rs.K - 1) / rs.K);
		byte[] sourceB = new byte[blocksB * rs.K];
		for (int r = 0; r < factorB; r++)
		{
			realComp.CopyTo(sourceB, r * alignedB);
		}

		byte[] pageB = rs.EncodeInterleaved(sourceB, blocksB);

		byte[] hybridB = new byte[real.Length + pageB.Length];
		real.CopyTo(hybridB, 0);
		pageB.CopyTo(hybridB, real.Length);

		byte[] metaB = metadata.ToArray();
		BitConverter.GetBytes(newOffset).CopyTo(metaB, 0x38);
		BitConverter.GetBytes(newOffset).CopyTo(metaB, 0x28);
		BitConverter.GetBytes((ulong)realComp.Length).CopyTo(metaB, 0x50);
		BitConverter.GetBytes((ulong)factorB).CopyTo(metaB, 0x18);
		BitConverter.GetBytes((ulong)hybridB.Length).CopyTo(metaB, 0x08);

		byte[] headerB = rebuildHeaderPage(metaB);
		Array.Clear(hybridB, 0x80, 0x400);
		headerB.CopyTo(hybridB, 0x80);
		Array.Copy(real, 0x80 + 0x3D8, hybridB, 0x80 + 0x3D8, 0x28);

		File.WriteAllBytes(Path.Combine(outDir, "h2b_realcomp.dwg"), hybridB);

		//H2c: the real page bytes copied VERBATIM to the end of the file, offset repointed,
		//every size field kept - isolates "a system page may not live at the end / the offset is
		//validated" from everything about the encoding.
		int realPageStored = blocks * 255;
		byte[] realPageBytes = real.Skip((int)(0x480 + pmOffset)).Take(realPageStored).ToArray();
		byte[] hybridC = new byte[real.Length + realPageBytes.Length];
		real.CopyTo(hybridC, 0);
		realPageBytes.CopyTo(hybridC, real.Length);

		byte[] metaC = metadata.ToArray();
		BitConverter.GetBytes(newOffset).CopyTo(metaC, 0x38);
		BitConverter.GetBytes(newOffset).CopyTo(metaC, 0x28);
		BitConverter.GetBytes((ulong)hybridC.Length).CopyTo(metaC, 0x08);
		byte[] headerC = rebuildHeaderPage(metaC);
		Array.Clear(hybridC, 0x80, 0x400);
		headerC.CopyTo(hybridC, 0x80);
		Array.Copy(real, 0x80 + 0x3D8, hybridC, 0x80 + 0x3D8, 0x28);
		File.WriteAllBytes(Path.Combine(outDir, "h2c_verbatim_at_end.dwg"), hybridC);

		//H2d: nothing moved at all - only the FileSize metadata field grows by 0x400 and the
		//header is rebuilt. Isolates FileSize validation.
		byte[] metaD = metadata.ToArray();
		BitConverter.GetBytes((ulong)real.Length + 0x400UL).CopyTo(metaD, 0x08);
		byte[] hybridD = real.ToArray();
		byte[] headerD = rebuildHeaderPage(metaD);
		Array.Clear(hybridD, 0x80, 0x400);
		headerD.CopyTo(hybridD, 0x80);
		Array.Copy(real, 0x80 + 0x3D8, hybridD, 0x80 + 0x3D8, 0x28);
		File.WriteAllBytes(Path.Combine(outDir, "h2d_filesize_only.dwg"), hybridD);

		//H3: OUR full system-page pipeline (store-only + RS) written IN PLACE into the real pages
		//map slot (id 39, offset 0, slot 0x600) - no relocation variable at all.
		{
			byte[] myComp2;
			using (var ms = new MemoryStream())
			{
				new DwgLZ77AC21Compressor().Compress(pmData, 0, pmData.Length, ms);
				myComp2 = ms.ToArray();
			}

			int aligned2 = (myComp2.Length + 7) & ~7;
			int blocks2 = (aligned2 + rs.K - 1) / rs.K;
			byte[] src2 = new byte[blocks2 * rs.K];
			myComp2.CopyTo(src2, 0);
			byte[] page2 = rs.EncodeInterleaved(src2, blocks2);
			Assert.True(page2.Length <= 0x600, "my in-place page exceeds the real slot");

			byte[] h3 = real.ToArray();
			Array.Clear(h3, 0x480, 0x600);
			page2.CopyTo(h3, 0x480);

			byte[] meta3 = metadata.ToArray();
			BitConverter.GetBytes((ulong)myComp2.Length).CopyTo(meta3, 0x50);
			BitConverter.GetBytes(1UL).CopyTo(meta3, 0x18); //factor 1
			byte[] header3 = rebuildHeaderPage(meta3);
			Array.Clear(h3, 0x80, 0x400);
			header3.CopyTo(h3, 0x80);
			Array.Copy(real, 0x80 + 0x3D8, h3, 0x80 + 0x3D8, 0x28);
			File.WriteAllBytes(Path.Combine(outDir, "h3_myenc_inplace.dwg"), h3);
		}

		//H3b: the REAL compressed bytes, re-RS-encoded by us with the REAL factor, in place -
		//isolates the RS encoding alone.
		{
			byte[] realComp2 = compData.Take((int)pmComp).ToArray();
			int aligned3 = (realComp2.Length + 7) & ~7;
			long total3 = aligned3 * (long)pmFactor;
			int blocks3 = (int)((total3 + rs.K - 1) / rs.K);
			byte[] src3 = new byte[blocks3 * rs.K];
			for (int r = 0; r < (int)pmFactor; r++)
			{
				realComp2.CopyTo(src3, r * aligned3);
			}

			byte[] page3 = rs.EncodeInterleaved(src3, blocks3);
			Assert.True(page3.Length <= 0x600, "the re-encoded real page exceeds the slot");

			byte[] h3b = real.ToArray();
			Array.Clear(h3b, 0x480, 0x600);
			page3.CopyTo(h3b, 0x480);
			//No header change needed: comp, factor and offset are all unchanged.
			File.WriteAllBytes(Path.Combine(outDir, "h3b_realcomp_myrs_inplace.dwg"), h3b);
		}
	}

	private static byte[] rebuildHeaderPage(byte[] metadata)
	{
		byte[] compressed;
		using (var ms = new MemoryStream())
		{
			new DwgLZ77AC21Compressor().Compress(metadata, 0, metadata.Length, ms);
			compressed = ms.ToArray();
		}

		var rng = new ACadSharp.IO.DWG.DwgStreamWriters.Dwg21RandomEncoder(0x1234);
		ulong checkValue = rng.NextUInt64();
		(ulong checkCrc, _) = ACadSharp.IO.DWG.DwgStreamWriters.Dwg21FileHeaderCheckData.Calculate(checkValue, checkValue);
		ulong compressedCrc = ACadSharp.IO.DWG.DwgStreamWriters.Dwg21Crc64.Normal(
			ACadSharp.IO.DWG.DwgStreamWriters.Dwg21Crc64.UpdateSeed1(0, (uint)compressed.Length),
			compressed, 0, compressed.Length);

		int blockLength = (32 + compressed.Length + 7) & ~7;
		var rs = ACadSharp.IO.DWG.DwgStreamWriters.Dwg21ReedSolomon.SystemPages;
		byte[] pre = new byte[3 * 239];
		int repeats = pre.Length / blockLength;
		for (int r = 0; r < repeats; r++)
		{
			int b = r * blockLength;
			BitConverter.GetBytes(checkCrc).CopyTo(pre, b + 0);
			BitConverter.GetBytes(checkValue).CopyTo(pre, b + 8);
			BitConverter.GetBytes(compressedCrc).CopyTo(pre, b + 16);
			BitConverter.GetBytes(compressed.Length).CopyTo(pre, b + 24);
			BitConverter.GetBytes(metadata.Length).CopyTo(pre, b + 28);
			compressed.CopyTo(pre, b + 32);
		}

		rng.FillPadding(pre, repeats * blockLength, pre.Length - repeats * blockLength);
		byte[] encoded = rs.EncodeInterleaved(pre, 3);
		byte[] page = new byte[0x400];
		encoded.CopyTo(page, 0);
		return page;
	}

	[Fact]
	public void DiagnosticSectionMapDump()
	{
		if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MOREDWG_AC21_HYBRID")))
		{
			return;
		}

		byte[] real = readFile();
		byte[] metadata = decodeFileHeaderPage(real);
		ulong smId = BitConverter.ToUInt64(metadata, 0xC0);
		ulong smComp = BitConverter.ToUInt64(metadata, 0xB0);
		ulong smUncomp = BitConverter.ToUInt64(metadata, 0xC8);
		ulong smFactor = BitConverter.ToUInt64(metadata, 0xD8);

		//Find the section map page via the pages map.
		ulong pmOffset = BitConverter.ToUInt64(metadata, 0x38);
		ulong pmComp = BitConverter.ToUInt64(metadata, 0x50);
		ulong pmUncomp = BitConverter.ToUInt64(metadata, 0x58);
		ulong pmFactor = BitConverter.ToUInt64(metadata, 0x18);
		byte[] pmData = decodeSystemPage(real, pmOffset, pmComp, pmUncomp, pmFactor);

		long offset = 0;
		long smOffset = -1, smSize = -1;
		var pageList = new System.Text.StringBuilder();
		for (int i = 0; i + 16 <= pmData.Length; i += 16)
		{
			long size = BitConverter.ToInt64(pmData, i);
			long id = Math.Abs(BitConverter.ToInt64(pmData, i + 8));
			pageList.Append($"(id={id},size=0x{size:X},off=0x{offset:X}) ");
			if (id == (long)smId)
			{
				smOffset = offset;
				smSize = size;
			}

			offset += size;
		}

		byte[] smData = decodeSystemPage(real, (ulong)smOffset, smComp, smUncomp, smFactor);

		var sb = new System.Text.StringBuilder();
		sb.Append("PAGES: " + pageList + "\n\nSECTIONS:\n");
		int pos = 0;
		while (pos + 0x40 <= smData.Length)
		{
			ulong dataSize = BitConverter.ToUInt64(smData, pos);
			ulong maxSize = BitConverter.ToUInt64(smData, pos + 8);
			ulong encrypted = BitConverter.ToUInt64(smData, pos + 0x10);
			ulong hash = BitConverter.ToUInt64(smData, pos + 0x18);
			long nameLen = BitConverter.ToInt64(smData, pos + 0x20);
			ulong unknown = BitConverter.ToUInt64(smData, pos + 0x28);
			ulong enc = BitConverter.ToUInt64(smData, pos + 0x30);
			long numPages = BitConverter.ToInt64(smData, pos + 0x38);
			pos += 0x40;
			string name = nameLen > 0 ? System.Text.Encoding.Unicode.GetString(smData, pos, (int)nameLen).TrimEnd('\0') : "";
			pos += (int)nameLen;
			sb.Append($"{name}: data=0x{dataSize:X} max=0x{maxSize:X} encr={encrypted} hash=0x{hash:X} unk=0x{unknown:X} enc={enc} pages={numPages} [");
			for (int p = 0; p < numPages; p++)
			{
				ulong pOff = BitConverter.ToUInt64(smData, pos);
				ulong pSize = BitConverter.ToUInt64(smData, pos + 8);
				ulong pId = BitConverter.ToUInt64(smData, pos + 0x10);
				ulong pUn = BitConverter.ToUInt64(smData, pos + 0x18);
				ulong pCo = BitConverter.ToUInt64(smData, pos + 0x20);
				pos += 0x38;
				sb.Append($"(off=0x{pOff:X},sz=0x{pSize:X},id={pId},un=0x{pUn:X},co=0x{pCo:X})");
			}

			sb.Append("]\n");
		}

		Assert.Fail(sb.ToString());
	}

	[Fact]
	public void DiagnosticRepackRealContent()
	{
		string outDir = Environment.GetEnvironmentVariable("MOREDWG_AC21_HYBRID");
		if (string.IsNullOrEmpty(outDir))
		{
			return;
		}

		//H5: every decoded section of the real file, fed byte-for-byte through OUR container
		//writer. If AutoCAD opens this, the container is sound end to end and the fault lives in
		//the section content our DwgWriter produces for AC1021.
		byte[] real = readFile();
		byte[] metadata = decodeFileHeaderPage(real);
		ulong pmOffset = BitConverter.ToUInt64(metadata, 0x38);
		ulong pmComp = BitConverter.ToUInt64(metadata, 0x50);
		ulong pmUncomp = BitConverter.ToUInt64(metadata, 0x58);
		ulong pmFactor = BitConverter.ToUInt64(metadata, 0x18);
		byte[] pmData = decodeSystemPage(real, pmOffset, pmComp, pmUncomp, pmFactor);

		var records = new Dictionary<long, (long offset, long size)>();
		long acc = 0;
		for (int i = 0; i + 16 <= pmData.Length; i += 16)
		{
			long size = BitConverter.ToInt64(pmData, i);
			long id = Math.Abs(BitConverter.ToInt64(pmData, i + 8));
			records[id] = (acc, size);
			acc += size;
		}

		ulong smId = BitConverter.ToUInt64(metadata, 0xC0);
		ulong smComp = BitConverter.ToUInt64(metadata, 0xB0);
		ulong smUncomp = BitConverter.ToUInt64(metadata, 0xC8);
		ulong smFactor = BitConverter.ToUInt64(metadata, 0xD8);
		byte[] smData = decodeSystemPage(real, (ulong)records[(long)smId].offset, smComp, smUncomp, smFactor);

		var doc = new CadDocument(ACadVersion.AC1021);
		using var output = new MemoryStream();
		var container = new ACadSharp.IO.DWG.DwgStreamWriters.DwgFileHeaderWriterAC21(
			output, Encoding.GetEncoding(getWindows1252()), doc);

		int pos = 0;
		while (pos + 0x40 <= smData.Length)
		{
			ulong dataSize = BitConverter.ToUInt64(smData, pos);
			ulong maxSize = BitConverter.ToUInt64(smData, pos + 8);
			long nameLen = BitConverter.ToInt64(smData, pos + 0x20);
			ulong enc = BitConverter.ToUInt64(smData, pos + 0x30);
			long numPages = BitConverter.ToInt64(smData, pos + 0x38);
			pos += 0x40;
			string name = nameLen > 0 ? Encoding.Unicode.GetString(smData, pos, (int)nameLen).TrimEnd('\0') : "";
			pos += (int)nameLen;

			byte[] sectionData = new byte[dataSize];
			for (int p = 0; p < numPages; p++)
			{
				ulong pOff = BitConverter.ToUInt64(smData, pos);
				ulong pId = BitConverter.ToUInt64(smData, pos + 0x10);
				ulong pUn = BitConverter.ToUInt64(smData, pos + 0x18);
				ulong pCo = BitConverter.ToUInt64(smData, pos + 0x20);
				pos += 0x38;

				(long pageOffset, long pageSize) = records[(long)pId];
				byte[] raw = real.Skip((int)(0x480 + pageOffset)).Take((int)pageSize).ToArray();
				byte[] pageBytes;
				if (enc == 4)
				{
					ulong aligned = (pCo + 7) & ~7UL;
					int blocks = (int)((aligned + 250) / 251);
					byte[] deint = deinterleave(raw, blocks, 251, blocks * 251);
					pageBytes = deint;
				}
				else
				{
					pageBytes = raw;
				}

				if (pCo != pUn)
				{
					byte[] dec = new byte[pUn];
					new DwgLZ77AC21Decompressor().Decompress(pageBytes, 0U, (uint)pCo, dec);
					pageBytes = dec;
				}

				Array.Copy(pageBytes, 0, sectionData, (long)pOff, (long)pUn);
			}

			if (name.Length > 0)
			{
				using var sectionStream = new MemoryStream(sectionData);
				container.AddSection(name, sectionStream, true, (int)maxSize);
			}
		}

		container.WriteFile();
		File.WriteAllBytes(Path.Combine(outDir, "h5_repack_real_content.dwg"), output.ToArray());
	}

	[Fact]
	public void DiagnosticSectionMapByteDiff()
	{
		if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MOREDWG_AC21_HYBRID")))
		{
			return;
		}

		//Serialize the REAL descriptors with our writer's exact field layout and byte-compare
		//against the real section map. Every field-semantic mistake shows at once, no oracle run.
		byte[] real = readFile();
		byte[] metadata = decodeFileHeaderPage(real);
		ulong pmOffset = BitConverter.ToUInt64(metadata, 0x38);
		byte[] pmData = decodeSystemPage(real, pmOffset,
			BitConverter.ToUInt64(metadata, 0x50), BitConverter.ToUInt64(metadata, 0x58),
			BitConverter.ToUInt64(metadata, 0x18));
		var records = new Dictionary<long, (long offset, long size)>();
		long acc = 0;
		for (int i = 0; i + 16 <= pmData.Length; i += 16)
		{
			long size = BitConverter.ToInt64(pmData, i);
			long id = Math.Abs(BitConverter.ToInt64(pmData, i + 8));
			records[id] = (acc, size);
			acc += size;
		}

		ulong smId = BitConverter.ToUInt64(metadata, 0xC0);
		byte[] smData = decodeSystemPage(real, (ulong)records[(long)smId].offset,
			BitConverter.ToUInt64(metadata, 0xB0), BitConverter.ToUInt64(metadata, 0xC8),
			BitConverter.ToUInt64(metadata, 0xD8));

		//Re-serialize identically: copy the descriptor stream field by field.
		using var ms = new MemoryStream();
		void wr(ulong v) => ms.Write(BitConverter.GetBytes(v), 0, 8);
		int pos = 0;
		while (pos + 0x40 <= smData.Length)
		{
			for (int f = 0; f < 8; f++)
			{
				wr(BitConverter.ToUInt64(smData, pos + f * 8));
			}

			long nameLen = BitConverter.ToInt64(smData, pos + 0x20);
			long numPages = BitConverter.ToInt64(smData, pos + 0x38);
			pos += 0x40;
			ms.Write(smData, pos, (int)nameLen);
			pos += (int)nameLen;
			for (int p = 0; p < numPages; p++)
			{
				for (int f = 0; f < 7; f++)
				{
					wr(BitConverter.ToUInt64(smData, pos + f * 8));
				}

				pos += 0x38;
			}
		}

		byte[] mine = ms.ToArray();
		var diffs = new System.Text.StringBuilder();
		diffs.Append($"lenReal={smData.Length} lenMine={mine.Length} tailRealBeyondParse={smData.Length - pos};");
		int shown = 0;
		for (int i = 0; i < Math.Min(smData.Length, mine.Length) && shown < 10; i++)
		{
			if (smData[i] != mine[i])
			{
				diffs.Append($" [0x{i:X}] real={smData[i]:X2} mine={mine[i]:X2};");
				shown++;
			}
		}

		Assert.Fail(diffs.ToString());
	}

	[Fact]
	public void DiagnosticChecksumValidation()
	{
		string outDir = Environment.GetEnvironmentVariable("MOREDWG_AC21_HYBRID");
		if (string.IsNullOrEmpty(outDir))
		{
			return;
		}

		byte[] real = readFile();
		byte[] metadata = decodeFileHeaderPage(real);
		ulong pmOffset = BitConverter.ToUInt64(metadata, 0x38);
		byte[] pmData = decodeSystemPage(real, pmOffset,
			BitConverter.ToUInt64(metadata, 0x50), BitConverter.ToUInt64(metadata, 0x58),
			BitConverter.ToUInt64(metadata, 0x18));
		var records = new Dictionary<long, (long offset, long size)>();
		long acc = 0;
		for (int i = 0; i + 16 <= pmData.Length; i += 16)
		{
			long size = BitConverter.ToInt64(pmData, i);
			long id = Math.Abs(BitConverter.ToInt64(pmData, i + 8));
			records[id] = (acc, size);
			acc += size;
		}

		ulong smId = BitConverter.ToUInt64(metadata, 0xC0);
		(long smOff, long smSlot) = records[(long)smId];
		byte[] smData = decodeSystemPage(real, (ulong)smOff,
			BitConverter.ToUInt64(metadata, 0xB0), BitConverter.ToUInt64(metadata, 0xC8),
			BitConverter.ToUInt64(metadata, 0xD8));

		void emit(string name, byte[] data)
		{
			byte[] comp;
			using (var ms = new MemoryStream())
			{
				new DwgLZ77AC21Compressor().Compress(data, 0, data.Length, ms);
				comp = ms.ToArray();
			}

			int aligned = (comp.Length + 7) & ~7;
			var rs = ACadSharp.IO.DWG.DwgStreamWriters.Dwg21ReedSolomon.SystemPages;
			int blocks = (aligned + rs.K - 1) / rs.K;
			byte[] src = new byte[blocks * rs.K];
			comp.CopyTo(src, 0);
			byte[] page = rs.EncodeInterleaved(src, blocks);
			Assert.True(page.Length <= smSlot, "rebuilt section map exceeds its slot");

			byte[] hybrid = real.ToArray();
			Array.Clear(hybrid, (int)(0x480 + smOff), (int)smSlot);
			page.CopyTo(hybrid, (int)(0x480 + smOff));

			byte[] meta = metadata.ToArray();
			BitConverter.GetBytes((ulong)comp.Length).CopyTo(meta, 0xB0);
			BitConverter.GetBytes(1UL).CopyTo(meta, 0xD8); //factor 1
			byte[] header = rebuildHeaderPage(meta);
			Array.Clear(hybrid, 0x80, 0x400);
			header.CopyTo(hybrid, 0x80);
			Array.Copy(real, 0x80 + 0x3D8, hybrid, 0x80 + 0x3D8, 0x28);
			File.WriteAllBytes(Path.Combine(outDir, name), hybrid);
		}

		//H6: rebuilt in place, content untouched - the vehicle's sanity check.
		emit("h6_sm_rebuilt.dwg", smData.ToArray());

		//H7: one page checksum perturbed - the first AcDbObjects page's checksum field.
		byte[] perturbed = smData.ToArray();
		int pos = 0;
		while (pos + 0x40 <= perturbed.Length)
		{
			ulong hash = BitConverter.ToUInt64(perturbed, pos + 0x18);
			long nameLen = BitConverter.ToInt64(perturbed, pos + 0x20);
			long numPages = BitConverter.ToInt64(perturbed, pos + 0x38);
			pos += 0x40 + (int)nameLen;
			if (hash == 0x674c05a9)
			{
				perturbed[pos + 0x28] ^= 0x01; //first page's checksum, lowest byte
				break;
			}

			pos += 0x38 * (int)numPages;
		}

		emit("h7_sm_checksum_flip.dwg", perturbed);
	}

	[Fact]
	public void DiagnosticUncompressedMultiBlockPage()
	{
		string outDir = Environment.GetEnvironmentVariable("MOREDWG_AC21_HYBRID");
		if (string.IsNullOrEmpty(outDir))
		{
			return;
		}

		//H8: does comp==uncomp on an encoding-4 DATA page mean "no RS" to AutoCAD? The real
		//Template page cannot discriminate (one RS block interleaves to identity). Replace the
		//first AcDbObjects page (multi-block) with our RS-encoded UNCOMPRESSED bytes and update
		//its section map entry to comp==uncomp. Refusal confirms the hypothesis.
		byte[] real = readFile();
		byte[] metadata = decodeFileHeaderPage(real);
		ulong pmOffset = BitConverter.ToUInt64(metadata, 0x38);
		byte[] pmData = decodeSystemPage(real, pmOffset,
			BitConverter.ToUInt64(metadata, 0x50), BitConverter.ToUInt64(metadata, 0x58),
			BitConverter.ToUInt64(metadata, 0x18));
		var records = new Dictionary<long, (long offset, long size)>();
		long acc = 0;
		for (int i = 0; i + 16 <= pmData.Length; i += 16)
		{
			long size = BitConverter.ToInt64(pmData, i);
			long id = Math.Abs(BitConverter.ToInt64(pmData, i + 8));
			records[id] = (acc, size);
			acc += size;
		}

		ulong smId = BitConverter.ToUInt64(metadata, 0xC0);
		(long smOff, long smSlot) = records[(long)smId];
		byte[] smData = decodeSystemPage(real, (ulong)smOff,
			BitConverter.ToUInt64(metadata, 0xB0), BitConverter.ToUInt64(metadata, 0xC8),
			BitConverter.ToUInt64(metadata, 0xD8));

		//Find AcDbObjects' first page entry and decode that page's uncompressed bytes.
		int pos = 0;
		int entryPos = -1;
		ulong pOff = 0, pId = 0, pUn = 0, pCo = 0;
		while (pos + 0x40 <= smData.Length)
		{
			ulong hash = BitConverter.ToUInt64(smData, pos + 0x18);
			long nameLen = BitConverter.ToInt64(smData, pos + 0x20);
			long numPages = BitConverter.ToInt64(smData, pos + 0x38);
			pos += 0x40 + (int)nameLen;
			if (hash == 0x674c05a9)
			{
				entryPos = pos;
				pOff = BitConverter.ToUInt64(smData, pos);
				pId = BitConverter.ToUInt64(smData, pos + 0x10);
				pUn = BitConverter.ToUInt64(smData, pos + 0x18);
				pCo = BitConverter.ToUInt64(smData, pos + 0x20);
				break;
			}

			pos += 0x38 * (int)numPages;
		}

		Assert.True(entryPos >= 0);

		(long pageOff, long pageSlot) = records[(long)pId];
		byte[] raw = real.Skip((int)(0x480 + pageOff)).Take((int)pageSlot).ToArray();
		ulong alignedCo = (pCo + 7) & ~7UL;
		int coBlocks = (int)((alignedCo + 250) / 251);
		byte[] deint = deinterleave(raw, coBlocks, 251, coBlocks * 251);
		byte[] pageData = new byte[pUn];
		new DwgLZ77AC21Decompressor().Decompress(deint, 0U, (uint)pCo, pageData);

		//Re-encode UNCOMPRESSED as interleaved RS - too big for the original slot, so it becomes
		//a NEW page (id 41) inserted just before the trailing header copy, with an entry appended
		//to both pages map copies and the section map repointed at it.
		var rs = ACadSharp.IO.DWG.DwgStreamWriters.Dwg21ReedSolomon.DataPages;
		int aligned = (pageData.Length + 7) & ~7;
		int blocks = (aligned + rs.K - 1) / rs.K;
		byte[] src = new byte[blocks * rs.K];
		pageData.CopyTo(src, 0);
		byte[] page = rs.EncodeInterleaved(src, blocks);
		long newPageSize = (page.Length + 0x1F) & ~0x1F;

		//Grow both pages map copies by one entry.
		byte[] pm2Data = new byte[pmData.Length + 16];
		pmData.CopyTo(pm2Data, 0);
		BitConverter.GetBytes(newPageSize).CopyTo(pm2Data, pmData.Length);
		BitConverter.GetBytes(41L).CopyTo(pm2Data, pmData.Length + 8);

		byte[] pmComp2;
		using (var ms = new MemoryStream())
		{
			new DwgLZ77AC21Compressor().Compress(pm2Data, 0, pm2Data.Length, ms);
			pmComp2 = ms.ToArray();
		}

		var rsSys0 = ACadSharp.IO.DWG.DwgStreamWriters.Dwg21ReedSolomon.SystemPages;
		int alignedPm = (pmComp2.Length + 7) & ~7;
		int pmBlocks = (alignedPm + rsSys0.K - 1) / rsSys0.K;
		byte[] pmSrc = new byte[pmBlocks * rsSys0.K];
		pmComp2.CopyTo(pmSrc, 0);
		byte[] pmPage = rsSys0.EncodeInterleaved(pmSrc, pmBlocks);

		long pm1Off = (long)pmOffset;
		long pm1Slot = records[(long)BitConverter.ToUInt64(metadata, 0x40)].size;
		long pm2MapId = (long)BitConverter.ToUInt64(metadata, 0x30);
		(long pm2Off, long pm2Slot) = records[pm2MapId];
		Assert.True(pmPage.Length <= pm1Slot && pmPage.Length <= pm2Slot, "grown pages map exceeds its slots");

		//The new page's accumulated offset = end of the accounted pages = old header2 offset.
		long header2Old = (long)BitConverter.ToUInt64(metadata, 0x48);

		byte[] hybrid = new byte[real.Length + newPageSize];
		//Everything up to the old header2 position stays; the new page goes in; header2 follows.
		long absHeader2Old = 0x480 + header2Old;
		Array.Copy(real, 0, hybrid, 0, absHeader2Old);
		page.CopyTo(hybrid, absHeader2Old);
		Array.Copy(real, absHeader2Old, hybrid, absHeader2Old + newPageSize, real.Length - absHeader2Old);

		//Write the grown pages map into both slots.
		Array.Clear(hybrid, (int)(0x480 + pm1Off), (int)pm1Slot);
		pmPage.CopyTo(hybrid, (int)(0x480 + pm1Off));
		Array.Clear(hybrid, (int)(0x480 + pm2Off), (int)pm2Slot);
		pmPage.CopyTo(hybrid, (int)(0x480 + pm2Off));

		//Update the section map entry: page id 41, comp = uncomp.
		byte[] sm2 = smData.ToArray();
		BitConverter.GetBytes(41UL).CopyTo(sm2, entryPos + 0x10);
		BitConverter.GetBytes(pUn).CopyTo(sm2, entryPos + 0x20);

		//Rebuild the section map in place (H6-proven vehicle) and the header.
		byte[] smComp2;
		using (var ms = new MemoryStream())
		{
			new DwgLZ77AC21Compressor().Compress(sm2, 0, sm2.Length, ms);
			smComp2 = ms.ToArray();
		}

		var rsSys = ACadSharp.IO.DWG.DwgStreamWriters.Dwg21ReedSolomon.SystemPages;
		int alignedSm = (smComp2.Length + 7) & ~7;
		int smBlocks = (alignedSm + rsSys.K - 1) / rsSys.K;
		byte[] smSrc = new byte[smBlocks * rsSys.K];
		smComp2.CopyTo(smSrc, 0);
		byte[] smPage = rsSys.EncodeInterleaved(smSrc, smBlocks);
		Array.Clear(hybrid, (int)(0x480 + smOff), (int)smSlot);
		smPage.CopyTo(hybrid, (int)(0x480 + smOff));

		byte[] meta = metadata.ToArray();
		BitConverter.GetBytes((ulong)smComp2.Length).CopyTo(meta, 0xB0);
		BitConverter.GetBytes(1UL).CopyTo(meta, 0xD8);
		BitConverter.GetBytes((ulong)pmComp2.Length).CopyTo(meta, 0x50);
		BitConverter.GetBytes((ulong)pm2Data.Length).CopyTo(meta, 0x58);
		BitConverter.GetBytes(1UL).CopyTo(meta, 0x18);
		BitConverter.GetBytes((ulong)(header2Old + newPageSize)).CopyTo(meta, 0x48);
		BitConverter.GetBytes((ulong)hybrid.Length).CopyTo(meta, 0x08);
		BitConverter.GetBytes((ulong)(BitConverter.ToUInt64(metadata, 0x60) + 1)).CopyTo(meta, 0x60);
		BitConverter.GetBytes(41UL).CopyTo(meta, 0x68); //PagesMaxId
		byte[] header = rebuildHeaderPage(meta);
		Array.Clear(hybrid, 0x80, 0x400);
		header.CopyTo(hybrid, 0x80);
		Array.Copy(real, 0x80 + 0x3D8, hybrid, 0x80 + 0x3D8, 0x28);
		File.WriteAllBytes(Path.Combine(outDir, "h8_uncompressed_page.dwg"), hybrid);

		//H8-CONTROL: the identical insertion surgery, but the relocated page keeps its ORIGINAL
		//compressed+RS bytes and its original comp/uncomp entry values. If this one opens, the
		//surgery is sound and H8's refusal convicts comp==uncomp alone.
		{
			byte[] pageC = raw; //the original stored page bytes
			long newSizeC = (pageC.Length + 0x1F) & ~0x1F;

			byte[] pmDataC = new byte[pmData.Length + 16];
			pmData.CopyTo(pmDataC, 0);
			BitConverter.GetBytes(newSizeC).CopyTo(pmDataC, pmData.Length);
			BitConverter.GetBytes(41L).CopyTo(pmDataC, pmData.Length + 8);
			byte[] pmCompC;
			using (var ms = new MemoryStream())
			{
				new DwgLZ77AC21Compressor().Compress(pmDataC, 0, pmDataC.Length, ms);
				pmCompC = ms.ToArray();
			}

			int alignedPmC = (pmCompC.Length + 7) & ~7;
			int pmBlocksC = (alignedPmC + rsSys0.K - 1) / rsSys0.K;
			byte[] pmSrcC = new byte[pmBlocksC * rsSys0.K];
			pmCompC.CopyTo(pmSrcC, 0);
			byte[] pmPageC = rsSys0.EncodeInterleaved(pmSrcC, pmBlocksC);

			byte[] hybridC = new byte[real.Length + newSizeC];
			Array.Copy(real, 0, hybridC, 0, absHeader2Old);
			pageC.CopyTo(hybridC, absHeader2Old);
			Array.Copy(real, absHeader2Old, hybridC, absHeader2Old + newSizeC, real.Length - absHeader2Old);
			Array.Clear(hybridC, (int)(0x480 + pm1Off), (int)pm1Slot);
			pmPageC.CopyTo(hybridC, (int)(0x480 + pm1Off));
			Array.Clear(hybridC, (int)(0x480 + pm2Off), (int)pm2Slot);
			pmPageC.CopyTo(hybridC, (int)(0x480 + pm2Off));

			byte[] smC = smData.ToArray();
			BitConverter.GetBytes(41UL).CopyTo(smC, entryPos + 0x10); //only the page id changes
			byte[] smCompC;
			using (var ms = new MemoryStream())
			{
				new DwgLZ77AC21Compressor().Compress(smC, 0, smC.Length, ms);
				smCompC = ms.ToArray();
			}

			int alignedSmC = (smCompC.Length + 7) & ~7;
			int smBlocksC = (alignedSmC + rsSys.K - 1) / rsSys.K;
			byte[] smSrcC = new byte[smBlocksC * rsSys.K];
			smCompC.CopyTo(smSrcC, 0);
			byte[] smPageC = rsSys.EncodeInterleaved(smSrcC, smBlocksC);
			Array.Clear(hybridC, (int)(0x480 + smOff), (int)smSlot);
			smPageC.CopyTo(hybridC, (int)(0x480 + smOff));

			byte[] metaC = metadata.ToArray();
			BitConverter.GetBytes((ulong)smCompC.Length).CopyTo(metaC, 0xB0);
			BitConverter.GetBytes(1UL).CopyTo(metaC, 0xD8);
			BitConverter.GetBytes((ulong)pmCompC.Length).CopyTo(metaC, 0x50);
			BitConverter.GetBytes((ulong)pmDataC.Length).CopyTo(metaC, 0x58);
			BitConverter.GetBytes(1UL).CopyTo(metaC, 0x18);
			BitConverter.GetBytes((ulong)(header2Old + newSizeC)).CopyTo(metaC, 0x48);
			BitConverter.GetBytes((ulong)hybridC.Length).CopyTo(metaC, 0x08);
			BitConverter.GetBytes((ulong)(BitConverter.ToUInt64(metadata, 0x60) + 1)).CopyTo(metaC, 0x60);
			BitConverter.GetBytes(41UL).CopyTo(metaC, 0x68);
			byte[] headerC2 = rebuildHeaderPage(metaC);
			Array.Clear(hybridC, 0x80, 0x400);
			headerC2.CopyTo(hybridC, 0x80);
			Array.Copy(real, 0x80 + 0x3D8, hybridC, 0x80 + 0x3D8, 0x28);
			File.WriteAllBytes(Path.Combine(outDir, "h8control_moved_page.dwg"), hybridC);
		}
	}

	[Fact]
	public void DiagnosticPairDiff()
	{
		string realPath = Environment.GetEnvironmentVariable("MOREDWG_AC21_REAL");
		string minePath = Environment.GetEnvironmentVariable("MOREDWG_AC21_MINE");
		if (string.IsNullOrEmpty(realPath) || string.IsNullOrEmpty(minePath))
		{
			return;
		}

		string describe(string path)
		{
			byte[] file = File.ReadAllBytes(path);
			byte[] meta = decodeFileHeaderPage(file);
			var sb = new System.Text.StringBuilder();
			sb.Append($"== {Path.GetFileName(path)} len=0x{file.Length:X}\n");
			sb.Append($"meta: fileSize=0x{BitConverter.ToUInt64(meta, 8):X} pmOff=0x{BitConverter.ToUInt64(meta, 0x38):X} pmId={BitConverter.ToUInt64(meta, 0x40)} pm2Off=0x{BitConverter.ToUInt64(meta, 0x28):X} pm2Id={BitConverter.ToUInt64(meta, 0x30)} " +
				$"hdr2=0x{BitConverter.ToUInt64(meta, 0x48):X} pmComp=0x{BitConverter.ToUInt64(meta, 0x50):X} pmUncomp=0x{BitConverter.ToUInt64(meta, 0x58):X} pmFactor={BitConverter.ToUInt64(meta, 0x18)} " +
				$"pages={BitConverter.ToUInt64(meta, 0x60)} maxId={BitConverter.ToUInt64(meta, 0x68)} sectAmount={BitConverter.ToUInt64(meta, 0xA0)} smId={BitConverter.ToUInt64(meta, 0xC0)} sm2Id={BitConverter.ToUInt64(meta, 0xB8)} " +
				$"smComp=0x{BitConverter.ToUInt64(meta, 0xB0):X} smUncomp=0x{BitConverter.ToUInt64(meta, 0xC8):X} smFactor={BitConverter.ToUInt64(meta, 0xD8)}\n");

			byte[] pmData = decodeSystemPage(file, BitConverter.ToUInt64(meta, 0x38),
				BitConverter.ToUInt64(meta, 0x50), BitConverter.ToUInt64(meta, 0x58), BitConverter.ToUInt64(meta, 0x18));
			long acc2 = 0;
			var recs = new Dictionary<long, (long off, long size)>();
			sb.Append("pages: ");
			for (int i = 0; i + 16 <= pmData.Length; i += 16)
			{
				long size = BitConverter.ToInt64(pmData, i);
				long rawId = BitConverter.ToInt64(pmData, i + 8);
				sb.Append($"({rawId}:0x{size:X})");
				recs[Math.Abs(rawId)] = (acc2, size);
				acc2 += size;
			}

			sb.Append("\nsections:\n");
			ulong smId2 = BitConverter.ToUInt64(meta, 0xC0);
			byte[] smData = decodeSystemPage(file, (ulong)recs[(long)smId2].off,
				BitConverter.ToUInt64(meta, 0xB0), BitConverter.ToUInt64(meta, 0xC8), BitConverter.ToUInt64(meta, 0xD8));
			int pos = 0;
			while (pos + 0x40 <= smData.Length)
			{
				ulong dataSize = BitConverter.ToUInt64(smData, pos);
				ulong maxSize = BitConverter.ToUInt64(smData, pos + 8);
				ulong hash = BitConverter.ToUInt64(smData, pos + 0x18);
				long nameLen = BitConverter.ToInt64(smData, pos + 0x20);
				ulong enc = BitConverter.ToUInt64(smData, pos + 0x30);
				long numPages = BitConverter.ToInt64(smData, pos + 0x38);
				pos += 0x40;
				string name = nameLen > 0 ? Encoding.Unicode.GetString(smData, pos, (int)nameLen).TrimEnd('\0') : "<terminator>";
				pos += (int)nameLen;
				sb.Append($"  {name}: data=0x{dataSize:X} max=0x{maxSize:X} hash=0x{hash:X} enc={enc} pages={numPages}");
				for (int p = 0; p < numPages && p < 3; p++)
				{
					ulong pOff = BitConverter.ToUInt64(smData, pos + p * 0x38);
					ulong pSz = BitConverter.ToUInt64(smData, pos + p * 0x38 + 8);
					ulong pId = BitConverter.ToUInt64(smData, pos + p * 0x38 + 0x10);
					ulong pUn = BitConverter.ToUInt64(smData, pos + p * 0x38 + 0x18);
					ulong pCo = BitConverter.ToUInt64(smData, pos + p * 0x38 + 0x20);
					sb.Append($" (o=0x{pOff:X},s=0x{pSz:X},id={pId},u=0x{pUn:X},c=0x{pCo:X})");
				}

				pos += 0x38 * (int)numPages;
				sb.Append("\n");
			}

			return sb.ToString();
		}

		Assert.Fail(describe(realPath) + "\n" + describe(minePath));
	}

	private static int getWindows1252()
	{
		return 1252;
	}

	private static byte[] decodeSystemPage(byte[] file, ulong offset, ulong comp, ulong uncomp, ulong factor)
	{
		ulong aligned = (comp + 7) & ~7UL;
		uint totalSize = (uint)(aligned * factor);
		int blocks = (int)(totalSize + 238) / 239;
		byte[] raw = file.Skip((int)(0x480 + offset)).Take(blocks * 255).ToArray();
		byte[] compData = deinterleave(raw, blocks, 239, (int)totalSize);
		byte[] data = new byte[uncomp];
		new DwgLZ77AC21Decompressor().Decompress(compData, 0U, (uint)comp, data);
		return data;
	}

	[Fact]
	public void DiagnosticMetadataDiff()
	{
		string oursPath = Environment.GetEnvironmentVariable("MOREDWG_AC21_DIFF");
		if (string.IsNullOrEmpty(oursPath) || !File.Exists(oursPath))
		{
			return; //only runs when pointed at a written file
		}

		byte[] real = readFile();
		byte[] ours = File.ReadAllBytes(oursPath);
		byte[] realMeta = decodeFileHeaderPage(real);
		byte[] oursMeta = decodeFileHeaderPage(ours);

		string[] names =
		{
			"HeaderSize", "FileSize", "PagesMapCrcCompressed", "PagesMapCorrectionFactor",
			"PagesMapCrcSeed", "Map2Offset", "Map2Id", "PagesMapOffset", "PagesMapId",
			"Header2offset", "PagesMapSizeCompressed", "PagesMapSizeUncompressed", "PagesAmount",
			"PagesMaxId", "U0x20", "U0x40", "PagesMapCrcUncompressed", "U0xF800", "U4", "U1",
			"SectionsAmount", "SectionsMapCrcUncompressed", "SectionsMapSizeCompressed",
			"SectionsMap2Id", "SectionsMapId", "SectionsMapSizeUncompressed",
			"SectionsMapCrcCompressed", "SectionsMapCorrectionFactor", "SectionsMapCrcSeed",
			"StreamVersion", "CrcSeed", "CrcSeedEncoded", "RandomSeed", "HeaderCRC64",
		};

		var sb = new System.Text.StringBuilder();
		for (int i = 0; i < names.Length; i++)
		{
			ulong a = BitConverter.ToUInt64(realMeta, i * 8);
			ulong b = BitConverter.ToUInt64(oursMeta, i * 8);
			sb.Append($"{names[i]}: real=0x{a:X} ours=0x{b:X}{(a == b ? "" : "  <<<")}\n");
		}

		sb.Append("meta0x00 real: " + BitConverter.ToString(real, 0, 0x30) + "\n");
		sb.Append("meta0x00 ours: " + BitConverter.ToString(ours, 0, 0x30) + "\n");
		Assert.Fail(sb.ToString());
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
