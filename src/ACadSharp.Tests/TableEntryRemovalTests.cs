using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests;

public class TableEntryRemovalTests
{
	[Fact]
	public void RemovingALayerSendsTheEntitiesBackToTheDefault()
	{
		CadDocument doc = new CadDocument();
		Layer layer = new Layer("to_remove");
		doc.Layers.Add(layer);

		Line line = new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0)) { Layer = layer };
		doc.ModelSpace.Entities.Add(line);
		Assert.Same(layer, line.Layer);

		doc.Layers.Remove(layer.Name);

		Assert.Equal(Layer.DefaultName, line.Layer.Name);
		Assert.Same(doc.Layers[Layer.DefaultName], line.Layer);
	}

	[Fact]
	public void RemovingALineTypeSendsTheEntitiesBackToByLayer()
	{
		CadDocument doc = new CadDocument();
		LineType lineType = new LineType("to_remove");
		doc.LineTypes.Add(lineType);

		Line line = new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0)) { LineType = lineType };
		doc.ModelSpace.Entities.Add(line);
		Assert.Same(lineType, line.LineType);

		doc.LineTypes.Remove(lineType.Name);

		Assert.Equal(LineType.ByLayerName, line.LineType.Name);
	}

	[Fact]
	public void RemovingAMaterialClearsItOnTheEntities()
	{
		//A Material is a NonGraphicalObject and not a table entry, which is the case a type filter
		//on the notification is most likely to miss.
		CadDocument doc = new CadDocument();
		Material material = new Material("to_remove");
		doc.Materials.Add(material);

		Line line = new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0));
		doc.ModelSpace.Entities.Add(line);
		line.Material = material;
		Assert.Same(material, line.Material);

		doc.Materials.Remove(material.Name);

		Assert.Null(line.Material);
	}

	[Fact]
	public void AnEntityInABlockIsToldToo()
	{
		CadDocument doc = new CadDocument();
		Layer layer = new Layer("to_remove");
		doc.Layers.Add(layer);

		BlockRecord block = new BlockRecord("a_block");
		doc.BlockRecords.Add(block);
		Line line = new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0)) { Layer = layer };
		block.Entities.Add(line);

		doc.Layers.Remove(layer.Name);

		Assert.Equal(Layer.DefaultName, line.Layer.Name);
	}
}
