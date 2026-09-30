using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.IO.DWG;
using ACadSharp.IO.DXF.DxfStreamWriter;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace ACadSharp.Tests.IO;

public class McadIntegrationTests
{
	[Theory]
	[InlineData("\\U+95E8", "门")]
	[InlineData("Ph\\U+1ED3ng", "Phồng")]
	[InlineData("\\U+GGGG", "\\U+GGGG")]
	public void UnicodeDecoderKeepsTheExistingDecoderOverload(string escaped, string expected)
	{
		Assert.NotNull(typeof(CadUtils).GetMethod(nameof(CadUtils.UnescapeUnicodeCharacters), new[] { typeof(string) }));
		Assert.Equal(expected, CadUtils.UnescapeUnicodeCharacters(escaped));
		Assert.Equal(expected, CadUtils.UnescapeUnicodeCharacters(escaped, false));
	}

	[Fact]
	public void DxfUnicodeDecodingDoesNotConsumeLegacyCodePageEscapes()
	{
		const string mixed = "\\M+5D6D0\\U+95E8";
		Assert.Equal("\\M+5D6D0门", CadUtils.UnescapeUnicodeCharacters(mixed, false));
		Assert.Equal("中门", CadUtils.UnescapeUnicodeCharacters(mixed));
	}

	[Theory]
	[InlineData(ACadVersion.AC1015)]
	[InlineData(ACadVersion.AC1032)]
	public void DxfKeepsUnicodeLayerNamesAndEntityReferences(ACadVersion version)
	{
		const string layerName = "Phòng ngủ 门";
		CadDocument document = new();
		document.Header.Version = version;
		Layer layer = new(layerName);
		document.Layers.Add(layer);
		document.Entities.Add(new Line(XYZ.Zero, new XYZ(1, 2, 0)) { Layer = layer });
		using MemoryStream bytes = new();
		DxfWriter.Write(bytes, document);
		byte[] payload = bytes.ToArray();
		if (version == ACadVersion.AC1015)
		{
			Assert.Contains("\\U+95E8", Encoding.ASCII.GetString(payload));
		}

		using MemoryStream input = new(payload);
		CadDocument restored = DxfReader.Read(input);
		Line line = Assert.Single(restored.Entities.OfType<Line>());
		Assert.Equal(layerName, line.Layer.Name);
		Assert.Same(restored.Layers[layerName], line.Layer);
	}

	[Theory]
	[InlineData(0.0000000716, 12, "0.0000000716")]
	[InlineData(123.456789, 3, "123.457")]
	[InlineData(-123.456789, 3, "-123.457")]
	public void AsciiPrecisionAvoidsScientificNotationAndUsesInvariantCulture(double value, int precision, string expected)
	{
		CultureInfo previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
			using MemoryStream bytes = new();
			using (DxfAsciiWriter writer = new(new StreamWriter(bytes, Encoding.ASCII, 1024, true)))
			{
				writer.DecimalPrecision = precision;
				writer.Write(10, value);
				writer.Flush();
			}

			using StringReader lines = new(Encoding.ASCII.GetString(bytes.ToArray()));
			Assert.Equal("10", lines.ReadLine().Trim());
			Assert.Equal(expected, lines.ReadLine());
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}

	[Fact]
	public void ReassigningSharedTableReferencesStillNotifiesAllOwners()
	{
		CadDocument document = new();
		Layer layer = new("shared");
		LineType lineType = new("shared");
		Material material = new("shared");
		document.Layers.Add(layer);
		document.LineTypes.Add(lineType);
		document.Materials.Add(material);
		Line first = new();
		Line second = new();
		document.Entities.Add(first);
		document.Entities.Add(second);
		foreach (Line line in new[] { first, second })
		{
			for (int i = 0; i < 4; i++)
			{
				line.Layer = layer;
				line.LineType = lineType;
				line.Material = material;
			}
		}

		document.Layers.Remove(layer.Name);
		document.LineTypes.Remove(lineType.Name);
		document.Materials.Remove(material.Name);
		foreach (Line line in new[] { first, second })
		{
			Assert.Same(document.Layers[Layer.DefaultName], line.Layer);
			Assert.Same(document.LineTypes[LineType.ByLayerName], line.LineType);
			Assert.Null(line.Material);
		}
	}

	[Fact]
	public void DetachedEntityRegistersReferencesInItsNewDocument()
	{
		CadDocument original = new();
		Layer originalLayer = new("shared");
		original.Layers.Add(originalLayer);
		Line line = new() { Layer = originalLayer };
		original.Entities.Add(line);
		original.Entities.Remove(line);
		CadDocument destination = new();
		Layer destinationLayer = new("shared");
		destination.Layers.Add(destinationLayer);
		destination.Entities.Add(line);
		original.Layers.Remove(originalLayer.Name);
		Assert.Same(destinationLayer, line.Layer);
		destination.Layers.Remove(destinationLayer.Name);
		Assert.Same(destination.Layers[Layer.DefaultName], line.Layer);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void AddedPolylineVerticesKeepOwnerProperties(bool registered)
	{
		Polyline3D owner = new();
		owner.Layer = new Layer("vertices");
		owner.LineType = new LineType("vertices");
		owner.Material = new Material("vertices");
		owner.Color = new Color(1);
		owner.LineTypeScale = 2.5;
		owner.IsInvisible = true;
		owner.Transparency = new Transparency(30);
		CadDocument document = new();
		if (registered) document.Entities.Add(owner);
		Vertex3D vertex = new(new XYZ(1, 2, 3));
		owner.Vertices.Add(vertex);
		Assert.Equal(owner.Layer.Name, vertex.Layer.Name);
		Assert.Equal(owner.LineType.Name, vertex.LineType.Name);
		Assert.Equal(owner.Material.Name, vertex.Material.Name);
		Assert.Equal(owner.Color, vertex.Color);
		Assert.Equal(owner.LineTypeScale, vertex.LineTypeScale);
		Assert.Equal(owner.IsInvisible, vertex.IsInvisible);
		Assert.Equal(owner.Transparency, vertex.Transparency);
		if (registered)
		{
			Assert.Same(owner.Layer, vertex.Layer);
			Assert.Same(owner.LineType, vertex.LineType);
			Assert.Same(owner.Material, vertex.Material);
			Assert.Same(document, vertex.Document);
		}
		else
		{
			Assert.NotSame(owner.Layer, vertex.Layer);
			Assert.NotSame(owner.Material, vertex.Material);
		}
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	[InlineData(5)]
	[InlineData(6)]
	[InlineData(7)]
	public void ScratchBuffersKeepInterleavedValuesAtEveryBitOffset(int offset)
	{
		using MemoryStream bytes = new();
		DwgStreamWriterBase writer = (DwgStreamWriterBase)DwgStreamWriterBase.GetStreamWriter(ACadVersion.AC1032, bytes, Encoding.ASCII);
		for (int i = 0; i < offset; i++) writer.WriteBit(true);
		for (int i = 0; i < 12; i++)
		{
			writer.WriteBitShort((short)(-1234 - i));
			writer.WriteBitLong(-987654321 + i);
			writer.WriteBitDouble(123.456 + i);
			writer.WriteRawLong(-7654321 + i);
			writer.WriteBytes(BitConverter.GetBytes(0xFEDCBA9876543210UL + (ulong)i));
		}
		writer.WriteByte(0);
		writer.WriteByte(0);
		bytes.Position = 0;
		IDwgStreamReader reader = DwgStreamReaderBase.GetStreamHandler(ACadVersion.AC1032, bytes);
		for (int i = 0; i < offset; i++) Assert.True(reader.ReadBit());
		for (int i = 0; i < 12; i++)
		{
			Assert.Equal((short)(-1234 - i), reader.ReadBitShort());
			Assert.Equal(-987654321 + i, reader.ReadBitLong());
			Assert.Equal(123.456 + i, reader.ReadBitDouble());
			Assert.Equal(-7654321 + i, reader.ReadRawLong());
			Assert.Equal(0xFEDCBA9876543210UL + (ulong)i, reader.ReadRawULong());
		}
	}
}
