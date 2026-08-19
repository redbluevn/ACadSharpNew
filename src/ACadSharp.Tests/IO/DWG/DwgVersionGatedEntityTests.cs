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
	/// A table is dropped when the file is written older than R2010, because only the R2010 layout
	/// of it is implemented. That is a version limit, not a missing entity type - AutoCAD stores
	/// tables in its own R2000 and R2004 files - so the message says which limit it is and which
	/// version keeps the object.
	///
	/// A multileader is no longer dropped: the pre-R2007 stream carries one extra field, the
	/// arrowhead list, which the writer skipped. Without it every value after that point was
	/// misaligned, and the object could not be read back even by this library. With it, AutoCAD
	/// opens the file at AC1015 and AC1018 with 0 errors and the values match its own save of the
	/// same drawing for 14 of the 15 multileaders in the sample.
	/// </summary>
	public class DwgVersionGatedEntityTests
	{
		[Theory]
		[InlineData(ACadVersion.AC1015)]
		[InlineData(ACadVersion.AC1018)]
		public void MultiLeaderIsWrittenAndReadBackBeforeR2010(ACadVersion version)
		{
			List<string> messages = write(version, out CadDocument reopened);

			Assert.DoesNotContain(messages, m => m.Contains("MultiLeader", System.StringComparison.Ordinal));
			MultiLeader reopenedLeader = Assert.Single(reopened.ModelSpace.Entities.OfType<MultiLeader>());
			Assert.Equal("note", reopenedLeader.ContextData.TextLabel);
			MultiLeaderObjectContextData.LeaderRoot root =
				Assert.Single(reopenedLeader.ContextData.LeaderRoots);
			Assert.Equal(10, root.ConnectionPoint.X, 6);
			Assert.Equal(5, root.ConnectionPoint.Y, 6);
			Assert.Single(Assert.Single(root.Lines).Points);
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

		[Theory]
		[InlineData(ACadVersion.AC1015)]
		[InlineData(ACadVersion.AC1018)]
		[InlineData(ACadVersion.AC1024)]
		[InlineData(ACadVersion.AC1032)]
		public void MultiLeaderWithNeitherTextNorBlockContentSurvives(ACadVersion version)
		{
			//The reader always consumes the "has contents block" bit when there is no text content.
			//The writer only wrote it inside the block branch, so a multileader with neither - the
			//shape the library's own SingleMLeader fixture builds - came back off by one bit and
			//failed to read at every version, not only before R2010.
			CadDocument doc = new CadDocument();
			doc.Header.Version = version;
			MultiLeader leader = new MultiLeader
			{
				PathType = MultiLeaderPathType.StraightLineSegments,
			};
			leader.ContextData.ContentBasePoint = new XYZ(1.86, 1.5, 0);
			MultiLeaderObjectContextData.LeaderRoot root = new MultiLeaderObjectContextData.LeaderRoot
			{
				ConnectionPoint = new XYZ(1.5, 1.5, 0),
				LandingDistance = 0.36,
			};
			MultiLeaderObjectContextData.LeaderLine line = new MultiLeaderObjectContextData.LeaderLine();
			line.Points.Add(XYZ.Zero);
			root.Lines.Add(line);
			leader.ContextData.LeaderRoots.Add(root);
			doc.ModelSpace.Entities.Add(leader);

			byte[] bytes;
			using (MemoryStream stream = new MemoryStream())
			using (DwgWriter writer = new DwgWriter(stream, doc))
			{
				writer.Write();
				bytes = stream.ToArray();
			}

			CadDocument reopened = DwgReader.Read(new MemoryStream(bytes));

			MultiLeader reopenedLeader = Assert.Single(reopened.ModelSpace.Entities.OfType<MultiLeader>());
			Assert.False(reopenedLeader.ContextData.HasTextContents);
			Assert.False(reopenedLeader.ContextData.HasContentsBlock);
			MultiLeaderObjectContextData.LeaderRoot reopenedRoot =
				Assert.Single(reopenedLeader.ContextData.LeaderRoots);
			Assert.Equal(1.5, reopenedRoot.ConnectionPoint.X, 6);
			Assert.Equal(0.36, reopenedRoot.LandingDistance, 6);
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
