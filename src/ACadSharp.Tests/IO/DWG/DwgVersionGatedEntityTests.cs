using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO.DWG
{
	/// <summary>
	/// A multileader and a table are dropped when the file is written older than R2010, because only
	/// the R2010 layout is implemented for them. That is a version limit, not a missing entity type:
	/// AutoCAD stores both in its own R2000 and R2004 files, and writing the R2010 layout into one of
	/// those makes AutoCAD refuse the whole drawing (measured). The message has to say which, so a
	/// caller knows that choosing a newer version keeps the object.
	/// </summary>
	public class DwgVersionGatedEntityTests
	{
		[Theory]
		[InlineData(ACadVersion.AC1015)]
		[InlineData(ACadVersion.AC1018)]
		public void MultiLeaderBelowR2010SaysItIsAVersionLimit(ACadVersion version)
		{
			List<string> messages = write(version, out _);

			Assert.Contains(messages, m =>
				m.Contains("MultiLeader", System.StringComparison.Ordinal) &&
				m.Contains(version.ToString(), System.StringComparison.Ordinal) &&
				m.Contains(ACadVersion.AC1024.ToString(), System.StringComparison.Ordinal));
		}

		[Theory]
		[InlineData(ACadVersion.AC1015)]
		[InlineData(ACadVersion.AC1018)]
		public void TableBelowR2010SaysItIsAVersionLimit(ACadVersion version)
		{
			List<string> messages = write(version, out _);

			Assert.Contains(messages, m =>
				m.Contains("TableEntity", System.StringComparison.Ordinal) &&
				m.Contains(version.ToString(), System.StringComparison.Ordinal) &&
				m.Contains(ACadVersion.AC1024.ToString(), System.StringComparison.Ordinal));
		}

		[Fact]
		public void FromR2010BothAreWrittenAndNothingIsReported()
		{
			List<string> messages = write(ACadVersion.AC1024, out CadDocument reopened);

			Assert.DoesNotContain(messages, m => m.Contains("oldest version", System.StringComparison.Ordinal));
			Assert.Single(reopened.ModelSpace.Entities.OfType<MultiLeader>());
			Assert.Single(reopened.ModelSpace.Entities.OfType<TableEntity>());
		}

		private static List<string> write(ACadVersion version, out CadDocument reopened)
		{
			CadDocument doc = new CadDocument();
			doc.Header.Version = version;

			MultiLeader leader = new MultiLeader();
			leader.ContextData.HasTextContents = true;
			leader.ContextData.TextLabel = "note";
			leader.ContextData.TextHeight = 2.5;
			MultiLeaderObjectContextData.LeaderRoot root = new MultiLeaderObjectContextData.LeaderRoot
			{
				ConnectionPoint = new XYZ(10, 5, 0),
			};
			MultiLeaderObjectContextData.LeaderLine leaderLine = new MultiLeaderObjectContextData.LeaderLine();
			leaderLine.Points.Add(new XYZ(0, 0, 0));
			root.Lines.Add(leaderLine);
			leader.ContextData.LeaderRoots.Add(root);
			doc.ModelSpace.Entities.Add(leader);

			BlockRecord tableBlock = new BlockRecord("*T1");
			tableBlock.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(4, 0, 0)));
			doc.BlockRecords.Add(tableBlock);
			doc.ModelSpace.Entities.Add(new TableEntity(tableBlock));

			List<string> messages = new List<string>();
			byte[] bytes;
			using (MemoryStream stream = new MemoryStream())
			using (DwgWriter writer = new DwgWriter(stream, doc))
			{
				writer.OnNotification += (s, e) => messages.Add(e.Message);
				writer.Write();
				bytes = stream.ToArray();
			}

			reopened = DwgReader.Read(new MemoryStream(bytes));
			return messages;
		}
	}
}
