using ACadSharp.IO;
using ACadSharp.Objects;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO.DXF;

public class DxfMLineStyleTests
{
	[Fact]
	public void DxfRoundTripKeepsTheColourOfEachElement()
	{
		//Group code 62 appears once for the style and then once per element. Reading every one of
		//them into FillColor left the elements at their default and put the last element's colour
		//into the style.
		CadDocument doc = new CadDocument();

		MLineStyle style = new MLineStyle("moredwg_mline");
		style.FillColor = new Color(3);
		style.AddElement(new MLineStyle.Element { Offset = 0.5, Color = new Color(1) });
		style.AddElement(new MLineStyle.Element { Offset = -0.5, Color = new Color(5) });
		doc.MLineStyles.Add(style);

		MemoryStream ms = new MemoryStream();
		using (DxfWriter writer = new DxfWriter(ms, doc, false))
		{
			writer.Write();
		}

		using MemoryStream readStream = new MemoryStream(ms.ToArray());
		MLineStyle got = DxfReader.Read(readStream).MLineStyles["moredwg_mline"];

		Assert.Equal(new Color(3), got.FillColor);
		Assert.Equal(2, got.Elements.Count());
		Assert.Equal(new Color(1), got.Elements.First().Color);
		Assert.Equal(new Color(5), got.Elements.Last().Color);
	}
}
