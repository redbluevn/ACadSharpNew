using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using CSMath;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace ACadSharp.Tests.IO.DXF
{
	/// <summary>
	/// Checks the group codes a written DXF record actually carries, by reading the file back the
	/// way the format defines it: as pairs of lines, the first the group code and the second the
	/// value. These are the cases where a wrong record made AutoCAD refuse the whole file.
	/// </summary>
	public class DxfEntityRecordTests
	{
		//Group 47 (pixel size) belongs to a derived boundary and only to one, whatever its value:
		//AutoCAD writes 47 = 0.0 for a derived path and nothing for a non-derived one, and discards
		//a drawing that breaks the rule either way (hatch C860 of a client drawing, and three
		//hatches of samples/sample_AC1032.dwg, respectively).
		[Fact]
		public void HatchWithoutDerivedPathDoesNotWriteCode47()
		{
			CadDocument doc = new CadDocument();
			Hatch hatch = createHatch();
			hatch.PixelSize = 0.05;
			doc.Entities.Add(hatch);

			List<(int, string)> record = this.recordOf(doc, "HATCH");

			Assert.DoesNotContain(record, p => p.Item1 == 47);
		}

		[Fact]
		public void HatchWithDerivedPathWritesCode47EvenWhenZero()
		{
			CadDocument doc = new CadDocument();
			Hatch hatch = createHatch();
			hatch.Paths[0].Flags |= BoundaryPathFlags.Derived;
			hatch.PixelSize = 0;
			doc.Entities.Add(hatch);

			List<(int, string)> record = this.recordOf(doc, "HATCH");

			(int, string) pixel = Assert.Single(record, p => p.Item1 == 47);
			Assert.Equal(0.0, double.Parse(pixel.Item2, System.Globalization.CultureInfo.InvariantCulture));
		}

		[Fact]
		public void HatchWithDerivedPathWritesItsPixelSize()
		{
			CadDocument doc = new CadDocument();
			Hatch hatch = createHatch();
			hatch.Paths[0].Flags |= BoundaryPathFlags.Derived;
			hatch.PixelSize = 0.05;
			doc.Entities.Add(hatch);

			List<(int, string)> record = this.recordOf(doc, "HATCH");

			(int, string) pixel = Assert.Single(record, p => p.Item1 == 47);
			Assert.Equal(0.05, double.Parse(pixel.Item2, System.Globalization.CultureInfo.InvariantCulture));
		}

		[Fact]
		public void LeaderWritesTheAnnotationHandle()
		{
			CadDocument doc = new CadDocument();
			MText annotation = new MText { Value = "annotation" };
			doc.Entities.Add(annotation);

			Leader leader = new Leader();
			leader.Vertices.Add(new XYZ(0, 0, 0));
			leader.Vertices.Add(new XYZ(10, 10, 0));
			leader.AssociatedAnnotation = annotation;
			doc.Entities.Add(leader);

			List<(int, string)> record = this.recordOf(doc, "LEADER");

			(int, string) pair = Assert.Single(record, p => p.Item1 == 340);
			Assert.Equal(annotation.Handle.ToString("X"), pair.Item2.Trim());
		}

		[Fact]
		public void ColourWordsOfAnMLeaderStyleCarryTheMethodByte()
		{
			//Group codes 90 to 94 of MULTILEADER and MLEADERSTYLE hold a whole colour in one word:
			//the method in the high byte, the colour in the three low ones. AutoCAD writes 0xC0 for
			//ByLayer, 0xC1 for ByBlock, 0xC2 for a true colour and 0xC3 for an index. Writing 0xC1
			//for everything that was not a true colour called all of them "by block", and the index
			//went through a byte, so ByLayer (256) came out as 0.
			CadDocument doc = new CadDocument();
			MultiLeaderStyle style = doc.MLeaderStyles["Standard"];
			style.LineColor = new Color(5);
			style.TextColor = Color.ByLayer;
			style.BlockContentColor = Color.ByBlock;

			List<(int, string)> record = this.recordOf(doc, "MLEADERSTYLE");

			Assert.Equal(unchecked((int)0xC3000005), word(record, 91));
			Assert.Equal(unchecked((int)0xC0000000), word(record, 93));
			Assert.Equal(unchecked((int)0xC1000000), word(record, 94));
		}

		[Fact]
		public void DxfRoundTripKeepsTheColoursOfAnMLeaderStyle()
		{
			CadDocument doc = new CadDocument();
			MultiLeaderStyle style = doc.MLeaderStyles["Standard"];
			style.LineColor = new Color(5);
			style.TextColor = Color.ByLayer;
			style.BlockContentColor = Color.ByBlock;

			MemoryStream stream = new MemoryStream();
			using (DxfWriter writer = new DxfWriter(stream, doc, false))
			{
				writer.Write();
			}

			using MemoryStream readStream = new MemoryStream(stream.ToArray());
			MultiLeaderStyle got = DxfReader.Read(readStream).MLeaderStyles["Standard"];

			Assert.Equal(new Color(5), got.LineColor);
			Assert.Equal(Color.ByLayer, got.TextColor);
			Assert.Equal(Color.ByBlock, got.BlockContentColor);
		}

		private static int word(List<(int, string)> record, int code)
		{
			(int, string) pair = Assert.Single(record, p => p.Item1 == code);
			return int.Parse(pair.Item2.Trim(), System.Globalization.CultureInfo.InvariantCulture);
		}

		private static Hatch createHatch()
		{
			Hatch hatch = new Hatch();
			hatch.IsSolid = false;
			hatch.SeedPoints.Add(new XY());

			Hatch.BoundaryPath.Polyline polyline = new Hatch.BoundaryPath.Polyline();
			polyline.IsClosed = true;
			polyline.Vertices.Add(new XYZ(0, 0, 0));
			polyline.Vertices.Add(new XYZ(10, 0, 0));
			polyline.Vertices.Add(new XYZ(10, 10, 0));

			Hatch.BoundaryPath path = new Hatch.BoundaryPath();
			path.Edges.Add(polyline);
			hatch.Paths.Add(path);

			return hatch;
		}

		/// <summary>
		/// Writes the document to an ASCII DXF and returns the (code, value) pairs of the first
		/// record of the given type.
		/// </summary>
		private List<(int, string)> recordOf(CadDocument doc, string type)
		{
			string text;
			using (MemoryStream stream = new MemoryStream())
			{
				using (DxfWriter writer = new DxfWriter(stream, doc, false))
				{
					writer.Write();
				}

				text = Encoding.UTF8.GetString(stream.ToArray());
			}

			string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
			List<(int, string)> pairs = new List<(int, string)>();
			bool inside = false;
			for (int i = 0; i + 1 < lines.Length; i += 2)
			{
				if (!int.TryParse(lines[i].Trim(), out int code))
				{
					continue;
				}

				string value = lines[i + 1];
				if (code == 0)
				{
					if (inside)
					{
						return pairs;
					}

					inside = value.Trim() == type;
					continue;
				}

				if (inside)
				{
					pairs.Add((code, value));
				}
			}

			Assert.True(inside, $"no {type} record was written");
			return pairs;
		}
	}
}
