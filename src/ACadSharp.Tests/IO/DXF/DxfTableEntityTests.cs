using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using ACadSharp.Tests.Common;
using CSMath;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO.DXF
{
	/// <summary>
	/// A table in DXF: the entity, its cells and their values.
	/// </summary>
	/// <remarks>
	/// Two behaviours have been pinned here in turn, and the first two were both losses. The writer
	/// once skipped the whole entity, so every table vanished from a DXF and its <c>*T</c> block sat
	/// in the file with nothing referencing it. Then it wrote the table as the <c>INSERT</c> it
	/// derives from: the drawn table survived and <b>every cell value, span and merge was dropped</b>
	/// with a notification - and the application writes DXF and builds tables in code. T92 writes
	/// the table itself, in the layout AutoCAD writes it.
	/// </remarks>
	public class DxfTableEntityTests
	{
		/// <summary>
		/// AutoCAD's own DXF, carrying two real tables - which makes it the oracle for this, with no
		/// AutoCAD run needed to produce it.
		/// </summary>
		private static string acadSample => Path.Combine(TestVariables.SamplesFolder, "sample_AC1032_ascii.dxf");

		[Fact]
		public void ATableBuiltInCodeSurvivesAsATable()
		{
			CadDocument doc = new CadDocument();
			BlockRecord block = new BlockRecord("*T1");
			block.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0)));
			doc.BlockRecords.Add(block);

			TableEntity table = new TableEntity(block)
			{
				InsertPoint = new XYZ(5, 7, 0),
			};
			doc.ModelSpace.Entities.Add(table);

			CadDocument result = writeAndRead(doc);

			TableEntity back = Assert.Single(result.ModelSpace.Entities.OfType<TableEntity>());
			Assert.Equal("*T1", back.Block.Name);
			Assert.Equal(5, back.InsertPoint.X);
			Assert.Equal(7, back.InsertPoint.Y);
		}

		[Fact]
		public void TableBlockContentSurvivesTheRoundTrip()
		{
			CadDocument doc = new CadDocument();
			BlockRecord block = new BlockRecord("*T2");
			block.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0)));
			block.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(0, 4, 0)));
			doc.BlockRecords.Add(block);
			doc.ModelSpace.Entities.Add(new TableEntity(block));

			CadDocument result = writeAndRead(doc);

			BlockRecord written = result.BlockRecords["*T2"];
			Assert.Equal(2, written.Entities.OfType<Line>().Count());
		}

		/// <summary>
		/// Every cell of AutoCAD's own two tables, through a DXF round trip: how many rows and
		/// columns, what each cell holds, and how far it is merged.
		/// </summary>
		/// <remarks>
		/// What this <b>cannot</b> see, and it was checked rather than assumed: claiming the table's
		/// cell-style override in group 93 without the values it promises. AutoCAD opens such a file
		/// with <b>AUDIT 0</b> and then silently drops every merge - its own re-export turns the 3x2
		/// title cell back into 1x1 and clears every merged flag - but this test stays <b>green</b>,
		/// because this reader ignores the override and hands back what it was given. Reading our own
		/// output back proves nothing about it (13 5g). The channel that does catch it is in
		/// run-checks: AutoCAD re-exports a file written here and the tables are compared against its
		/// own original.
		/// </remarks>
		[Fact]
		public void AutoCadsOwnTablesRoundTripCellForCell()
		{
			CadDocument doc = DxfReader.Read(acadSample);
			TableEntity[] before = doc.ModelSpace.Entities.OfType<TableEntity>().ToArray();
			Assert.Equal(2, before.Length);

			CadDocument result = writeAndRead(doc);
			TableEntity[] after = result.ModelSpace.Entities.OfType<TableEntity>().ToArray();
			Assert.Equal(before.Length, after.Length);

			for (int t = 0; t < before.Length; t++)
			{
				Assert.Equal(before[t].Rows.Count, after[t].Rows.Count);
				Assert.Equal(before[t].Columns.Count, after[t].Columns.Count);

				for (int r = 0; r < before[t].Rows.Count; r++)
				{
					TableEntity.Row wasRow = before[t].Rows[r];
					TableEntity.Row isRow = after[t].Rows[r];
					Assert.Equal(wasRow.Cells.Count, isRow.Cells.Count);

					for (int c = 0; c < wasRow.Cells.Count; c++)
					{
						TableEntity.Cell was = wasRow.Cells[c];
						TableEntity.Cell got = isRow.Cells[c];
						string where = $"table {t} cell [{r},{c}]";

						Assert.True(was.Type == got.Type, $"{where}: type {was.Type} became {got.Type}");
						Assert.True(was.MergedValue == got.MergedValue, $"{where}: merged {was.MergedValue} became {got.MergedValue}");
						Assert.True(was.BorderWidth == got.BorderWidth, $"{where}: span width {was.BorderWidth} became {got.BorderWidth}");
						Assert.True(was.BorderHeight == got.BorderHeight, $"{where}: span height {was.BorderHeight} became {got.BorderHeight}");

						//The block a block cell draws. Both readers used to read the handle and drop it -
						//an empty if body in Build - and both writers then wrote null in its place, so
						//AutoCAD read every block cell back as a text cell (T97).
						Assert.True(
							was.Content?.BlockRecord?.Name == got.Content?.BlockRecord?.Name,
							$"{where}: block \"{was.Content?.BlockRecord?.Name}\" became \"{got.Content?.BlockRecord?.Name}\"");

						object wasValue = was.Content?.CadValue?.Value;
						object gotValue = got.Content?.CadValue?.Value;
						Assert.True(
							Equals(wasValue?.ToString(), gotValue?.ToString()),
							$"{where}: value \"{wasValue}\" became \"{gotValue}\"");
					}
				}
			}
		}

		private static CadDocument writeAndRead(CadDocument doc)
		{
			byte[] bytes;
			using (MemoryStream stream = new MemoryStream())
			using (DxfWriter writer = new DxfWriter(stream, doc, false))
			{
				writer.Write();
				bytes = stream.ToArray();
			}

			return DxfReader.Read(new MemoryStream(bytes));
		}
	}
}
