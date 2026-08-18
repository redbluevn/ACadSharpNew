using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO.DXF;

public class DxfInsertAttributeTests
{
	[Fact]
	public void DxfRoundTripKeepsTheAttributesOfAnInsert()
	{
		//An INSERT carries its ATTRIBs inline, closed by a SEQEND. The reader used to hand each of
		//them back as a loose entity of the block, so the insert came back with no attributes at all
		//and model space gained orphan ATTRIBs.
		CadDocument doc = new CadDocument();

		BlockRecord block = new BlockRecord("attributed_block");
		block.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0)));
		AttributeDefinition attdef = new AttributeDefinition
		{
			Tag = "TAG1",
			Value = "definition",
			InsertPoint = new XYZ(0, 0, 0),
		};
		block.Entities.Add(attdef);
		doc.BlockRecords.Add(block);

		Insert insert = new Insert(block);
		insert.Attributes.Add(new AttributeEntity(attdef) { Value = "instance" });
		doc.ModelSpace.Entities.Add(insert);

		MemoryStream ms = new MemoryStream();
		using (DxfWriter writer = new DxfWriter(ms, doc, false))
		{
			writer.Write();
		}

		using MemoryStream readStream = new MemoryStream(ms.ToArray());
		CadDocument rt = DxfReader.Read(readStream);

		Insert got = Assert.Single(rt.ModelSpace.Entities.OfType<Insert>());
		Assert.Empty(rt.ModelSpace.Entities.OfType<AttributeEntity>());

		Assert.Equal(2, got.Attributes.Count);
		Assert.All(got.Attributes, a => Assert.Equal("TAG1", a.Tag));
		Assert.Contains(got.Attributes, a => a.Value == "instance");
		Assert.All(got.Attributes, a => Assert.Same(got, a.Owner));
		Assert.NotNull(got.Attributes.Seqend);
	}

	[Theory]
	[InlineData("sample_AC1009_ascii.dxf")]
	[InlineData("sample_AC1015_ascii.dxf")]
	[InlineData("sample_AC1018_ascii.dxf")]
	[InlineData("sample_AC1032_ascii.dxf")]
	public void AttributesBelongToTheirInsert(string name)
	{
		//The same drawing saved by AutoCAD in several versions. A DXF older than R13 carries no
		//handles, so the attributes can only be linked by their position in the stream; the reader
		//used to rely on the owner handle alone and gave those back as loose attributes.
		string path = Path.Combine(TestVariables.SamplesFolder, name);
		CadDocument doc = DxfReader.Read(path);

		int withAttributes = 0;
		int attributes = 0;
		int seqends = 0;
		int loose = 0;

		foreach (BlockRecord block in doc.BlockRecords)
		{
			foreach (Entity entity in block.Entities)
			{
				if (entity is Insert insert && insert.HasAttributes)
				{
					withAttributes++;
					attributes += insert.Attributes.Count;
					if (insert.Attributes.Seqend != null)
					{
						seqends++;
					}
				}

				if (entity is AttributeEntity)
				{
					loose++;
				}
			}
		}

		Assert.Equal(2, withAttributes);
		Assert.Equal(4, attributes);
		Assert.Equal(2, seqends);
		Assert.Equal(0, loose);
	}
}
