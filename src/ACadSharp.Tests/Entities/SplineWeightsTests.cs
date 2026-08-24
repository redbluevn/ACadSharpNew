namespace ACadSharp.Tests.Entities;

using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using System;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// A spline carries a weight for every control point or none at all. <see cref="Spline.Weights"/>
/// is a public mutable list, so a caller can leave it half filled, and both writers used to accept
/// that and fail in their own way: DWG threw an index exception naming nothing, part way through a
/// file; DXF wrote fewer 41s than 10s and reported nothing at all.
/// </summary>
public class SplineWeightsTests
{
	[Fact]
	public void APartialWeightListIsRefusedByTheDwgWriterWithAReadableMessage()
	{
		CadDocument doc = this.document(weights: 1, controlPoints: 5);

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
		{
			using MemoryStream stream = new();
			using DwgWriter writer = new(stream, doc);
			writer.Write();
		});

		Assert.Contains("1 weights for 5 control points", ex.Message);
	}

	[Fact]
	public void APartialWeightListIsRefusedByTheDxfWriterToo()
	{
		CadDocument doc = this.document(weights: 1, controlPoints: 5);

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
		{
			using MemoryStream stream = new();
			using DxfWriter writer = new(stream, doc, false);
			writer.Write();
		});

		Assert.Contains("control points", ex.Message);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(5)]
	public void AWeightListThatIsEmptyOrCompleteIsWritten(int weights)
	{
		CadDocument doc = this.document(weights: weights, controlPoints: 5);

		using MemoryStream stream = new();
		using (DwgWriter writer = new(stream, doc))
		{
			writer.Write();
		}

		CadDocument back = DwgReader.Read(new MemoryStream(stream.ToArray()));
		Spline spline = back.Entities.OfType<Spline>().Single();
		Assert.Equal(5, spline.ControlPoints.Count);
	}

	[Fact]
	public void AnOrdinaryNonRationalSplineConvertsToAHatchBoundaryEdge()
	{
		//The same defect one layer up, and worse: this is a public constructor and it failed on the
		//ordinary case, not the malformed one. A non-rational spline has an empty weight list -
		//that is what non-rational means - and the conversion read Weights[i] for every control
		//point regardless, so it threw an index exception naming nothing.
		Spline plain = this.spline(weights: 0, controlPoints: 5);

		Hatch.BoundaryPath.Spline edge = new(plain);

		Assert.Equal(5, edge.ControlPoints.Count);
		Assert.False(edge.IsRational);
		Assert.All(edge.ControlPoints, p => Assert.Equal(1.0, p.Z));
	}

	[Fact]
	public void AWeightedSplineKeepsItsWeightsThroughTheHatchEdge()
	{
		Spline rational = this.spline(weights: 5, controlPoints: 5);
		rational.Weights[2] = 2.5;

		Hatch.BoundaryPath.Spline edge = new(rational);

		Assert.True(edge.IsRational);
		Assert.Equal(2.5, edge.ControlPoints[2].Z);
	}

	[Fact]
	public void APartialWeightListIsRefusedByTheHatchEdgeToo()
	{
		Spline partial = this.spline(weights: 1, controlPoints: 5);

		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
			() => new Hatch.BoundaryPath.Spline(partial));

		Assert.Contains("1 weights for 5 control points", ex.Message);
	}

	private Spline spline(int weights, int controlPoints)
	{
		Spline spline = new Spline();
		spline.Degree = 3;
		for (int i = 0; i < controlPoints; i++)
		{
			spline.ControlPoints.Add(new XYZ(i, i, 0));
		}

		for (int i = 0; i < controlPoints + 4; i++)
		{
			spline.Knots.Add(i);
		}

		for (int i = 0; i < weights; i++)
		{
			spline.Weights.Add(1.0);
		}

		return spline;
	}

	private CadDocument document(int weights, int controlPoints)
	{
		CadDocument doc = new CadDocument();
		Spline spline = new Spline();
		spline.Degree = 3;
		for (int i = 0; i < controlPoints; i++)
		{
			spline.ControlPoints.Add(new XYZ(i, i, 0));
		}

		for (int i = 0; i < controlPoints + 4; i++)
		{
			spline.Knots.Add(i);
		}

		for (int i = 0; i < weights; i++)
		{
			spline.Weights.Add(1.0);
		}

		spline.Flags = SplineFlags.Rational;
		doc.Entities.Add(spline);
		doc.Header.Version = ACadVersion.AC1032;
		return doc;
	}
}
