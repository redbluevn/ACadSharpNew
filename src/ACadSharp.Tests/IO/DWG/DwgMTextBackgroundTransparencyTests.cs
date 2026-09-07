using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO.DWG;

public class DwgMTextBackgroundTransparencyTests
{
	[Theory]
	[InlineData((short)0)]
	[InlineData((short)30)]
	[InlineData((short)90)]
	public void DwgRoundTripKeepsTheBackgroundTransparency(short percent)
	{
		//The DWG field is an alpha value - low byte alpha, 0x02 in the top byte for "by value" -
		//and not the 0-90 percentage the model holds. It used to be cast straight to a short, so
		//AutoCAD's own numbers looked like nonsense and were thrown away with a warning: about
		//2,500 of them across a folder of workshop drawings, 705 distinct values in one file.
		//
		//AutoCAD 2027 settled the encoding rather than the spec: a drawing written here with 30%
		//was exported back to DXF by AutoCAD, and group 441 came out as 33554610, which is exactly
		//Transparency.ToAlphaValue(30).
		CadDocument doc = new CadDocument();
		MText text = new MText
		{
			Value = "BG",
			InsertPoint = XYZ.Zero,
			Height = 10,
			BackgroundFillFlags = BackgroundFillFlags.UseBackgroundFillColor,
			BackgroundScale = 1.5,
			BackgroundColor = new Color(1),
			BackgroundTransparency = new Transparency(percent),
		};
		doc.Entities.Add(text);

		string file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dwg");
		try
		{
			using (DwgWriter writer = new DwgWriter(file, doc))
			{
				writer.Write();
			}

			CadDocument read = DwgReader.Read(file);
			MText back = read.Entities.OfType<MText>().Single();

			Assert.Equal(percent, back.BackgroundTransparency.Value);
		}
		finally
		{
			if (File.Exists(file))
			{
				File.Delete(file);
			}
		}
	}

	[Fact]
	public void TheAlphaEncodingIsTheOneAutoCadWrites()
	{
		//Pinned because the reader and the writer both depend on it, and because it is the number
		//AutoCAD produced from a file written here: 30% -> 33554610 -> 30%.
		Assert.Equal(33554610, Transparency.ToAlphaValue(new Transparency(30)));
		Assert.Equal(30, Transparency.FromAlphaValue(33554610).Value);
	}
}
