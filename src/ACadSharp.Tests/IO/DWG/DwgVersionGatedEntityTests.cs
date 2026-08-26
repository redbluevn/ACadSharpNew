using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Tables;
using ACadSharp.Tests.Common;
using CSMath;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO.DWG
{
	/// <summary>
	/// A table used to be dropped at every version below R2010, because only the R2010 layout of it
	/// was implemented. It is now written in the inline cell layout as well, so R2000 and R2004 keep
	/// it - AutoCAD opens both with 0 errors. R2007 is the exception and says so: its text cells are
	/// not in that layout, and a file written as if they were does not open.
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

		/// <summary>
		/// A table is written and read back at R2000 and R2004, in the inline cell layout those
		/// versions use.
		/// </summary>
		/// <remarks>
		/// It used to be dropped at every version below R2010, and this test used to assert the
		/// message saying so. AutoCAD 2027 on the upstream sample carried down to both: the file
		/// opens, AUDIT reports 0, and every cell of both its tables comes back - so the drop was a
		/// limit of this writer, not of those versions.
		/// </remarks>
		[Theory]
		[InlineData(ACadVersion.AC1015)]
		[InlineData(ACadVersion.AC1018)]
		public void TableIsWrittenAndReadBackBeforeR2010(ACadVersion version)
		{
			List<string> messages = write(version, out CadDocument reopened);

			Assert.DoesNotContain(messages, m => m.Contains("TableEntity", System.StringComparison.Ordinal));
			Assert.Single(reopened.ModelSpace.Entities.OfType<TableEntity>());
		}

		/// <summary>
		/// A real table - AutoCAD's own, with its text, its numbers and its merges - carried down to
		/// R2000 and R2004 and read back cell for cell.
		/// </summary>
		/// <remarks>
		/// The fixture above is a table built in code with no rows at all, so it only shows that the
		/// entity survives. This is the one that shows the CONTENT does. The inline layout has one
		/// string per cell and no typed values, so a number or a date goes in as the text the cell
		/// draws - the same thing AutoCAD writes there - and that is what is compared.
		/// </remarks>
		[Theory]
		[InlineData(ACadVersion.AC1015)]
		[InlineData(ACadVersion.AC1018)]
		public void ARealTableKeepsItsCellsBeforeR2010(ACadVersion version)
		{
			CadDocument doc = DwgReader.Read(Path.Combine(TestVariables.SamplesFolder, "sample_AC1032.dwg"));
			TableEntity[] before = doc.ModelSpace.Entities.OfType<TableEntity>().ToArray();
			Assert.Equal(2, before.Length);

			doc.Header.Version = version;
			byte[] bytes;
			using (MemoryStream stream = new MemoryStream())
			{
				DwgWriter.Write(stream, doc, new DwgWriterConfiguration { CloseStream = false }, null);
				bytes = stream.ToArray();
			}

			TableEntity[] after = DwgReader.Read(new MemoryStream(bytes))
				.ModelSpace.Entities.OfType<TableEntity>().ToArray();
			Assert.Equal(before.Length, after.Length);

			for (int t = 0; t < before.Length; t++)
			{
				Assert.Equal(before[t].Rows.Count, after[t].Rows.Count);
				Assert.Equal(before[t].Columns.Count, after[t].Columns.Count);

				for (int r = 0; r < before[t].Rows.Count; r++)
				{
					for (int c = 0; c < before[t].Rows[r].Cells.Count; c++)
					{
						TableEntity.Cell was = before[t].Rows[r].Cells[c];
						TableEntity.Cell got = after[t].Rows[r].Cells[c];
						string where = $"table {t} cell [{r},{c}] at {version}";

						object value = was.Content?.CadValue?.Value;
						string drawn = was.Content?.CadValue?.FormattedValue;
						string expected = value as string
							?? (string.IsNullOrEmpty(drawn) ? value?.ToString() : drawn)
							?? string.Empty;
						string actual = got.Content?.CadValue?.Value?.ToString() ?? string.Empty;

						Assert.True(expected == actual, $"{where}: \"{expected}\" became \"{actual}\"");
					}
				}
			}
		}

		/// <summary>
		/// R2007 is the one version below R2010 that still drops it, and says so precisely.
		/// </summary>
		/// <remarks>
		/// The reader's own inline-text branch is conditioned on the version being BELOW AC1021, so
		/// R2007 keeps a text cell's string somewhere the inline layout does not describe. Written
		/// as if it did, AutoCAD does not open the file at all - measured, which is why the writer
		/// stops at that version rather than guessing.
		/// </remarks>
		[Fact]
		public void TableAtR2007SaysWhichVersionsKeepIt()
		{
			List<string> messages = write(ACadVersion.AC1021, out _);

			Assert.Contains(messages, m =>
				m.Contains("TableEntity", System.StringComparison.Ordinal) &&
				m.Contains(ACadVersion.AC1021.ToString(), System.StringComparison.Ordinal) &&
				m.Contains(ACadVersion.AC1015.ToString(), System.StringComparison.Ordinal));
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
