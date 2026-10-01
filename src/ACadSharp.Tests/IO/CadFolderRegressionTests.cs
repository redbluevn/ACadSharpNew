using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO;

public class CadFolderRegressionTests
{
    [Theory]
    [InlineData(false, ACadVersion.AC1015, 1e-6)]
    [InlineData(false, ACadVersion.AC1015, 1)]
    [InlineData(false, ACadVersion.AC1015, 1e6)]
    [InlineData(false, ACadVersion.AC1032, 1e-6)]
    [InlineData(false, ACadVersion.AC1032, 1)]
    [InlineData(false, ACadVersion.AC1032, 1e6)]
    [InlineData(true, ACadVersion.AC1032, 1e-6)]
    [InlineData(true, ACadVersion.AC1032, 1)]
    [InlineData(true, ACadVersion.AC1032, 1e6)]
    public void AlmostPerpendicularEllipseSurvivesRoundTrip(bool dxf, ACadVersion version, double scale)
    {
        var document = new CadDocument();
        document.Header.Version = version;
        var ellipse = new Ellipse { Normal = new XYZ(1e-14, 0, 1), MajorAxisEndPoint = new XYZ(scale, 0, 0), RadiusRatio = 0.5 };
        document.Entities.Add(ellipse);
        using var output = new MemoryStream();
        if (dxf) DxfWriter.Write(output, document); else DwgWriter.Write(output, document);
        var back = dxf ? DxfReader.Read(new MemoryStream(output.ToArray())) : DwgReader.Read(new MemoryStream(output.ToArray()));
        var actual = Assert.Single(back.Entities.OfType<Ellipse>());
        Assert.Equal(ellipse.MajorAxisEndPoint.X, actual.MajorAxisEndPoint.X, 10);
        Assert.Equal(ellipse.RadiusRatio, actual.RadiusRatio);
    }

    [Theory]
    [InlineData(1e-6)]
    [InlineData(1)]
    [InlineData(1e6)]
    public void ObliqueEllipseIsRejectedAtEveryScale(double scale)
    {
        var ellipse = new Ellipse { Normal = new XYZ(1e-4, 0, 1), MajorAxisEndPoint = new XYZ(scale, 0, 0) };
        Assert.False(ellipse.IsValid(CadFileFormat.DWG, ACadVersion.AC1032));
    }

    [Fact]
    public void SplineUsesActiveKnotDomainAndEvaluatesItsEndpoint()
    {
        var spline = new Spline { Degree = 3 };
        spline.ControlPoints.AddRange(new[] { new XYZ(100, 200, 0), new XYZ(110, 220, 0), new XYZ(120, 220, 0), new XYZ(130, 200, 0) });
        //Imported outer knots can differ slightly from the active, clamped domain.
        spline.Knots.AddRange(new[] { 19.999999999, 20d, 20d, 20d, 30d, 30d, 30d, 30d });
        Assert.Equal(100, spline.PointOnSpline(0).X, 8);
        Assert.Equal(130, spline.PointOnSpline(1).X, 8);
        Assert.All(spline.PolygonalVertexes(32), point => Assert.InRange(point.X, 100, 130));
        Assert.Equal(100, spline.GetBoundingBox().Min.X, 8);
    }

    [Fact]
    public void UnclampedSplineEndpointIsEvaluatedInsteadOfUsingLastControlPoint()
    {
        var spline = new Spline { Degree = 2 };
        spline.ControlPoints.AddRange(new[] { new XYZ(100, 200, 0), new XYZ(110, 220, 0), new XYZ(120, 220, 0), new XYZ(130, 200, 0) });
        spline.Knots.AddRange(new[] { 0d, 1d, 2d, 3d, 4d, 5d, 6d });
        Assert.Equal(105, spline.PointOnSpline(0).X, 8);
        Assert.Equal(125, spline.PointOnSpline(1).X, 8);
        Assert.Equal(125, spline.PolygonalVertexes(32).Last().X, 8);
    }

    [Theory]
    [InlineData(1, 100, 210)]
    [InlineData(2, 90, 210)]
    [InlineData(3, 80, 210)]
    [InlineData(4, 100, 215)]
    [InlineData(5, 90, 215)]
    [InlineData(6, 80, 215)]
    [InlineData(7, 100, 220)]
    [InlineData(8, 90, 220)]
    [InlineData(9, 80, 220)]
    public void ImportedMTextExtentsRespectAttachment(int attachment, double minX, double maxY)
    {
        var text = new MText { InsertPoint = new XYZ(100, 210, 0), AttachmentPoint = (AttachmentPointType)attachment, Value = "Measured text" };
        text.SetMeasuredExtents(20, 10);
        var box = text.GetBoundingBox();
        Assert.Equal(minX, box.Min.X, 8);
        Assert.Equal(maxY - 10, box.Min.Y, 8);
        Assert.Equal(minX + 20, box.Max.X, 8);
        Assert.Equal(maxY, box.Max.Y, 8);
    }

    [Theory]
    [InlineData(false, ACadVersion.AC1015)]
    [InlineData(false, ACadVersion.AC1021)]
    [InlineData(false, ACadVersion.AC1032)]
    [InlineData(true, ACadVersion.AC1032)]
    public void ImportedMTextMeasurementsSurviveRoundTrip(bool dxf, ACadVersion version)
    {
        var document = new CadDocument();
        document.Header.Version = version;
        var text = new MText { InsertPoint = new XYZ(100, 210, 0), Value = "Measured text", RectangleWidth = 30 };
        document.Entities.Add(text);
        text.SetMeasuredExtents(20, 10);
        using var output = new MemoryStream();
        if (dxf) DxfWriter.Write(output, document); else DwgWriter.Write(output, document);
        var back = dxf ? DxfReader.Read(new MemoryStream(output.ToArray())) : DwgReader.Read(new MemoryStream(output.ToArray()));
        var actual = Assert.Single(back.Entities.OfType<MText>());
        Assert.Equal(text.GetBoundingBox(), actual.GetBoundingBox());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void EditingMTextInvalidatesImportedMeasurements(int change)
    {
        var text = new MText { Value = "Before", InsertPoint = new XYZ(100, 200, 0) };
        text.Style = (ACadSharp.Tables.TextStyle)text.Style.Clone();
        text.SetMeasuredExtents(20, 10);
        if (change == 0) text.Value = "After";
        if (change == 1) text.Height *= 2;
        if (change == 2) text.Style.Width *= 2;
        if (change == 3) text.Style.Filename = "different.shx";
        if (change == 4) text.RectangleHeight = 20;
        if (change == 5) text.HorizontalWidth = 40;
        if (change == 6) text.VerticalHeight = 40;
        Assert.False(text.TryGetMeasuredExtents(out _, out _));
        Assert.Equal(new BoundingBox(text.InsertPoint), text.GetBoundingBox());
    }

    [Fact]
    public void ImportedMTextExtentsUseWorldAxesAndClonePreservesMeasurements()
    {
        var text = new MText { InsertPoint = new XYZ(100, 200, 0), AlignmentPoint = XYZ.AxisY, Normal = -XYZ.AxisZ, Value = "Measured" };
        text.SetMeasuredExtents(20, 10);
        var box = ((MText)text.Clone()).GetBoundingBox();
        Assert.Equal(new XYZ(90, 200, 0), box.Min);
        Assert.Equal(new XYZ(100, 220, 0), box.Max);
        Assert.False(new MText().TryGetMeasuredExtents(out _, out _));
    }

    [Fact]
    public void ImportedMTextWithParallelAxesKeepsAFinitePointFallback()
    {
        var text = new MText { AlignmentPoint = XYZ.AxisZ, Normal = XYZ.AxisZ, InsertPoint = new XYZ(100, 200, 0) };
        text.SetMeasuredExtents(20, 10);
        Assert.Equal(new BoundingBox(text.InsertPoint), text.GetBoundingBox());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedMTextColumnMeasurementsArePreservedButInvalidatedByLayoutEdits(bool dxf)
    {
        var doc = new CadDocument();
        doc.Header.Version = ACadVersion.AC1032;
        var text = new MText { Value = "Columns" };
        doc.Entities.Add(text);
        text.ColumnData.ColumnType = ColumnType.StaticColumns;
        text.ColumnData.ColumnCount = 2;
        text.ColumnData.Width = 10;
        text.ColumnData.Heights.AddRange(new[] { 10d, 10d });
        text.SetMeasuredExtents(22, 10);
        using var bytes = new MemoryStream();
        if (dxf) DxfWriter.Write(bytes, doc); else DwgWriter.Write(bytes, doc);
        var back = dxf ? DxfReader.Read(new MemoryStream(bytes.ToArray())) : DwgReader.Read(new MemoryStream(bytes.ToArray()));
        var actual = Assert.Single(back.Entities.OfType<MText>());
        Assert.True(actual.TryGetMeasuredExtents(out var width, out var height));
        Assert.Equal(22, width);
        Assert.Equal(10, height);
        actual.ColumnData.Width += 1;
        text.ColumnData.Heights[0] = 20;
        Assert.False(text.TryGetMeasuredExtents(out _, out _));
        Assert.False(actual.TryGetMeasuredExtents(out _, out _));
    }

    [Fact]
    public void TransformingImportedMTextInvalidatesTheLayoutCache()
    {
        var text = new MText { Value = "Measured" };
        text.SetMeasuredExtents(20, 10);
        text.ApplyTransform(Transform.CreateRotation(XYZ.AxisZ, System.Math.PI / 2));
        Assert.False(text.TryGetMeasuredExtents(out _, out _));
    }

    [Theory]
    [InlineData(false, ACadVersion.AC1015)]
    [InlineData(false, ACadVersion.AC1021)]
    [InlineData(false, ACadVersion.AC1024)]
    [InlineData(true, ACadVersion.AC1024)]
    public void LegacyFormatsDoNotReuseMeasurementsOfAnOmittedColumnLayout(bool dxf, ACadVersion version)
    {
        var doc = new CadDocument();
        doc.Header.Version = version;
        var text = new MText { Value = "Columns", InsertPoint = new XYZ(100, 200, 0) };
        doc.Entities.Add(text);
        text.ColumnData.ColumnType = ColumnType.StaticColumns;
        text.ColumnData.ColumnCount = 2;
        text.ColumnData.Width = 10;
        text.SetMeasuredExtents(22, 10);
        using var bytes = new MemoryStream();
        if (dxf) DxfWriter.Write(bytes, doc); else DwgWriter.Write(bytes, doc);
        var back = dxf ? DxfReader.Read(new MemoryStream(bytes.ToArray())) : DwgReader.Read(new MemoryStream(bytes.ToArray()));
        var actual = Assert.Single(back.Entities.OfType<MText>());
        Assert.False(actual.HasColumns);
        Assert.False(actual.TryGetMeasuredExtents(out _, out _));
        Assert.Equal(text.GetBoundingBox(), actual.GetBoundingBox());
    }
}
