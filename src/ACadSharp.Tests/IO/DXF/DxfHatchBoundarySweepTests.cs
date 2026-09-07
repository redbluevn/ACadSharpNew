using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO.DXF;

public class DxfHatchBoundarySweepTests
{
	[Fact]
	public void DxfRoundTripKeepsAFullSweepArcBoundary()
	{
		//A circular hatch boundary is stored as a full sweep - -PI to PI here, which is what real
		//drawings carry. Both endpoints used to be converted with MathHelper.RadToDeg, which
		//normalises each one on its own, so they were written as 180 and 180: the same number twice,
		//with the 360 degrees between them gone. The reader only calls DegToRad, so it cannot get
		//the sweep back, and the boundary - and with it the fill - disappears from the file.
		//
		//AutoCAD 2027 judged both files and never complained about either: the drawing written the
		//old way opens with 0 audit errors, and its extents are a single point at (60, 100) where
		//the circle should be. The one written this way reports (60, 60)..(140, 140), the circle of
		//radius 40 about (100, 100). Silent loss, which is why this test exists.
		CadDocument doc = new CadDocument();

		Hatch hatch = new Hatch
		{
			IsSolid = true,
			Pattern = HatchPattern.Solid,
		};

		Hatch.BoundaryPath path = new Hatch.BoundaryPath();
		path.Edges.Add(new Hatch.BoundaryPath.Arc
		{
			Center = new XY(100, 100),
			Radius = 40,
			StartAngle = -Math.PI,
			EndAngle = Math.PI,
			CounterClockWise = true,
		});
		hatch.Paths.Add(path);
		doc.Entities.Add(hatch);

		string file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dxf");
		try
		{
			using (DxfWriter writer = new DxfWriter(file, doc, false))
			{
				writer.Write();
			}

			CadDocument read = DxfReader.Read(file);
			Hatch.BoundaryPath.Arc arc = read.Entities.OfType<Hatch>()
				.SelectMany(h => h.Paths)
				.SelectMany(p => p.Edges)
				.OfType<Hatch.BoundaryPath.Arc>()
				.Single();

			Assert.Equal(2 * Math.PI, Math.Abs(arc.EndAngle - arc.StartAngle), 9);
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
	public void DxfRoundTripKeepsAFullSweepEllipseBoundary()
	{
		//The ellipse edge is written through the same conversion and had the same fault.
		CadDocument doc = new CadDocument();

		Hatch hatch = new Hatch
		{
			IsSolid = true,
			Pattern = HatchPattern.Solid,
		};

		Hatch.BoundaryPath path = new Hatch.BoundaryPath();
		path.Edges.Add(new Hatch.BoundaryPath.Ellipse
		{
			Center = new XY(0, 0),
			MajorAxisEndPoint = new XY(50, 0),
			RadiusRatio = 0.5,
			StartAngle = -Math.PI,
			EndAngle = Math.PI,
			CounterClockWise = true,
		});
		hatch.Paths.Add(path);
		doc.Entities.Add(hatch);

		string file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dxf");
		try
		{
			using (DxfWriter writer = new DxfWriter(file, doc, false))
			{
				writer.Write();
			}

			CadDocument read = DxfReader.Read(file);
			Hatch.BoundaryPath.Ellipse ellipse = read.Entities.OfType<Hatch>()
				.SelectMany(h => h.Paths)
				.SelectMany(p => p.Edges)
				.OfType<Hatch.BoundaryPath.Ellipse>()
				.Single();

			Assert.Equal(2 * Math.PI, Math.Abs(ellipse.EndAngle - ellipse.StartAngle), 9);
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
	public void RadToDegNormalisesOnlyWhenAskedTo()
	{
		//The flag existed and was ignored, which is where the loss came from. Pinned here because
		//the writer above depends on it: with normalisation the two endpoints of a full sweep are
		//the same number, and without it they are 360 degrees apart.
		Assert.Equal(180, CSMath.MathHelper.RadToDeg(-Math.PI), 9);
		Assert.Equal(180, CSMath.MathHelper.RadToDeg(Math.PI), 9);

		Assert.Equal(-180, CSMath.MathHelper.RadToDeg(-Math.PI, false), 9);
		Assert.Equal(180, CSMath.MathHelper.RadToDeg(Math.PI, false), 9);
	}
}
