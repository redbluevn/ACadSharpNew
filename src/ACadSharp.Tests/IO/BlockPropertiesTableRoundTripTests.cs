using ACadSharp.IO;
using ACadSharp.Objects.Evaluations;
using ACadSharp.Tables;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO;

/// <summary>
/// AutoCAD 2027 authored the fixture through BEDIT/BTABLE. The user entered rows 10 and 20,
/// cleared "Block properties must match a row in the table", accepted AutoCAD's **Last** default,
/// and saved the DWG and DXF. Source hashes are E37DC82B...B8BD and E4BDFDF9...BB60.
/// </summary>
public class BlockPropertiesTableRoundTripTests
{
	private static string dwgSample => Path.Combine(TestVariables.SamplesFolder, "dynamic-blocks", "BLOCKPROPERTIESTABLE.dwg");

	private static string dxfSample => Path.Combine(TestVariables.SamplesFolder, "dynamic-blocks", "BLOCKPROPERTIESTABLE.dxf");

	[Fact]
	public void AutoCadOracleIsDecodedFromDwg()
	{
		this.assertOracle(DwgReader.Read(dwgSample));
	}

	[Fact]
	public void AutoCadOracleIsDecodedFromDxf()
	{
		this.assertOracle(DxfReader.Read(dxfSample));
	}

	[Fact]
	public void AutoCadOracleSurvivesDwgRoundTrip()
	{
		CadDocument source = DwgReader.Read(dwgSample);
		using MemoryStream stream = new();
		DwgWriter.Write(stream, source);
		this.assertOracle(DwgReader.Read(new MemoryStream(stream.ToArray())));
	}

	[Fact]
	public void AutoCadOracleSurvivesDxfRoundTrip()
	{
		CadDocument source = DwgReader.Read(dwgSample);
		using MemoryStream stream = new();
		DxfWriter.Write(stream, source, false);
		string text = System.Text.Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n");
		Assert.DoesNotContain("100\nAcDbBlockPropertiesTableGrip\n", text);
		this.assertOracle(DxfReader.Read(new MemoryStream(stream.ToArray())));
	}

	[Fact]
	public void AnUnprovedCellVariantFailsClosed()
	{
		CadDocument source = DwgReader.Read(dwgSample);
		BlockPropertiesTable table = this.findTable(source);
		table.Rows[0].Values[0].Code = 70;

		using MemoryStream dxf = new();
		Assert.Throws<InvalidOperationException>(() => DxfWriter.Write(dxf, source, false));

		using MemoryStream dwg = new();
		Assert.Throws<InvalidOperationException>(() => DwgWriter.Write(dwg, source));
	}

	private void assertOracle(CadDocument document)
	{
		BlockRecord block = document.BlockRecords["MAUTOCAD_PROPERTIES_TABLE"];
		EvaluationGraph graph = Assert.IsType<EvaluationGraph>(block.EvaluationGraph);
		EvaluationGraph.Node[] nodes = graph.Nodes.OrderBy(node => node.Index).ToArray();
		Assert.Equal(6, nodes.Length);
		Assert.Collection(nodes,
			node => Assert.IsType<BlockLinearParameter>(node.Expression),
			node => Assert.IsType<BlockPropertiesTable>(node.Expression),
			node => Assert.IsType<BlockPropertiesTableGrip>(node.Expression),
			node => Assert.IsType<BlockGripLocationComponent>(node.Expression),
			node => Assert.IsType<BlockGripLocationComponent>(node.Expression),
			node => Assert.IsType<DynamicBlockProxyNode>(node.Expression));

		BlockLinearParameter linear = Assert.IsType<BlockLinearParameter>(nodes[0].Expression);
		BlockPropertiesTable table = Assert.IsType<BlockPropertiesTable>(nodes[1].Expression);
		Assert.Contains(table, linear.Reactors);
		Assert.Equal(2, table.Version);
		Assert.Equal("Block Table1", table.Label);
		Assert.Equal(3, table.GripId);
		Assert.True(table.ShowProperties);
		Assert.False(table.ChainActions);
		Assert.False(table.MustMatch);
		Assert.Equal(-9999, table.Value170A);
		Assert.Equal(-9999, table.Value170B);
		Assert.False(table.Value290);
		Assert.True(table.Value291);
		Assert.True(table.Value292);
		Assert.False(table.Value293);
		Assert.False(table.Value294);
		Assert.True(table.FinalValue291);
		Assert.False(table.FinalValue292);
		Assert.Equal(string.Empty, table.UnmatchedValue);

		BlockPropertiesTable.Column column = Assert.Single(table.Columns);
		Assert.Same(linear, column.Parameter);
		Assert.Equal(0, column.PropertyIndex);
		Assert.Equal(-1, column.Value171);
		Assert.Equal(string.Empty, column.UnmatchedValue);
		Assert.Equal("UpdatedDistance", column.ConnectionName);

		Assert.Collection(table.Rows,
			row => this.assertRow(row, 0, 10.0),
			row => this.assertRow(row, 1, 20.0));

		Assert.Equal(5, graph.Edges.Count);
		Assert.Equal(new[] { (2, 1, 0), (1, 3, 0), (1, 4, 0), (1, 0, 4), (0, 1, 4) },
			graph.Edges.Select(edge => (edge.FromNodeIndex, edge.ToNodeIndex, edge.Flags)).ToArray());
	}

	private BlockPropertiesTable findTable(CadDocument document) =>
		document.BlockRecords["MAUTOCAD_PROPERTIES_TABLE"].EvaluationGraph.Nodes
			.Select(node => node.Expression).OfType<BlockPropertiesTable>().Single();

	private void assertRow(BlockPropertiesTable.Row row, int index, double number)
	{
		Assert.Equal(index, row.Index);
		BlockPropertiesTable.Value value = Assert.Single(row.Values);
		Assert.Equal(40, value.Code);
		Assert.Equal(number, value.Number);
	}
}
