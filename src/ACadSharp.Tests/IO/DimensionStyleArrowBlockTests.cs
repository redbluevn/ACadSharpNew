using ACadSharp.Blocks;
using ACadSharp.IO;
using ACadSharp.Tables;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO;

/// <summary>
/// DIMBLK, the arrowhead of a style whose two ends match (DIMSAH off), was read from DWG and DXF but never
/// assigned, so every such style came back with the default closed filled arrow; group 5, DIMBLK's
/// obsolete name, went to the leader arrow.
/// </summary>
public class DimensionStyleArrowBlockTests
{
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void TheStyleArrowBlockSurvivesAWriteAndARead(bool dwg)
	{
		CadDocument document = new();
		BlockRecord tick = new("_ArchTick");
		tick.Entities.Add(new ACadSharp.Entities.Line(new CSMath.XYZ(-0.5, -0.5, 0), new CSMath.XYZ(0.5, 0.5, 0)));
		document.BlockRecords.Add(tick);
		DimensionStyle style = new("ARCH") { SeparateArrowBlocks = false };
		document.DimensionStyles.Add(style);
		style.ArrowBlock = tick;

		using MemoryStream stream = new();
		if (dwg)
		{
			DwgWriter.Write(stream, document);
		}
		else
		{
			DxfWriter.Write(stream, document);
		}

		using MemoryStream written = new(stream.ToArray());
		CadDocument back = dwg ? DwgReader.Read(written) : DxfReader.Read(written);
		DimensionStyle read = back.DimensionStyles.First(item => item.Name == "ARCH");
		Assert.False(read.SeparateArrowBlocks);
		Assert.Equal("_ArchTick", read.ArrowBlock?.Name);
		Assert.Null(read.LeaderArrow);
	}
}
