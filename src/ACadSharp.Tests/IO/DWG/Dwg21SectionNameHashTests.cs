using ACadSharp.IO.DWG;
using ACadSharp.IO.DWG.DwgStreamWriters;
using ACadSharp.IO.DWG.FileHeaders;
using Xunit;

namespace ACadSharp.Tests.IO.DWG
{
	/// <summary>
	/// The 32-bit hash an R2007 section map carries per section name is not in the specification.
	/// Measured on real files: it is the page-checksum summing routine over the name as UTF-16 with
	/// its terminator, started at (sum1 = character count, sum2 = 0).  These are the fourteen names
	/// a real AutoCAD R2007 file uses, with the values those files carry.
	/// </summary>
	public class Dwg21SectionNameHashTests
	{
		[Theory]
		[InlineData("AcDb:XrefManifest", 0x7ae40662u)]
		[InlineData("AcDb:AppInfoHistory", 0x96de0737u)]
		[InlineData("AcDb:AppInfo", 0x3fa0043eu)]
		[InlineData("AcDb:Preview", 0x40aa0473u)]
		[InlineData("AcDb:SummaryInfo", 0x717a060fu)]
		[InlineData("AcDb:RevHistory", 0x60a205b3u)]
		[InlineData("AcDb:AcDbObjects", 0x674c05a9u)]
		[InlineData("AcDb:ObjFreeSpace", 0x77e2061fu)]
		[InlineData("AcDb:Template", 0x4a1404ceu)]
		[InlineData("AcDb:Handles", 0x3f6e0450u)]
		[InlineData("AcDb:Classes", 0x3f54045fu)]
		[InlineData("AcDb:AuxHeader", 0x54f0050au)]
		[InlineData("AcDb:Header", 0x32b803d9u)]
		[InlineData("AcDb:FileDepList", 0x6c4205cau)]
		public void ReproducesTheHashARealFileCarries(string name, uint expected)
		{
			Assert.Equal(expected, Dwg21Checksum.GetSectionNameHash(name));
		}
	}
}
