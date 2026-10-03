using ACadSharp.IO;
using CSMath;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.Entities;

public class HatchAffineTransformTests
{
    private static void near(XYZ expected, XYZ actual)
    {
        Assert.InRange((expected - actual).GetLength(), 0, 1e-8);
    }

    private static XYZ vector(Transform transform, XYZ value)
        => (transform.Matrix * new XYZM(value.X, value.Y, value.Z, 0)).Convert<XYZ>();

    private static ACadSharp.Entities.Hatch hatch(XYZ normal, double elevation)
    {
        var h = new ACadSharp.Entities.Hatch { Normal = normal, Elevation = elevation, Pattern = new ACadSharp.Entities.HatchPattern("CUSTOM") };
        var path = new ACadSharp.Entities.Hatch.BoundaryPath();
        var pts = new[] { new XY(2, 3), new XY(5, 3), new XY(5, 7), new XY(2, 7) };
        for (int i = 0; i < pts.Length; i++) path.Edges.Add(new ACadSharp.Entities.Hatch.BoundaryPath.Line { Start = pts[i], End = pts[(i + 1) % pts.Length] });
        h.Paths.Add(path);
        h.SeedPoints.Add(new XY(3, 4));
        h.Pattern.Lines.Add(new ACadSharp.Entities.HatchPattern.Line { BasePoint = new XY(3, 4), Offset = new XY(0, 2), Angle = 0.3, DashLengths = { 3, -1 } });
        return h;
    }

    private static XYZ world(ACadSharp.Entities.Hatch h, XY point)
        => Matrix4.GetArbitraryAxis(h.Normal) * new XYZ(point.X, point.Y, h.Elevation);

    [Theory]
    [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(0, 2)]
    [InlineData(1, 0)] [InlineData(1, 1)] [InlineData(1, 2)]
    [InlineData(2, 0)] [InlineData(2, 1)] [InlineData(2, 2)]
    public void HatchTransformsOcsPointsAndElevationInWorldSpace(int normalKind, int transformKind)
    {
        XYZ normal = normalKind == 0 ? XYZ.AxisZ : normalKind == 1 ? -XYZ.AxisZ : new XYZ(1, 2, 3).Normalize();
        var h = hatch(normal, 5);
        var transform = transformKind == 0 ? Transform.CreateTranslation(new XYZ(10, -4, 7))
            : transformKind == 1 ? new Transform(Transform.CreateTranslation(new XYZ(10, -4, 7)).Matrix * Transform.CreateRotation(XYZ.AxisX, 0.7).Matrix * Transform.CreateScaling(new XYZ(2)).Matrix)
            : Transform.CreateScaling(new XYZ(-1, 1, 1));
        var points = h.Paths[0].Edges.Cast<ACadSharp.Entities.Hatch.BoundaryPath.Line>().Select(e => transform.ApplyTransform(world(h, e.Start))).ToArray();
        XYZ seed = transform.ApplyTransform(world(h, h.SeedPoints[0]));
        XYZ patternBase = transform.ApplyTransform(world(h, h.Pattern.Lines[0].BasePoint));
        var basis = Matrix4.GetArbitraryAxis(h.Normal);
        XYZ offset = vector(transform, basis * new XYZ(0, 2, 0));
        XYZ direction = vector(transform, basis * new XYZ(Math.Cos(0.3), Math.Sin(0.3), 0)).Normalize();
        h.ApplyTransform(transform);
        for (int i = 0; i < points.Length; i++) near(points[i], world(h, ((ACadSharp.Entities.Hatch.BoundaryPath.Line)h.Paths[0].Edges[i]).Start));
        near(seed, world(h, h.SeedPoints[0]));
        near(patternBase, world(h, h.Pattern.Lines[0].BasePoint));
        basis = Matrix4.GetArbitraryAxis(h.Normal);
        near(offset, basis * new XYZ(h.Pattern.Lines[0].Offset.X, h.Pattern.Lines[0].Offset.Y, 0));
        near(direction, basis * new XYZ(Math.Cos(h.Pattern.Lines[0].Angle), Math.Sin(h.Pattern.Lines[0].Angle), 0));
        Assert.Equal(transformKind == 1 ? 2 : 1, h.PatternScale, 9);
        Assert.Equal(transformKind == 1 ? 6 : 3, h.Pattern.Lines[0].DashLengths[0], 9);
        Assert.Equal(transformKind == 1 ? -2 : -1, h.Pattern.Lines[0].DashLengths[1], 9);
    }

    [Theory]
    [InlineData(false, false, false)] [InlineData(false, true, false)]
    [InlineData(true, false, false)] [InlineData(true, true, false)]
    [InlineData(false, false, true)] [InlineData(false, true, true)]
    [InlineData(true, false, true)] [InlineData(true, true, true)]
    public void MirroredCurveRetainsTheTransformedArc(bool ellipse, bool clockwise, bool full)
    {
        double start = full ? 0 : clockwise ? 2 * Math.PI - 1.9 : 0.2;
        double end = full ? 2 * Math.PI : clockwise ? 2 * Math.PI - 0.2 : 1.9;
        ACadSharp.Entities.Hatch.BoundaryPath.Edge edge = ellipse
            ? new ACadSharp.Entities.Hatch.BoundaryPath.Ellipse { Center = new XY(2, 3), MajorAxisEndPoint = new XY(12, 16), RadiusRatio = 0.5, StartAngle = start, EndAngle = end, CounterClockWise = !clockwise }
            : new ACadSharp.Entities.Hatch.BoundaryPath.Arc { Center = new XY(2, 3), Radius = 10, StartAngle = start, EndAngle = end, CounterClockWise = !clockwise };
        var transform = new Transform(Transform.CreateTranslation(new XYZ(4, 5, 0)).Matrix * Transform.CreateRotation(XYZ.AxisZ, 0.37).Matrix * Transform.CreateScaling(new XYZ(-2, 2, 1)).Matrix);
        XYZ[] expected = ((ellipse ? ((ACadSharp.Entities.Hatch.BoundaryPath.Ellipse)edge).PolygonalVertexes(64) : ((ACadSharp.Entities.Hatch.BoundaryPath.Arc)edge).PolygonalVertexes(64))).Select(p => transform.ApplyTransform(p)).ToArray();
        edge.ApplyTransform(transform);
        var actual = ellipse ? ((ACadSharp.Entities.Hatch.BoundaryPath.Ellipse)edge).PolygonalVertexes(64) : ((ACadSharp.Entities.Hatch.BoundaryPath.Arc)edge).PolygonalVertexes(64);
        Assert.Equal(expected.Length, actual.Count);
        foreach (XYZ p in expected) Assert.InRange(actual.Min(q => (p - q).GetLength()), 0, 1e-8);
        Assert.Equal(clockwise, ellipse ? ((ACadSharp.Entities.Hatch.BoundaryPath.Ellipse)edge).CounterClockWise : ((ACadSharp.Entities.Hatch.BoundaryPath.Arc)edge).CounterClockWise);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NonuniformScaleAndShearRejectBeforeMutatingHatch(bool shear)
    {
        var h = hatch(XYZ.AxisZ, 5);
        var before = h.GetBoundingBox();
        var m = Transform.CreateScaling(new XYZ(2, 1, 1)).Matrix;
        if (shear) { m = new Transform().Matrix; m.M10 = 0.5; }
        Assert.Throws<NotSupportedException>(() => h.ApplyTransform(new Transform(m)));
        near(before.Min, h.GetBoundingBox().Min); near(before.Max, h.GetBoundingBox().Max);
        Assert.Equal(XYZ.AxisZ, h.Normal); Assert.Equal(5, h.Elevation);
        Assert.Equal(new XY(3, 4), h.SeedPoints[0]);
        Assert.Equal(new XY(3, 4), h.Pattern.Lines[0].BasePoint);
        Assert.Equal(new XY(0, 2), h.Pattern.Lines[0].Offset);
        Assert.Equal(1, h.PatternScale); Assert.Equal(0, h.PatternAngle);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NonuniformCurveTransformRejectsBeforeMutating(bool ellipse)
    {
        ACadSharp.Entities.Hatch.BoundaryPath.Edge edge = ellipse
            ? new ACadSharp.Entities.Hatch.BoundaryPath.Ellipse { Center = new XY(2, 3), MajorAxisEndPoint = new XY(20, 0), RadiusRatio = 0.5, EndAngle = 2 * Math.PI, CounterClockWise = true }
            : new ACadSharp.Entities.Hatch.BoundaryPath.Arc { Center = new XY(2, 3), Radius = 10, EndAngle = 2 * Math.PI, CounterClockWise = true };
        var before = edge.GetBoundingBox();
        Assert.Throws<NotSupportedException>(() => edge.ApplyTransform(Transform.CreateScaling(new XYZ(2, 1, 1))));
        near(before.Min, edge.GetBoundingBox().Min); near(before.Max, edge.GetBoundingBox().Max);
    }

    [Theory]
    [InlineData(false, ACadVersion.AC1015)] [InlineData(false, ACadVersion.AC1032)] [InlineData(true, ACadVersion.AC1032)]
    public void TransformedHatchPlaneSurvivesRoundTrip(bool dxf, ACadVersion version)
    {
        var h = hatch(-XYZ.AxisZ, 5);
        h.ApplyTransform(Transform.CreateTranslation(new XYZ(10, 0, 7)));
        var document = new CadDocument(); document.Header.Version = version; document.Entities.Add(h);
        using var stream = new MemoryStream();
        if (dxf) DxfWriter.Write(stream, document); else DwgWriter.Write(stream, document);
        var doc = dxf ? DxfReader.Read(new MemoryStream(stream.ToArray())) : DwgReader.Read(new MemoryStream(stream.ToArray()));
        var actual = Assert.Single(doc.Entities.OfType<ACadSharp.Entities.Hatch>());
        near(h.GetBoundingBox().Min, actual.GetBoundingBox().Min);
        near(h.GetBoundingBox().Max, actual.GetBoundingBox().Max);
        near(world(h, h.SeedPoints[0]), world(actual, actual.SeedPoints[0]));
    }
}
