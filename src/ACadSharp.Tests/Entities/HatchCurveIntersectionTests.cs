using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using CSMath.Geometry;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.Entities;

public class HatchCurveIntersectionTests
{
    private static Hatch.BoundaryPath.Edge curve(bool ellipse, bool clockwise, double start, double end)
    {
        double storedStart = clockwise ? MathHelper.TwoPI - end : start;
        double storedEnd = clockwise ? MathHelper.TwoPI - start : end;
        return ellipse
            ? new Hatch.BoundaryPath.Ellipse { MajorAxisEndPoint = new XY(20, 0), RadiusRatio = 0.5, StartAngle = storedStart, EndAngle = storedEnd, CounterClockWise = !clockwise }
            : new Hatch.BoundaryPath.Arc { Radius = 10, StartAngle = storedStart, EndAngle = storedEnd, CounterClockWise = !clockwise };
    }

    [Theory]
    [InlineData(false, false, 0)] [InlineData(false, true, 0)]
    [InlineData(true, false, 0)] [InlineData(true, true, 0)]
    [InlineData(false, false, 1)] [InlineData(false, true, 1)]
    [InlineData(true, false, 1)] [InlineData(true, true, 1)]
    [InlineData(false, false, 2)] [InlineData(false, true, 2)]
    [InlineData(true, false, 2)] [InlineData(true, true, 2)]
    public void OpenCurveDoesNotIntersectItsEndpointChord(bool ellipse, bool clockwise, int sweep)
    {
        // Quarter, major, and zero-crossing half arcs; the horizontal probe also
        // crosses their endpoint chords, which are not part of these curves.
        double start = sweep == 0 ? 0 : sweep == 1 ? Math.PI / 2 : 3 * Math.PI / 2;
        double end = sweep == 1 ? MathHelper.TwoPI : Math.PI / 2;
        var edge = curve(ellipse, clockwise, start, end);
        var hit = Assert.Single(edge.FindIntersections(new Line2D(new XY(0, 2.5), XY.AxisX)));
        double expectedX = (ellipse ? 20 : 10) * Math.Sqrt(1 - 0.25 * 0.25) * (sweep == 1 ? -1 : 1);
        Assert.InRange(Math.Abs(hit.X - expectedX), 0, 0.002);
        Assert.Equal(2.5, hit.Y, 9);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void FullCurveStillHasBothIntersections(bool ellipse, bool clockwise)
    {
        var edge = curve(ellipse, clockwise, 0, MathHelper.TwoPI);
        var hits = edge.FindIntersections(new Line2D(new XY(0, 2.5), XY.AxisX)).OrderBy(p => p.X).ToArray();
        Assert.Equal(2, hits.Length);
        double x = (ellipse ? 20 : 10) * Math.Sqrt(1 - 0.25 * 0.25);
        Assert.InRange(Math.Abs(hits[0].X + x), 0, 0.002);
        Assert.InRange(Math.Abs(hits[1].X - x), 0, 0.002);
    }

    private static Hatch quarterHatch(bool ellipse, bool clockwise)
    {
        var hatch = new Hatch { Pattern = new HatchPattern("CUSTOM"), PatternType = HatchPatternType.Custom };
        hatch.Pattern.Lines.Add(new HatchPattern.Line { BasePoint = new XY(0, 1), Offset = new XY(0, 2) });
        var path = new Hatch.BoundaryPath { Flags = BoundaryPathFlags.External };
        path.Edges.Add(curve(ellipse, clockwise, 0, Math.PI / 2));
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = new XY(0, 10), End = XY.Zero });
        path.Edges.Add(new Hatch.BoundaryPath.Line { Start = XY.Zero, End = new XY(ellipse ? 20 : 10, 0) });
        hatch.Paths.Add(path);
        hatch.SeedPoints.Add(new XY(1, 1));
        return hatch;
    }

    private static void assertQuarterPattern(Hatch hatch, bool ellipse)
    {
        var lines = hatch.ExplodePattern().Cast<Line>().OrderBy(l => l.StartPoint.Y).ToArray();
        Assert.Equal(5, lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            double y = 1 + 2 * i;
            Assert.Equal(y, lines[i].StartPoint.Y, 8);
            Assert.Equal(y, lines[i].EndPoint.Y, 8);
            Assert.Equal(0, lines[i].StartPoint.X, 8);
            double x = (ellipse ? 20 : 10) * Math.Sqrt(1 - y * y / 100);
            Assert.InRange(Math.Abs(lines[i].EndPoint.X - x), 0, 0.002);
        }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void QuarterPatternFillsTheAreaBetweenAxesAndCurve(bool ellipse, bool clockwise)
    {
        assertQuarterPattern(quarterHatch(ellipse, clockwise), ellipse);
    }

    [Theory]
    [InlineData(false, false, ACadVersion.AC1015)] [InlineData(true, false, ACadVersion.AC1015)]
    [InlineData(false, false, ACadVersion.AC1032)] [InlineData(true, false, ACadVersion.AC1032)]
    [InlineData(false, true, ACadVersion.AC1032)] [InlineData(true, true, ACadVersion.AC1032)]
    public void QuarterPatternSurvivesRoundTrip(bool ellipse, bool dxf, ACadVersion version)
    {
        var doc = new CadDocument(); doc.Header.Version = version;
        doc.Entities.Add(quarterHatch(ellipse, false));
        using var stream = new MemoryStream();
        if (dxf) DxfWriter.Write(stream, doc); else DwgWriter.Write(stream, doc);
        using var input = new MemoryStream(stream.ToArray());
        var back = dxf ? DxfReader.Read(input) : DwgReader.Read(input);
        assertQuarterPattern(Assert.Single(back.Entities.OfType<Hatch>()), ellipse);
    }
}
