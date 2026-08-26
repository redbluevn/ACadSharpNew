using ACadSharp.Exceptions;
using ACadSharp.IO.DWG;
using ACadSharp.IO.DWG.DwgStreamWriters;
using ACadSharp.Tables.Collections;
using CSUtilities.IO;
using CSUtilities.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CSUtilities.Converters;
using System.Text;

namespace ACadSharp.IO;

/// <summary>
/// Class for writing a DWG from a <see cref="CadDocument"/>.
/// </summary>
public class DwgWriter : CadWriterBase<DwgWriterConfiguration>
{
	public DwgPreview Preview { get; set; }

	private ACadVersion _version { get { return this._document.Header.Version; } }

	private DwgFileHeader _fileHeader;

	private IDwgFileHeaderWriter _fileHeaderWriter;

	private Dictionary<ulong, long> _handlesMap = new Dictionary<ulong, long>();

	/// <summary>
	/// Initializes a new instance of the <see cref="DwgWriter"/> class.
	/// </summary>
	/// <param name="filename"></param>
	/// <param name="document"></param>
	public DwgWriter(string filename, CadDocument document)
		: this(File.Create(filename), document)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="DwgWriter"/> class.
	/// </summary>
	/// <param name="stream"></param>
	/// <param name="document"></param>
	public DwgWriter(Stream stream, CadDocument document) : base(stream, document)
	{
		this._fileHeader = DwgFileHeader.CreateFileHeader(this._version);
	}

	/// <summary>
	/// Write a <see cref="CadDocument"/> into a file
	/// </summary>
	/// <param name="filename"></param>
	/// <param name="document"></param>
	/// <param name="configuration"></param>
	/// <param name="notification"></param>
	public static void Write(string filename, CadDocument document, DwgWriterConfiguration configuration = null, NotificationEventHandler notification = null)
	{
		using (DwgWriter writer = new DwgWriter(filename, document))
		{
			if (configuration != null)
			{
				writer.Configuration = configuration;
			}

			writer.OnNotification += notification;
			writer.Write();
		}
	}

	/// <summary>
	/// Write a <see cref="CadDocument"/> intio a <see cref="Stream"/>
	/// </summary>
	/// <param name="stream"></param>
	/// <param name="document"></param>
	/// <param name="notification"></param>
	public static void Write(Stream stream, CadDocument document, DwgWriterConfiguration configuration = null, NotificationEventHandler notification = null)
	{
		using (DwgWriter writer = new DwgWriter(stream, document))
		{
			if (configuration != null)
			{
				writer.Configuration = configuration;
			}

			writer.OnNotification += notification;
			writer.Write();
		}
	}

	/// <inheritdoc/>
	public override void Dispose()
	{
		this._stream.Dispose();
	}

	/// <inheritdoc/>
	public override void Write()
	{
		base.Write();

		if (this._version < ACadVersion.AC1018)
		{
			this._document.VEntityControl ??= new ViewportEntityControl(this._document);
		}

		this.getFileHeaderWriter();

		this.writeHeader();
		this.writeClasses();
		//this.writeEmptySection();
		this.writeSummaryInfo();
		this.writePreview();
		//this.writeVBASection();
		this.writeAppInfo();
		this.writeFileDepList();
		//No AcDb:XrefManifest: it is optional, contrary to what this writer assumed before.  Strip
		//it out of a real R2007 file entirely, rebuild the section map, and AutoCAD still opens the
		//file with AUDIT 0; and AutoCAD's own R2007 output for a drawing that never had one does
		//not carry one either.  The old "required" reading came from RENAMING the section, which
		//leaves the reader a name it does not know.
		this.writeAppInfoHistory();
		this.writeRevHistory();
		//this.writeSecurity();
		this.writeAuxHeader();
		this.writeObjects();
		this.writeObjFreeSpace();
		this.writeTemplate();
		this.writePrototype();

		//Write in the last place to avoid conflicts with versions < AC1018
		this.writeHandles();

		this._fileHeaderWriter.WriteFile();

		this._stream.Flush();

		if (this.Configuration.CloseStream)
		{
			this._stream.Close();
		}
	}

	private void getFileHeaderWriter()
	{
		switch (this._document.Header.Version)
		{
			case ACadVersion.MC0_0:
			case ACadVersion.AC1_2:
			case ACadVersion.AC1_4:
			case ACadVersion.AC1_50:
			case ACadVersion.AC2_10:
			case ACadVersion.AC1002:
			case ACadVersion.AC1003:
			case ACadVersion.AC1004:
			case ACadVersion.AC1006:
			case ACadVersion.AC1009:
			case ACadVersion.AC1012:
				throw new CadNotSupportedException(this._document.Header.Version);
			case ACadVersion.AC1014:
				//The file header of R14 is written correctly - five section locator records, no
				//auxiliary header, an empty object free space - but AutoCAD still refuses to open
				//the result, so the rest of an R14 file is not right yet. Say so instead of handing
				//back a drawing that looks fine and is not: AC1015 is the oldest version that works.
				this.triggerNotification(
					$"A DWG written as {ACadVersion.AC1014} is not accepted by AutoCAD; the oldest version that opens is {ACadVersion.AC1015}",
					NotificationType.Warning);
				this._fileHeaderWriter = new DwgFileHeaderWriterAC15(this._stream, this._encoding, this._document);
				break;
			case ACadVersion.AC1015:
				this._fileHeaderWriter = new DwgFileHeaderWriterAC15(this._stream, this._encoding, this._document);
				break;
			case ACadVersion.AC1018:
				this._fileHeaderWriter = new DwgFileHeaderWriterAC18(this._stream, this._encoding, this._document);
				break;
			case ACadVersion.AC1021:
				//T87: the R2007 container, built against this library's own reader and validated
				//codec by codec against a real AC1021 file - see DwgFileHeaderWriterAC21.
				this._fileHeaderWriter = new DwgFileHeaderWriterAC21(this._stream, this._encoding, this._document);
				break;
			case ACadVersion.AC1024:
			case ACadVersion.AC1027:
			case ACadVersion.AC1032:
				this._fileHeaderWriter = new DwgFileHeaderWriterAC18(this._stream, this._encoding, this._document);
				break;
			case ACadVersion.Unknown:
			default:
				throw new CadNotSupportedException();
		}
	}

	/// <summary>
	/// Builds the AcDs data section, where an R2013+ drawing keeps the geometry of its regions,
	/// solids and bodies.
	/// </summary>
	/// <remarks>
	/// Only R2013 and later keep geometry here; earlier versions carry it inside the entity, so a
	/// store there would have nothing to say. The entities that get a record are exactly the ones
	/// whose has-DS bit is set - see <see cref="DwgPrototype1bWriter.KeepsGeometryInDataStore"/>.
	/// Measured in AutoCAD 2027: every drawing measured, from one box to a production sample with
	/// two solids and a region, opens and audits 0 with nothing erased, and AutoCAD's own DXF export
	/// of the file counts the same entities and ASM_Data records as its export of the source.
	/// </remarks>
	private void writePrototype()
	{
		if (this._fileHeader.AcadVersion < ACadVersion.AC1027)
		{
			//Older versions carry the payload inside the entity, so there is nothing to put here.
			return;
		}

		var payloads = DwgPrototype1bWriter.CollectPayloads(this._document, this._fileHeader.AcadVersion);
		if (!payloads.Any())
		{
			return;
		}

		DwgPrototype1bWriter writer = new(payloads);
		this._fileHeaderWriter.AddSection(DwgSectionDefinition.AcDsPrototype, writer.Write(), true);
	}

	private void writeAppInfo()
	{
		if (this._fileHeader.AcadVersion < ACadVersion.AC1018)
			return;

		MemoryStream stream = new MemoryStream();
		DwgAppInfoWriter writer = new DwgAppInfoWriter(this._version, stream);
		writer.Write();

		this._fileHeaderWriter.AddSection(DwgSectionDefinition.AppInfo, stream, false, 0x80);
	}

	private void writeAuxHeader()
	{
		MemoryStream stream = new MemoryStream();
		DwgAuxHeaderWriter writer = new DwgAuxHeaderWriter(stream, this._encoding, this._document.Header);
		writer.Write();

		this._fileHeaderWriter.AddSection(DwgSectionDefinition.AuxHeader, stream, true);
	}

	private void writeClasses()
	{
		MemoryStream stream = new MemoryStream();
		DwgClassesWriter writer = new DwgClassesWriter(stream, this._document, this._encoding);
		writer.Write();

		this._fileHeaderWriter.AddSection(DwgSectionDefinition.Classes, stream, true);
	}

	//The 32 bytes an AcDb:AppInfoHistory section opens with. They are byte for byte the same in
	//every real R2007 file measured (four of them, all with different bodies), so they are a
	//constant of the section and not a digest of what follows it.
	private static readonly byte[] _appInfoHistoryPrefix =
	{
		0x53, 0xde, 0x38, 0x1d, 0xec, 0x43, 0x21, 0xca, 0x96, 0x19, 0xe1, 0xe2, 0x17, 0x1a, 0x2a, 0x67,
		0x3b, 0xd9, 0x7f, 0xf7, 0x3c, 0xbb, 0xce, 0x08, 0xa0, 0x53, 0xd8, 0xed, 0xd2, 0x8d, 0xc5, 0xc7,
	};

	private void writeAppInfoHistory()
	{
		//An R2007 drawing needs this section, which lists the applications that have saved it. It
		//is shaped like AcDb:AppInfo - a class version, the name "AppInfoDataList", an entry count
		//and then that many (16-byte id, string) pairs - with a 32-byte constant in front. The
		//third entry is an OLE property set, and its two datetimes are a second copy of the
		//drawing's created and modified times: AutoCAD refuses an R2007 file whose copies of a
		//value disagree, the same rule that governs the editing time in AcDb:SummaryInfo.
		if (this._fileHeader.AcadVersion != ACadVersion.AC1021)
		{
			return;
		}

		string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
		CadSummaryInfo info = this._document.SummaryInfo;
		string stamp(DateTime value) => value.ToString("yyyy-MM-ddTHH:mm:ss");

		MemoryStream stream = new MemoryStream();
		IDwgStreamWriter writer = DwgStreamWriterBase.GetStreamWriter(this._version, stream, Encoding.Unicode);

		writer.WriteBytes(_appInfoHistoryPrefix);
		//UInt32 class version, then the list name, then the entry count.
		writer.WriteInt(0);
		writer.WriteTextUnicode("AppInfoDataList");
		writer.WriteInt(4);

		writer.WriteBytes(new byte[16]);
		writer.WriteTextUnicode(version);
		writer.WriteBytes(new byte[16]);
		writer.WriteTextUnicode("This is a comment from ACadSharp");
		writer.WriteBytes(new byte[16]);
		writer.WriteTextUnicode(
			"<prop_set fmt_id=\"{f29f85e0-4ff9-1068-ab91-08002b27b3d9}\">" +
			$"<prop id=\"8\"><string>{info.LastSavedBy}</string></prop>" +
			$"<prop id=\"10\"><datetime>{stamp(info.ModifiedDate)}</datetime></prop>" +
			"<prop id=\"258\"><string>ACadSharp</string></prop>" +
			$"<prop id=\"259\"><string>{version}</string></prop>" +
			$"<prop id=\"12\"><datetime>{stamp(info.CreatedDate)}</datetime></prop>" +
			"</prop_set>");
		writer.WriteBytes(new byte[16]);
		writer.WriteTextUnicode(
			$"<ProductInformation name =\"ACadSharp\" build_version=\"{version}\" registry_version=\"{version}\" install_id_string=\"ACadSharp\" registry_localeID=\"1033\"/>");

		this._fileHeaderWriter.AddSection(DwgSectionDefinition.AppInfoHistory, stream, false, 0x600);
	}

	private void writeFileDepList()
	{
		if (this._fileHeader.AcadVersion < ACadVersion.AC1018)
			return;

		MemoryStream stream = new MemoryStream();
		StreamIO swriter = new StreamIO(stream);

		//nt32	4	Feature count(ftc)
		swriter.Write<uint>(0);

		//String32	ftc * (4 + n)	Feature name list.A feature name is one of the following:
		/*
		 * “Acad: XRef” (for block table record)
		 * “Acad: Image” (for image definition)
		 * “Acad: PlotConfig” (for plotsetting)
		 * “Acad: Text” (for text style)
		*/

		//Int32	4	File count
		swriter.Write<uint>(0);

		//Then follows an array of features(repeated file count times). The feature name + the full filename constitute the lookup key of a file dependency:

		//String32	4 + n	Full filename
		//String32	4 + n	Found path, path at which file was found
		//String32	4 + n	Fingerprint GUID(applies to xref’s only)
		//String32	4 + n	Version GUID(applies to xref’s only)
		//Int32	4	Feature index in the feature list above.
		//Int32	4	Timestamp(Seconds since 1 / 1 / 1980)
		//Int32	4	Filesize
		//Int16	2	Affects graphics(1 = true, 0 = false)
		//Int32	4	Reference count

		this._fileHeaderWriter.AddSection(DwgSectionDefinition.FileDepList, stream, false, 0x80);
	}

	private void writeHandles()
	{
		MemoryStream stream = new MemoryStream();
		DwgHandleWriter writer = new DwgHandleWriter(this._version, stream, this._handlesMap);
		writer.Write(this._fileHeaderWriter.HandleSectionOffset);

		this._fileHeaderWriter.AddSection(DwgSectionDefinition.Handles, stream, true);
	}

	private void writeHeader()
	{
		MemoryStream stream = new MemoryStream();
		DWG.DwgHeaderWriter writer = new DWG.DwgHeaderWriter(stream, this._document, this._encoding);
		writer.OnNotification += this.triggerNotification;
		writer.Write();

		this._fileHeaderWriter.AddSection(DwgSectionDefinition.Header, stream, true);
	}

	private void writeObjects()
	{
		MemoryStream stream = new MemoryStream();
		DwgObjectWriter writer = new DwgObjectWriter(
			stream,
			this._document,
			this._encoding,
			this.Configuration.WriteXRecords,
			this.Configuration.WriteXData,
			this.Configuration.WriteShapes,
			this.Configuration.WriteDynamicBlockData);
		writer.OnNotification += this.triggerNotification;
		writer.Write();

		this._handlesMap = writer.Map;

		this._fileHeaderWriter.AddSection(DwgSectionDefinition.AcDbObjects, stream, true);
	}

	private void writeObjFreeSpace()
	{
		MemoryStream stream = new MemoryStream();
		StreamIO writer = new StreamIO(stream);

		//Int32	4	0
		writer.Write<int>(0);
		//UInt32	4	Approximate number of objects in the drawing(number of handles).
		writer.Write<uint>((uint)this._handlesMap.Count);

		//Julian datetime	8	If version > R14 then system variable TDUPDATE otherwise TDUUPDATE.
		if (this._version >= ACadVersion.AC1015)
		{
			CadUtils.DateToJulian(this._document.Header.UniversalUpdateDateTime, out int jdate, out int mili);
			writer.Write<int>(jdate);
			writer.Write<int>(mili);
		}
		else
		{
			CadUtils.DateToJulian(this._document.Header.UpdateDateTime, out int jdate, out int mili);
			writer.Write<int>(jdate);
			writer.Write<int>(mili);
		}

		//UInt32	4	Offset of the objects section in the stream.
		writer.Write<uint>(0);  //It may be the cause of failure for version AC1024

		//UInt8	1	Number of 64 - bit values that follow(ODA writes 4).
		writer.Stream.WriteByte(4);
		//UInt32	4	ODA writes 0x00000032
		writer.Write<uint>(0x00000032);
		//UInt32	4	ODA writes 0x00000000
		writer.Write<uint>(0x00000000);
		//UInt32	4	ODA writes 0x00000064
		writer.Write<uint>(0x00000064);
		//UInt32	4	ODA writes 0x00000000
		writer.Write<uint>(0x00000000);
		//UInt32	4	ODA writes 0x00000200
		writer.Write<uint>(0x00000200);
		//UInt32	4	ODA writes 0x00000000
		writer.Write<uint>(0x00000000);
		//UInt32	4	ODA writes 0xffffffff
		writer.Write<uint>(0xffffffff);
		//UInt32	4	ODA writes 0x00000000
		writer.Write<uint>(0x00000000);

		this._fileHeaderWriter.AddSection(DwgSectionDefinition.ObjFreeSpace, stream, true);
	}

	private void writePreview()
	{
		MemoryStream stream = new MemoryStream();
		DwgPreviewWriter writer = new DwgPreviewWriter(this._version, stream);

		if (this.Preview != null && this.Preview.Code != DwgPreview.PreviewType.Unknown)
		{
			writer.Write(this.Preview, this._stream.Position);

			//Page has to fit the image byte size, in intervals of 0x400
			int pageSize = (int)((stream.Length % 0x400) * 0x400 + 0x400);
			this._fileHeaderWriter.AddSection(DwgSectionDefinition.Preview, stream, false, pageSize);
		}
		else
		{
			writer.Write();
			this._fileHeaderWriter.AddSection(DwgSectionDefinition.Preview, stream, false, 0x400);
		}
	}

	private void writeRevHistory()
	{
		if (this._fileHeader.AcadVersion < ACadVersion.AC1018)
			return;

		MemoryStream stream = new MemoryStream();
		StreamIO swriter = new StreamIO(stream);
		swriter.Write<uint>(0);
		swriter.Write<uint>(0);
		swriter.Write<uint>(0);
		this._fileHeaderWriter.AddSection(DwgSectionDefinition.RevHistory, stream, true);
	}

	private void writeSummaryInfo()
	{
		///<see cref="DwgReader.ReadSummaryInfo"/>

		if (this._fileHeader.AcadVersion < ACadVersion.AC1018)
			return;

		MemoryStream stream = new MemoryStream();
		var writer = DwgStreamWriterBase.GetStreamWriter(this._version, stream, TextEncoding.Windows1252());

		CadSummaryInfo info = this._document.SummaryInfo;

		writer.WriteTextUnicode(info.Title);
		writer.WriteTextUnicode(info.Subject);
		writer.WriteTextUnicode(info.Author);
		writer.WriteTextUnicode(info.Keywords);
		writer.WriteTextUnicode(info.Comments);
		writer.WriteTextUnicode(info.LastSavedBy);
		writer.WriteTextUnicode(info.RevisionNumber);
		writer.WriteTextUnicode(info.HyperlinkBase);

		//Total editing time, as whole days plus the milliseconds within that day. The ODA writes
		//two zero Int32s here, and so did this writer - which an R2007 file does not survive:
		//AutoCAD cross-checks the pair against the header's $TDINDWG and refuses the file when
		//they disagree (measured by swapping this section alone into a real R2007 file - zero
		//is refused, and so is any other value than the header's own). Later versions are more
		//forgiving, but writing the document's real value is right for all of them.
		TimeSpan editing = this._document.Header.TotalEditingTime;
		writer.WriteInt((int)editing.TotalDays);
		writer.WriteInt((int)Math.Round(editing.Subtract(TimeSpan.FromDays((int)editing.TotalDays)).TotalMilliseconds));

		writer.Write8BitJulianDate(info.CreatedDate);
		writer.Write8BitJulianDate(info.ModifiedDate);

		//Int16	2 + 2 * (2 + n)	Property count, followed by PropertyCount key/value string pairs.
		writer.WriteRawShort((ushort)info.Properties.Count);
		foreach (KeyValuePair<string, string> property in info.Properties)
		{
			writer.WriteTextUnicode(property.Key);
			writer.WriteTextUnicode(property.Value);
		}

		writer.WriteInt(0);
		writer.WriteInt(0);

		this._fileHeaderWriter.AddSection(DwgSectionDefinition.SummaryInfo, stream, false, 0x100);
	}

	private void writeTemplate()
	{
		MemoryStream stream = new MemoryStream();
		StreamIO writer = new StreamIO(stream);

		//Int16	2	Template description string length in bytes(the ODA always writes 0 here).
		writer.Write<short>(0);
		//UInt16	2	MEASUREMENT system variable(0 = English, 1 = Metric).
		writer.Write<ushort>((ushort)this._document.Header.MeasurementUnits);

		this._fileHeaderWriter.AddSection(DwgSectionDefinition.Template, stream, true);
	}
}