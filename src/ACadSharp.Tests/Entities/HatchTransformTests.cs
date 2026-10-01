using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.Entities;

public class HatchTransformTests
{
    private static void near(XYZ expected, XYZ actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Z, actual.Z, 9);
    }

    private static Hatch patternedHatch()
    {
        var hatch = new Hatch { Pattern = new HatchPattern("custom") };
        hatch.Pattern.Lines.Add(new HatchPattern.Line
        {
            BasePoint = new XY(3, 4), Offset = new XY(-1, 2), Angle = 0.7,
            DashLengths = { 3, -1, 0 },
        });
        hatch.PatternScale = 2;
        hatch.PatternAngle = 0.4;
        return hatch;
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 1.0)]
    [InlineData(30.0, -20.0, 0.0, 1.0)]
    [InlineData(30.0, -20.0, 0.4, 3.0)]
    public void TransformMovesSeedPointsWithoutChangingTheSourceClone(double dx, double dy, double angle, double scale)
    {
        var source = patternedHatch(); source.SeedPoints.Add(new XY(2, 3));
        var clone = (Hatch)source.Clone();
        Assert.NotSame(source.SeedPoints, clone.SeedPoints);
        var transform = new Transform(Transform.CreateTranslation(new XYZ(dx, dy, 0)).Matrix
            * Transform.CreateScaling(new XYZ(scale)).Matrix * Transform.CreateRotation(XYZ.AxisZ, angle).Matrix);
        clone.ApplyTransform(transform);
        double c = Math.Cos(angle), s = Math.Sin(angle);
        near(new XYZ(dx + scale * (2 * c - 3 * s), dy + scale * (2 * s + 3 * c), 0), (XYZ)Assert.Single(clone.SeedPoints));
        Assert.Equal(new XY(2, 3), Assert.Single(source.SeedPoints));
    }

    [Fact]
    public void DefaultHatchIdentityKeepsUnitPatternScale()
    {
        var hatch = new Hatch();
        Assert.Equal(1, hatch.PatternScale);
        hatch.ApplyTransform(new Transform());
        Assert.Equal(1, hatch.PatternScale);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void CombinedTranslationRotationAndUniformScaleMovesCurveSamples(bool ellipse, bool counterClockwise)
    {
        Hatch.BoundaryPath.Edge edge = ellipse
            ? new Hatch.BoundaryPath.Ellipse { Center = new XY(2, 3), MajorAxisEndPoint = new XY(3, 4), RadiusRatio = 0.45, StartAngle = 0.2, EndAngle = 2.1, CounterClockWise = counterClockwise }
            : new Hatch.BoundaryPath.Arc { Center = new XY(2, 3), Radius = 4, StartAngle = 0.2, EndAngle = 2.1, CounterClockWise = counterClockwise };
        var before = ellipse ? ((Hatch.BoundaryPath.Ellipse)edge).PolygonalVertexes(64) : ((Hatch.BoundaryPath.Arc)edge).PolygonalVertexes(64);
        const double angle = 0.4, scale = 3;
        var move = Transform.CreateTranslation(new XYZ(30, -20, 0));
        var rotate = Transform.CreateRotation(XYZ.AxisZ, angle);
        var resize = Transform.CreateScaling(new XYZ(scale));
        edge.ApplyTransform(new Transform(move.Matrix * resize.Matrix * rotate.Matrix));
        var after = ellipse ? ((Hatch.BoundaryPath.Ellipse)edge).PolygonalVertexes(64) : ((Hatch.BoundaryPath.Arc)edge).PolygonalVertexes(64);
        Assert.Equal(before.Count, after.Count);
        for (int i = 0; i < before.Count; i++)
        {
            var p = before[i]; double c = Math.Cos(angle), s = Math.Sin(angle);
            near(new XYZ(30 + scale * (p.X * c - p.Y * s), -20 + scale * (p.X * s + p.Y * c), 0), after[i]);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FullCircularBoundaryDoesNotCollapseAfterRotation(bool counterClockwise)
    {
        var edge = new Hatch.BoundaryPath.Arc { Center = new XY(2, 3), Radius = 4, StartAngle = 0, EndAngle = 2 * Math.PI, CounterClockWise = counterClockwise };
        edge.ApplyTransform(Transform.CreateRotation(XYZ.AxisZ, 0.4));
        Assert.Equal(2 * Math.PI, edge.EndAngle - edge.StartAngle, 9);
        var box = edge.GetBoundingBox();
        Assert.Equal(8, box.Max.X - box.Min.X, 2);
        Assert.Equal(8, box.Max.Y - box.Min.Y, 2);
    }

    [Theory]
    [InlineData(0.0)] [InlineData(2.0)]
    public void HatchCombinedTransformAppliesOnlyPatternDelta(double importedScale)
    {
        var hatch = patternedHatch();
        hatch.PatternScale = importedScale;
        if (importedScale == 0)
        {
            //Imported line definitions may remain materialized even when metadata says zero.
            hatch.Pattern.Lines[0].BasePoint = new XY(3, 4);
            hatch.Pattern.Lines[0].Offset = new XY(-1, 2);
            hatch.Pattern.Lines[0].DashLengths = new System.Collections.Generic.List<double> { 3, -1, 0 };
        }
        var expected = hatch.Pattern.Clone();
        expected.Update(new XY(30, -20), 0.4, 3);
        var transform = new Transform(Transform.CreateTranslation(new XYZ(30, -20, 0)).Matrix
            * Transform.CreateScaling(new XYZ(3)).Matrix * Transform.CreateRotation(XYZ.AxisZ, 0.4).Matrix);
        hatch.ApplyTransform(transform);
        patternNear(expected, hatch.Pattern);
        Assert.Equal(importedScale * 3, hatch.PatternScale, 9);
        Assert.Equal(0.8, hatch.PatternAngle, 9);
    }

    [Fact]
    public void ReassigningPatternMetadataDoesNotApplyItAgain()
    {
        var hatch = patternedHatch();
        var expected = hatch.Pattern.Clone();
        hatch.PatternScale = 2;
        hatch.PatternAngle = 0.4;
        patternNear(expected, hatch.Pattern);
        hatch.PatternScale = 3;
        hatch.PatternAngle = 1;
        expected.Update(XY.Zero, 0.6, 1.5);
        patternNear(expected, hatch.Pattern);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HatchIdentityOrTranslationDoesNotRescaleItsBakedPattern(bool translate)
    {
        var hatch = patternedHatch();
        var expected = hatch.Pattern.Clone();
        var shift = translate ? new XYZ(30, -20, 0) : XYZ.Zero;
        expected.Update((XY)shift, 0, 1);
        hatch.ApplyTransform(Transform.CreateTranslation(shift));
        patternNear(expected, hatch.Pattern);
        Assert.Equal(2, hatch.PatternScale, 9);
        Assert.Equal(0.4, hatch.PatternAngle, 9);
    }

    private static void patternNear(HatchPattern expected, HatchPattern actual)
    {
        Assert.Equal(expected.Lines.Count, actual.Lines.Count);
        for (int i = 0; i < expected.Lines.Count; i++)
        {
            var a = expected.Lines[i]; var b = actual.Lines[i];
            near((XYZ)a.BasePoint, (XYZ)b.BasePoint);
            near((XYZ)a.Offset, (XYZ)b.Offset);
            Assert.Equal(a.Angle, b.Angle, 9);
            Assert.Equal(a.DashLengths.Count, b.DashLengths.Count);
            for (int d = 0; d < a.DashLengths.Count; d++) Assert.Equal(a.DashLengths[d], b.DashLengths[d], 9);
        }
    }

    [Fact]
    public void DxfPatternLeavesFollowingHatchFieldsForTheOuterReader()
    {
        var hatch = patternedHatch();
        hatch.Style = HatchStyleType.Ignore;
        hatch.PixelSize = 1.25;
        hatch.SeedPoints.AddRange(new[] { new XY(2, 3), new XY(2.1, 3.2), new XY(4, 5) });
        hatch.Pattern.Lines.Add(new HatchPattern.Line { BasePoint = new XY(3, 4), Offset = new XY(-2, 3), Angle = 0.3, DashLengths = { -2, 4 } });
        hatch.Paths.Add(new Hatch.BoundaryPath(new[] { new Hatch.BoundaryPath.Arc { Center = new XY(2, 3), Radius = 4, EndAngle = 2 * Math.PI, CounterClockWise = true } }) { Flags = BoundaryPathFlags.Derived });
        var document = new CadDocument(); document.Header.Version = ACadVersion.AC1032; document.Entities.Add(hatch);
        using var stream = new MemoryStream(); DxfWriter.Write(stream, document);
        var restored = DxfReader.Read(new MemoryStream(stream.ToArray()));
        var imported = Assert.Single(restored.Entities.OfType<Hatch>());
        Assert.Equal(hatch.Style, imported.Style);
        Assert.Equal(hatch.PixelSize, imported.PixelSize);
        Assert.Equal(hatch.SeedPoints.ToArray(), imported.SeedPoints.ToArray());
        patternNear(hatch.Pattern, imported.Pattern);
    }

    [Theory]
    [InlineData(false, false, ACadVersion.AC1015)]
    [InlineData(false, false, ACadVersion.AC1032)]
    [InlineData(false, true, ACadVersion.AC1032)]
    [InlineData(true, false, ACadVersion.AC1015)]
    [InlineData(true, false, ACadVersion.AC1032)]
    [InlineData(true, true, ACadVersion.AC1032)]
    public void ImportedHatchTranslationPreservesMaterializedPattern(bool ellipse, bool dxf, ACadVersion version)
    {
        var hatch = patternedHatch();
        Hatch.BoundaryPath.Edge edge = ellipse
            ? new Hatch.BoundaryPath.Ellipse { Center = new XY(2, 3), MajorAxisEndPoint = new XY(3, 4), RadiusRatio = 0.45, EndAngle = 2 * Math.PI, CounterClockWise = true }
            : new Hatch.BoundaryPath.Arc { Center = new XY(2, 3), Radius = 4, EndAngle = 2 * Math.PI, CounterClockWise = true };
        hatch.Paths.Add(new Hatch.BoundaryPath(new[] { edge }));
        hatch.SeedPoints.Add(new XY(2, 3));
        var expectedPattern = hatch.Pattern.Clone();
        var before = hatch.GetBoundingBox();
        var document = new CadDocument(); document.Header.Version = version; document.Entities.Add(hatch);
        using var stream = new MemoryStream();
        if (dxf) DxfWriter.Write(stream, document); else DwgWriter.Write(stream, document);
        var restored = dxf ? DxfReader.Read(new MemoryStream(stream.ToArray())) : DwgReader.Read(new MemoryStream(stream.ToArray()));
        var imported = Assert.Single(restored.Entities.OfType<Hatch>());
        Assert.Equal(new XY(2, 3), Assert.Single(imported.SeedPoints));
        patternNear(expectedPattern, imported.Pattern);
        imported.PatternScale = imported.PatternScale;
        imported.PatternAngle = imported.PatternAngle;
        patternNear(expectedPattern, imported.Pattern);
        var shift = new XYZ(30, -20, 0);
        imported.ApplyTransform(Transform.CreateTranslation(shift));
        Assert.Equal(new XY(32, -17), Assert.Single(imported.SeedPoints));
        expectedPattern.Update((XY)shift, 0, 1);
        patternNear(expectedPattern, imported.Pattern);
        near(before.Min + shift, imported.GetBoundingBox().Min);
        near(before.Max + shift, imported.GetBoundingBox().Max);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TranslatingArcMovesSamplesWithoutChangingRadiusOrAngles(bool counterClockwise)
    {
        var edge = new Hatch.BoundaryPath.Arc
        {
            Center = new XY(2, 3), Radius = 4, StartAngle = 0.2, EndAngle = 2.1,
            CounterClockWise = counterClockwise,
        };
        var before = edge.PolygonalVertexes(64);
        var shift = new XYZ(30, -20, 0);
        edge.ApplyTransform(Transform.CreateTranslation(shift));
        Assert.Equal(4, edge.Radius, 9);
        Assert.Equal(0.2, edge.StartAngle, 9);
        Assert.Equal(2.1, edge.EndAngle, 9);
        Assert.Equal(counterClockwise, edge.CounterClockWise);
        var after = edge.PolygonalVertexes(64);
        Assert.Equal(before.Count, after.Count);
        for (int i = 0; i < before.Count; i++) near(before[i] + shift, after[i]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TranslatingEllipseMovesSamplesWithoutChangingMajorAxis(bool counterClockwise)
    {
        var edge = new Hatch.BoundaryPath.Ellipse
        {
            Center = new XY(2, 3), MajorAxisEndPoint = new XY(3, 4), RadiusRatio = 0.45,
            StartAngle = 0.2, EndAngle = 2.1, CounterClockWise = counterClockwise,
        };
        var before = edge.PolygonalVertexes(64);
        var shift = new XYZ(30, -20, 0);
        edge.ApplyTransform(Transform.CreateTranslation(shift));
        Assert.Equal(new XY(3, 4), edge.MajorAxisEndPoint);
        var after = edge.PolygonalVertexes(64);
        Assert.Equal(before.Count, after.Count);
        for (int i = 0; i < before.Count; i++) near(before[i] + shift, after[i]);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(0.4, 2.0)]
    public void PatternUpdateTransformsOffsetAsVector(double angle, double scale)
    {
        var pattern = new HatchPattern("custom");
        var line = new HatchPattern.Line
        {
            BasePoint = new XY(3, 4), Offset = new XY(-1, 2), Angle = 0.7,
            DashLengths = { 3, -1, 0 },
        };
        pattern.Lines.Add(line);
        pattern.Update(new XY(30, -20), angle, scale);
        double c = Math.Cos(angle), s = Math.Sin(angle);
        near(new XYZ(scale * (-c - 2 * s), scale * (-s + 2 * c), 0), (XYZ)line.Offset);
        near(new XYZ(30 + scale * (3 * c - 4 * s), -20 + scale * (3 * s + 4 * c), 0), (XYZ)line.BasePoint);
        Assert.Equal(0.7 + angle, line.Angle, 9);
        Assert.Equal(new[] { 3 * scale, -scale, 0 }, line.DashLengths.ToArray());
    }
}
