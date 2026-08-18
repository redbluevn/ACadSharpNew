using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO;

/// <summary>
/// A drawing older than R2007 stores its text in a code page and not in Unicode. AutoCAD keeps the
/// characters that code page covers and writes the rest as \U+XXXX; encoding them straight into the
/// code page turns each one into a question mark, and AutoCAD then refuses the name and drops the
/// block that carries it.
/// </summary>
public class CodePageNamesTests
{
	//Vietnamese, and a Chinese name of the kind a downloaded block library brings with it. Neither
	//fits in the Windows-1252 code page a drawing normally declares.
	private const string _vietnamese = "Mặt bằng tầng điển hình";
	private const string _chinese = "设计师精选家具";

	[Theory]
	[InlineData(ACadVersion.AC1015)]
	[InlineData(ACadVersion.AC1018)]
	[InlineData(ACadVersion.AC1032)]
	public void BlockNamesOutsideTheCodePageSurviveARoundTrip(ACadVersion version)
	{
		CadDocument doc = new CadDocument(version);

		BlockRecord block = new BlockRecord(_vietnamese);
		block.Entities.Add(new Line(XYZ.Zero, new XYZ(10, 10, 0)));
		block.Entities.Add(new Circle { Center = XYZ.Zero, Radius = 5 });
		doc.BlockRecords.Add(block);

		BlockRecord second = new BlockRecord(_chinese);
		second.Entities.Add(new Line(XYZ.Zero, new XYZ(1, 1, 0)));
		doc.BlockRecords.Add(second);

		doc.Layers.Add(new Layer(_vietnamese));

		MemoryStream ms = new MemoryStream();
		DwgWriter.Write(ms, doc);
		using MemoryStream readStream = new MemoryStream(ms.ToArray());
		CadDocument rt = DwgReader.Read(readStream);

		Assert.True(rt.BlockRecords.TryGetValue(_vietnamese, out BlockRecord gotBlock), $"{_vietnamese} is missing after the round trip");
		Assert.Equal(2, gotBlock.Entities.Count);
		Assert.Single(gotBlock.Entities.OfType<Circle>());

		Assert.True(rt.BlockRecords.TryGetValue(_chinese, out BlockRecord gotSecond), $"{_chinese} is missing after the round trip");
		Assert.Single(gotSecond.Entities);

		Assert.True(rt.Layers.TryGetValue(_vietnamese, out Layer _), "the layer name is missing after the round trip");
	}

	[Fact]
	public void TextOutsideTheCodePageSurvivesARoundTrip()
	{
		CadDocument doc = new CadDocument(ACadVersion.AC1015);
		doc.Entities.Add(new MText { Value = "CÔNG TY CỔ PHẦN", InsertPoint = XYZ.Zero, Height = 2 });
		doc.Entities.Add(new TextEntity { Value = "Địa chỉ", InsertPoint = XYZ.Zero, Height = 2 });

		MemoryStream ms = new MemoryStream();
		DwgWriter.Write(ms, doc);
		using MemoryStream readStream = new MemoryStream(ms.ToArray());
		CadDocument rt = DwgReader.Read(readStream);

		Assert.Equal("CÔNG TY CỔ PHẦN", Assert.Single(rt.Entities.OfType<MText>()).Value);
		Assert.Equal("Địa chỉ", Assert.Single(rt.Entities.OfType<TextEntity>()).Value);
	}
}
