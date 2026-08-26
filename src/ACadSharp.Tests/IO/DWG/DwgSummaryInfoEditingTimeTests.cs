using ACadSharp.IO;
using System;
using System.IO;
using Xunit;

namespace ACadSharp.Tests.IO.DWG
{
	/// <summary>
	/// The SummaryInfo section carries a second copy of the header's $TDINDWG. AutoCAD refuses
	/// to open an R2007 drawing where the two disagree - measured by putting nothing but this
	/// section into a real R2007 file: the file opens with the header's own value and is
	/// refused with zero, or with any other value.
	/// </summary>
	public class DwgSummaryInfoEditingTimeTests
	{
		[Theory]
		[InlineData(ACadVersion.AC1018)]
		[InlineData(ACadVersion.AC1021)]
		[InlineData(ACadVersion.AC1024)]
		[InlineData(ACadVersion.AC1032)]
		public void EditingTimeSurvivesTheRoundTrip(ACadVersion version)
		{
			CadDocument doc = new CadDocument();
			doc.Header.Version = version;
			doc.Header.TotalEditingTime = TimeSpan.FromDays(3) + TimeSpan.FromMilliseconds(8124);

			byte[] bytes;
			using (MemoryStream stream = new MemoryStream())
			{
				using (DwgWriter writer = new DwgWriter(stream, doc)) { writer.Write(); }
				bytes = stream.ToArray();
			}

			using DwgReader reader = new DwgReader(new MemoryStream(bytes));
			CadDocument read = reader.Read();

			Assert.Equal(doc.Header.TotalEditingTime, read.SummaryInfo.TotalEditingTime);
			Assert.Equal(doc.Header.TotalEditingTime, read.Header.TotalEditingTime);
		}
	}
}
