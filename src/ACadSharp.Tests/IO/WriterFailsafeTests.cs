using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using ACadSharp.XData;
using CSMath;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO;

/// <summary>
/// One object that cannot be written must cost that object, not the drawing.
/// </summary>
/// <remarks>
/// Neither writer had a single catch in it: every <c>throw</c> on the write path - an evaluation
/// expression carrying a group code nobody implemented, an extended data record of an unknown type,
/// a visual style property of an unknown kind, an entity type nobody added - came straight out of
/// <c>Write()</c>, and the caller was left with <b>no file at all</b>. The application builds its
/// entities in code and writes DXF, which is exactly the path where something unexpected turns up;
/// its own export defect (T80) had this shape, four client drawings producing no file.
///
/// The tests below were written against the code BEFORE the failsafe existed and every one of them
/// failed there, with the exception escaping <c>Write()</c> - which is the only reason to trust them
/// now that they pass (13 §5g).
/// </remarks>
public class WriterFailsafeTests
{
	/// <summary>
	/// A record of a kind no writer knows. Both writers switch on the record's type and throw at
	/// their default branch, so this reproduces the real shape without inventing a fake failure.
	/// </summary>
	private sealed class UnknownExtendedDataRecord : ExtendedDataRecord
	{
		public UnknownExtendedDataRecord()
			: base(DxfCode.ExtendedDataAsciiString, "moredwg-unknown-record")
		{
		}
	}

	[Theory]
	[InlineData(ACadVersion.AC1015)]
	[InlineData(ACadVersion.AC1032)]
	public void ADwgKeepsTheDrawingWhenOneEntityCannotBeWritten(ACadVersion version)
	{
		CadDocument doc = documentWithOneUnwritableEntity();
		doc.Header.Version = version;
		List<string> errors = new();

		using MemoryStream stream = new();
		DwgWriter.Write(stream, doc, new DwgWriterConfiguration { CloseStream = false }, (s, e) =>
		{
			if (e.NotificationType == NotificationType.Error)
			{
				errors.Add(e.Message);
			}
		});

		byte[] bytes = stream.ToArray();
		Assert.True(bytes.Length > 0, "the file is empty: one entity took the whole drawing with it");

		CadDocument back = DwgReader.Read(new MemoryStream(bytes));
		Line[] lines = back.Entities.OfType<Line>().ToArray();

		//The good line is still there; the one carrying the record nobody can write is not, and the
		//caller was told which one and why.
		Assert.Single(lines);
		Assert.Equal(new XYZ(0, 0, 0), lines[0].StartPoint);
		Assert.Contains(errors, m => m.Contains("could not be written and was left out"));
	}

	[Fact]
	public void ADxfKeepsTheDrawingWhenOneEntityCannotBeWritten()
	{
		CadDocument doc = documentWithOneUnwritableEntity();
		List<string> errors = new();

		using MemoryStream stream = new();
		DxfWriter.Write(stream, doc, false, new DxfWriterConfiguration { CloseStream = false }, (s, e) =>
		{
			if (e.NotificationType == NotificationType.Error)
			{
				errors.Add(e.Message);
			}
		});

		byte[] bytes = stream.ToArray();
		Assert.True(bytes.Length > 0, "the file is empty: one entity took the whole drawing with it");

		CadDocument back = DxfReader.Read(new MemoryStream(bytes));
		Line[] lines = back.Entities.OfType<Line>().ToArray();

		Assert.Single(lines);
		Assert.Equal(new XYZ(0, 0, 0), lines[0].StartPoint);
		Assert.Contains(errors, m => m.Contains("could not be written and was left out"));
	}

	/// <summary>
	/// A half written record is worse than either outcome, so the DXF path buffers the record and
	/// only lets it into the file once it is complete. If it did not, the codes written before the
	/// failure would still be in the text.
	/// </summary>
	[Fact]
	public void ADxfDoesNotLeaveHalfOfTheFailedRecordInTheFile()
	{
		CadDocument doc = documentWithOneUnwritableEntity();

		using MemoryStream stream = new();
		DxfWriter.Write(stream, doc, false, new DxfWriterConfiguration { CloseStream = false }, null);
		string text = System.Text.Encoding.UTF8.GetString(stream.ToArray());

		//The application name is written FIRST, and only then are the records of that block written -
		//so the throw happens with 1001 MOREDWG_FAILSAFE already emitted. Unbuffered, the name would
		//be in the file twice: once as the APPID table entry, once as the fragment of the entity
		//that failed. Buffered, only the table entry survives.
		//
		//Two earlier attempts at this assertion were wrong, not the writer: the layer name "doomed"
		//is in the file legitimately as a LAYER table entry, and "AcDbLine" appears four times
		//because the CLASSES section names it too.
		Assert.Equal(1, occurrences(text, "MOREDWG_FAILSAFE"));
	}

	/// <summary>
	/// Off, the caller gets the exception it used to get - for anyone who would rather fail loudly
	/// than ship a drawing with something missing.
	/// </summary>
	[Fact]
	public void FailsafeOffStillThrows()
	{
		CadDocument doc = documentWithOneUnwritableEntity();

		using MemoryStream stream = new();
		DwgWriterConfiguration configuration = new() { Failsafe = false, CloseStream = false };
		Assert.ThrowsAny<Exception>(() => DwgWriter.Write(stream, doc, configuration, null));
	}

	private static int occurrences(string text, string value)
	{
		int count = 0;
		for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
		{
			count++;
		}

		return count;
	}

	/// <summary>
	/// A document with two lines, one of which carries an extended data record no writer knows.
	/// </summary>
	private static CadDocument documentWithOneUnwritableEntity()
	{
		CadDocument doc = new CadDocument();

		AppId app = new AppId("MOREDWG_FAILSAFE");
		doc.AppIds.Add(app);

		Layer doomed = new Layer("doomed");
		doc.Layers.Add(doomed);

		Line good = new Line(XYZ.Zero, new XYZ(10, 0, 0));
		Line bad = new Line(new XYZ(0, 10, 0), new XYZ(10, 10, 0)) { Layer = doomed };
		bad.ExtendedData.Add(app, new List<ExtendedDataRecord> { new UnknownExtendedDataRecord() });

		doc.Entities.Add(good);
		doc.Entities.Add(bad);
		return doc;
	}
}
