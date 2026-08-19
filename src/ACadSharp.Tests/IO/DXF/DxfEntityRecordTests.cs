using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using System;
using System.Collections.Generic;
using System.IO;
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
