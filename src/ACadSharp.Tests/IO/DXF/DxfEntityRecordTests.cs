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
		[Fact]
		public void HatchWithoutPixelSizeDoesNotWriteCode47()
		{
			CadDocument doc = new CadDocument();
			Hatch hatch = createHatch();
			hatch.PixelSize = 0;
			doc.Entities.Add(hatch);

			List<(int, string)> record = this.recordOf(doc, "HATCH");

			Assert.DoesNotContain(record, p => p.Item1 == 47);
		}

		[Fact]
		public void HatchWithPixelSizeWritesCode47()
		{
			CadDocument doc = new CadDocument();
			Hatch hatch = createHatch();
			hatch.PixelSize = 0.05;
			doc.Entities.Add(hatch);

			List<(int, string)> record = this.recordOf(doc, "HATCH");

			Assert.Contains(record, p => p.Item1 == 47);
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
