using ACadSharp.Blocks;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO;

/// <summary>
/// One minimal drawing per 2D entity, written and read back, checking that the values that define
/// the entity survive. The existing single-object tests write every case and read it back without
/// looking at what came out; these compare the entity before and after, which is what catches a
/// field written in the wrong place or dropped.
/// </summary>
public class Entity2DRoundTripTests
{
	/// <summary>
	/// The 2D entities the project has to keep intact, one builder each. The builder adds the
	/// entity to the document and returns the check to run on the entity that comes back.
	/// </summary>
	private static readonly Dictionary<string, Func<CadDocument, Action<CadDocument>>> _cases = new()
	{
		["Line"] = doc =>
		{
			Line line = new Line(new XYZ(1, 2, 0), new XYZ(30, 40, 0));
			doc.Entities.Add(line);
			return rt =>
			{
				Line got = single<Line>(rt);
				assertEqual(line.StartPoint, got.StartPoint);
				assertEqual(line.EndPoint, got.EndPoint);
			};
		},
		["Point"] = doc =>
		{
			Point point = new Point(new XYZ(5, -7, 0));
			doc.Entities.Add(point);
			return rt => assertEqual(point.Location, single<Point>(rt).Location);
		},
		["Circle"] = doc =>
		{
			Circle circle = new Circle { Center = new XYZ(3, 4, 0), Radius = 12.5 };
			doc.Entities.Add(circle);
			return rt =>
			{
				Circle got = single<Circle>(rt);
				assertEqual(circle.Center, got.Center);
				Assert.Equal(circle.Radius, got.Radius, 9);
			};
		},
		["Arc"] = doc =>
		{
			Arc arc = new Arc
			{
				Center = new XYZ(2, 2, 0),
				Radius = 7,
				StartAngle = 0.25,
				EndAngle = 2.5,
			};
			doc.Entities.Add(arc);
			return rt =>
			{
				Arc got = single<Arc>(rt);
				assertEqual(arc.Center, got.Center);
				Assert.Equal(arc.Radius, got.Radius, 9);
				Assert.Equal(arc.StartAngle, got.StartAngle, 9);
				Assert.Equal(arc.EndAngle, got.EndAngle, 9);
			};
		},
		["Ellipse"] = doc =>
		{
			Ellipse ellipse = new Ellipse
			{
				Center = new XYZ(1, 1, 0),
				MajorAxisEndPoint = new XYZ(10, 0, 0),
				RadiusRatio = 0.4,
				StartParameter = 0,
				EndParameter = Math.PI,
			};
			doc.Entities.Add(ellipse);
			return rt =>
			{
				Ellipse got = single<Ellipse>(rt);
				assertEqual(ellipse.Center, got.Center);
				assertEqual(ellipse.MajorAxisEndPoint, got.MajorAxisEndPoint);
				Assert.Equal(ellipse.RadiusRatio, got.RadiusRatio, 9);
				Assert.Equal(ellipse.StartParameter, got.StartParameter, 9);
				Assert.Equal(ellipse.EndParameter, got.EndParameter, 9);
			};
		},
		["LwPolyline"] = doc =>
		{
			LwPolyline pline = new LwPolyline { IsClosed = true };
			pline.Vertices.Add(new LwPolyline.Vertex(new XY(0, 0)));
			pline.Vertices.Add(new LwPolyline.Vertex(new XY(10, 0)) { Bulge = 0.5 });
			pline.Vertices.Add(new LwPolyline.Vertex(new XY(10, 10)));
			doc.Entities.Add(pline);
			return rt =>
			{
				LwPolyline got = single<LwPolyline>(rt);
				Assert.Equal(pline.IsClosed, got.IsClosed);
				Assert.Equal(pline.Vertices.Count, got.Vertices.Count);
				for (int i = 0; i < pline.Vertices.Count; i++)
				{
					Assert.Equal(pline.Vertices[i].Location.X, got.Vertices[i].Location.X, 9);
					Assert.Equal(pline.Vertices[i].Location.Y, got.Vertices[i].Location.Y, 9);
					Assert.Equal(pline.Vertices[i].Bulge, got.Vertices[i].Bulge, 9);
				}
			};
		},
		["Polyline2D"] = doc =>
		{
			Polyline2D pline = new Polyline2D { IsClosed = true };
			pline.Vertices.Add(new Vertex2D(new XY(0, 0)));
			pline.Vertices.Add(new Vertex2D(new XY(5, 0)));
			pline.Vertices.Add(new Vertex2D(new XY(5, 5)));
			doc.Entities.Add(pline);
			return rt =>
			{
				Polyline2D got = single<Polyline2D>(rt);
				Assert.Equal(pline.IsClosed, got.IsClosed);
				Assert.Equal(pline.Vertices.Count(), got.Vertices.Count());
				List<Vertex2D> expected = pline.Vertices.ToList();
				List<Vertex2D> actual = got.Vertices.ToList();
				for (int i = 0; i < expected.Count; i++)
				{
					assertEqual(expected[i].Location, actual[i].Location);
				}
			};
		},
		["Spline"] = doc =>
		{
			Spline spline = new Spline { Degree = 3 };
			spline.ControlPoints.Add(new XYZ(0, 0, 0));
			spline.ControlPoints.Add(new XYZ(10, 20, 0));
			spline.ControlPoints.Add(new XYZ(20, -10, 0));
			spline.ControlPoints.Add(new XYZ(30, 5, 0));
			spline.Knots.AddRange(new double[] { 0, 0, 0, 0, 1, 1, 1, 1 });
			doc.Entities.Add(spline);
			return rt =>
			{
				Spline got = single<Spline>(rt);
				Assert.Equal(spline.Degree, got.Degree);
				Assert.Equal(spline.ControlPoints.Count, got.ControlPoints.Count);
				Assert.Equal(spline.Knots.Count, got.Knots.Count);
				for (int i = 0; i < spline.ControlPoints.Count; i++)
				{
					assertEqual(spline.ControlPoints[i], got.ControlPoints[i]);
				}
			};
		},
		["TextEntity"] = doc =>
		{
			TextEntity text = new TextEntity
			{
				Value = "MoreDwg",
				InsertPoint = new XYZ(1, 2, 0),
				Height = 2.5,
				Rotation = 0.5,
			};
			doc.Entities.Add(text);
			return rt =>
			{
				TextEntity got = single<TextEntity>(rt);
				Assert.Equal(text.Value, got.Value);
				assertEqual(text.InsertPoint, got.InsertPoint);
				Assert.Equal(text.Height, got.Height, 9);
				Assert.Equal(text.Rotation, got.Rotation, 9);
			};
		},
		["MText"] = doc =>
		{
			MText mtext = new MText
			{
				Value = "MoreDwg mtext",
				InsertPoint = new XYZ(4, 5, 0),
				Height = 3,
			};
			doc.Entities.Add(mtext);
			return rt =>
			{
				MText got = single<MText>(rt);
				Assert.Equal(mtext.Value, got.Value);
				assertEqual(mtext.InsertPoint, got.InsertPoint);
				Assert.Equal(mtext.Height, got.Height, 9);
			};
		},
		["Hatch"] = doc =>
		{
			Hatch hatch = new Hatch { IsSolid = true };
			hatch.SeedPoints.Add(new XY(1, 1));

			Hatch.BoundaryPath.Polyline boundary = new Hatch.BoundaryPath.Polyline { IsClosed = true };
			boundary.Vertices.Add(new XYZ(0, 0, 0));
			boundary.Vertices.Add(new XYZ(10, 0, 0));
			boundary.Vertices.Add(new XYZ(10, 10, 0));

			Hatch.BoundaryPath path = new Hatch.BoundaryPath();
			path.Edges.Add(boundary);
			hatch.Paths.Add(path);

			doc.Entities.Add(hatch);
			return rt =>
			{
				Hatch got = single<Hatch>(rt);
				Assert.Equal(hatch.IsSolid, got.IsSolid);
				Assert.Equal(hatch.Pattern.Name, got.Pattern.Name);
				Assert.Equal(hatch.Paths.Count, got.Paths.Count);
				Assert.Equal(hatch.SeedPoints.Count, got.SeedPoints.Count);
			};
		},
		["Insert"] = doc =>
		{
			BlockRecord record = new BlockRecord("moredwg_block");
			record.Entities.Add(new Circle { Center = XYZ.Zero, Radius = 4 });
			doc.BlockRecords.Add(record);

			Insert insert = new Insert(record)
			{
				InsertPoint = new XYZ(7, 8, 0),
				XScale = 2,
				YScale = 3,
				Rotation = 0.25,
			};
			doc.Entities.Add(insert);
			return rt =>
			{
				Insert got = single<Insert>(rt);
				Assert.Equal(insert.Block.Name, got.Block.Name);
				assertEqual(insert.InsertPoint, got.InsertPoint);
				Assert.Equal(insert.XScale, got.XScale, 9);
				Assert.Equal(insert.YScale, got.YScale, 9);
				Assert.Equal(insert.Rotation, got.Rotation, 9);
				Assert.Single(got.Block.Entities.OfType<Circle>());
			};
		},
		["Attribute"] = doc =>
		{
			BlockRecord record = new BlockRecord("moredwg_attblock");
			record.Entities.Add(new AttributeDefinition
			{
				InsertPoint = XYZ.Zero,
				Tag = "TAG",
				Prompt = "prompt",
				Value = "definition",
				Height = 2,
			});
			doc.BlockRecords.Add(record);

			//The constructor already creates one attribute per definition of the block; setting a
			//value on it is what an application does, adding another one would duplicate the tag.
			Insert insert = new Insert(record) { InsertPoint = XYZ.Zero };
			insert.Attributes.First().Value = "instance";
			doc.Entities.Add(insert);
			return rt =>
			{
				Insert got = single<Insert>(rt);
				AttributeEntity att = Assert.Single(got.Attributes);
				Assert.Equal("TAG", att.Tag);
				Assert.Equal("instance", att.Value);
				AttributeDefinition definition = Assert.Single(got.Block.Entities.OfType<AttributeDefinition>());
				Assert.Equal("TAG", definition.Tag);
				Assert.Equal("definition", definition.Value);
			};
		},
		["Leader"] = doc =>
		{
			Leader leader = new Leader();
			leader.Vertices.Add(new XYZ(0, 0, 0));
			leader.Vertices.Add(new XYZ(10, 10, 0));
			leader.Vertices.Add(new XYZ(20, 10, 0));
			doc.Entities.Add(leader);
			return rt =>
			{
				Leader got = single<Leader>(rt);
				Assert.Equal(leader.Vertices.Count, got.Vertices.Count);
				for (int i = 0; i < leader.Vertices.Count; i++)
				{
					assertEqual(leader.Vertices[i], got.Vertices[i]);
				}
			};
		},
		["DimensionLinear"] = doc =>
		{
			DimensionLinear dimension = new DimensionLinear
			{
				FirstPoint = new XYZ(0, 0, 0),
				SecondPoint = new XYZ(10, 0, 0),
				DefinitionPoint = new XYZ(10, 0, 0),
				TextMiddlePoint = new XYZ(5, 5, 0),
				Rotation = 0,
			};
			doc.Entities.Add(dimension);
			return rt =>
			{
				DimensionLinear got = single<DimensionLinear>(rt);
				assertEqual(dimension.FirstPoint, got.FirstPoint);
				assertEqual(dimension.SecondPoint, got.SecondPoint);
				assertEqual(dimension.TextMiddlePoint, got.TextMiddlePoint);
			};
		},
		["Wipeout"] = doc =>
		{
			Wipeout wipeout = new Wipeout
			{
				Size = new XY(1, 1),
				ClippingState = true,
			};

			//A rectangular boundary keeps two corners only; a boundary with more vertices has to
			//say so, otherwise the writer stores the first two and the rest is lost. ClipType is
			//derived from the vertex count, so adding the four corners is what says it.
			wipeout.ClipBoundaryVertices.Add(new XY(0, 0));
			wipeout.ClipBoundaryVertices.Add(new XY(0, 1));
			wipeout.ClipBoundaryVertices.Add(new XY(1, 1));
			wipeout.ClipBoundaryVertices.Add(new XY(1, 0));
			doc.Entities.Add(wipeout);
			return rt =>
			{
				Wipeout got = single<Wipeout>(rt);
				Assert.Equal(wipeout.ClippingState, got.ClippingState);
				Assert.Equal(wipeout.ClipBoundaryVertices.Count, got.ClipBoundaryVertices.Count);
			};
		},
	};

	public static TheoryData<string, ACadVersion> DwgCases
	{
		get
		{
			TheoryData<string, ACadVersion> data = new();
			foreach (string name in _cases.Keys)
			{
				//R2000 is the version used for debugging and R2018 the one the project ships.
				data.Add(name, ACadVersion.AC1015);
				data.Add(name, ACadVersion.AC1032);
			}

			return data;
		}
	}

	public static TheoryData<string> DxfCases
	{
		get
		{
			TheoryData<string> data = new();
			foreach (string name in _cases.Keys)
			{
				data.Add(name);
			}

			return data;
		}
	}

	[Theory]
	[MemberData(nameof(DwgCases))]
	public void DwgRoundTripKeepsTheEntity(string name, ACadVersion version)
	{
		CadDocument doc = new CadDocument(version);
		Action<CadDocument> check = _cases[name](doc);

		MemoryStream ms = new MemoryStream();
		DwgWriter.Write(ms, doc);
		using MemoryStream readStream = new MemoryStream(ms.ToArray());

		check(DwgReader.Read(readStream));
	}

	[Theory]
	[MemberData(nameof(DxfCases))]
	public void DxfRoundTripKeepsTheEntity(string name)
	{
		CadDocument doc = new CadDocument(ACadVersion.AC1032);
		Action<CadDocument> check = _cases[name](doc);

		MemoryStream ms = new MemoryStream();
		using (DxfWriter writer = new DxfWriter(ms, doc, false))
		{
			writer.Write();
		}

		using MemoryStream readStream = new MemoryStream(ms.ToArray());
		check(DxfReader.Read(readStream));
	}

	private static T single<T>(CadDocument doc) where T : Entity
	{
		return Assert.Single(doc.Entities.OfType<T>());
	}

	private static void assertEqual(XYZ expected, XYZ actual)
	{
		Assert.Equal(expected.X, actual.X, 9);
		Assert.Equal(expected.Y, actual.Y, 9);
		Assert.Equal(expected.Z, actual.Z, 9);
	}
}
