using ACadSharp.Blocks;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO;

public class AnonymousBlockNameTests
{
	public static TheoryData<ACadVersion> Versions => new TheoryData<ACadVersion>
	{
		ACadVersion.AC1015,
		ACadVersion.AC1018,
		ACadVersion.AC1024,
		ACadVersion.AC1032,
	};

	[Theory]
	[MemberData(nameof(Versions))]
	public void ABlockWithARealNameKeepsIt(ACadVersion version)
	{
		//A drawing can carry the anonymous flag on a block that has a real name - the xref bound
		//blocks of a production drawing do. The DWG writes only the first two characters of an
		//anonymous name, so such a block used to end up called "Xr" and AutoCAD refused the whole
		//file. AutoCAD itself drops the flag for those and keeps the name.
		CadDocument doc = new CadDocument();
		doc.Header.Version = version;

		BlockRecord named = new BlockRecord("Xr_plan$0$marks") { Flags = BlockTypeFlags.Anonymous };
		named.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(1, 1, 0)));
		doc.BlockRecords.Add(named);

		BlockRecord anonymous = new BlockRecord("*U5") { Flags = BlockTypeFlags.Anonymous };
		anonymous.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(2, 0, 0)));
		doc.BlockRecords.Add(anonymous);

		MemoryStream ms = new MemoryStream();
		using (DwgWriter writer = new DwgWriter(ms, doc))
		{
			writer.Write();
		}

		CadDocument rt = DwgReader.Read(new MemoryStream(ms.ToArray()));

		BlockRecord got = Assert.Single(rt.BlockRecords, b => b.Name == "Xr_plan$0$marks");
		Assert.False(got.IsAnonymous);
		Assert.Single(got.Entities);

		Assert.Contains(rt.BlockRecords, b => b.Name.StartsWith("*U") && b.IsAnonymous);
	}
}
