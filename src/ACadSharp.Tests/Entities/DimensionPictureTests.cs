using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using ACadSharp.XData;
using CSMath;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.Entities;

public class DimensionPictureTests
{
    private static DimensionAligned create(bool linear = false)
    {
        var dim = linear ? new DimensionLinear() : new DimensionAligned();
        dim.FirstPoint = new XYZ(100, 200, 0);
        dim.SecondPoint = new XYZ(110, 200, 0);
        dim.DefinitionPoint = new XYZ(110, 205, 0);
        dim.Block = new BlockRecord("*D1") { IsAnonymous = true };
        dim.Block.BlockEntity.BasePoint = new XYZ(1000, 2000, 0);
        dim.Block.Entities.Add(new Line(new XYZ(98, 200, 0), new XYZ(112, 210, 0)));
        dim.Block.Entities.Add(new Point(dim.FirstPoint));
        dim.Block.Entities.Add(new Point(dim.SecondPoint));
        dim.Block.Entities.Add(new Point(dim.DefinitionPoint));
        dim.CaptureImportedPicture();
        return dim;
    }

    [Theory]
    [InlineData(false, false, ACadVersion.AC1015)]
    [InlineData(false, false, ACadVersion.AC1021)]
    [InlineData(false, false, ACadVersion.AC1032)]
    [InlineData(false, true, ACadVersion.AC1032)]
    [InlineData(true, false, ACadVersion.AC1032)]
    [InlineData(true, true, ACadVersion.AC1032)]
    public void ImportedPictureSurvivesRoundTripInWorldCoordinates(bool linear, bool dxf, ACadVersion version)
    {
        var dim = create(linear);
        dim.Normal = -XYZ.AxisZ;
        dim.InsertionPoint = new XYZ(3000, 4000, 0);
        var document = new CadDocument();
        document.Header.Version = version;
        document.Entities.Add(dim);
        using var stream = new MemoryStream();
        if (dxf) DxfWriter.Write(stream, document); else DwgWriter.Write(stream, document);
        var back = dxf ? DxfReader.Read(new MemoryStream(stream.ToArray())) : DwgReader.Read(new MemoryStream(stream.ToArray()));
        var restored = Assert.Single(back.Entities.OfType<DimensionAligned>());
        Assert.Equal(new XYZ(98, 200, 0), restored.GetBoundingBox().Min);
        Assert.Equal(new XYZ(112, 210, 0), restored.GetBoundingBox().Max);
        Assert.False(restored.PictureNeedsUpdate);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    [InlineData(12)] [InlineData(13)] [InlineData(14)]
    public void GeometryAndStyleEditsRejectOldPicture(int mutation)
    {
        var dim = create(true);
        switch (mutation)
        {
            case 0: dim.FirstPoint += XYZ.AxisX; break;
            case 1: dim.SecondPoint += XYZ.AxisX; break;
            case 2: dim.DefinitionPoint += XYZ.AxisY; break;
            case 3: dim.Text = "Edited"; break;
            case 4: dim.TextMiddlePoint += XYZ.AxisY; break;
            case 5: dim.Normal = -XYZ.AxisZ; break;
            case 6: dim.Style.TextHeight *= 2; break;
            case 7: dim.Style.ArrowSize *= 2; break;
            case 8: dim.Style.Style.Filename = "other.shx"; break;
            case 9: dim.Style.Style.Width *= 2; break;
            case 10: ((DimensionLinear)dim).Rotation = 0.3; break;
            case 11: dim.ExtLineRotation = 0.2; break;
            case 12: dim.Flags |= DimensionType.TextUserDefinedLocation; break;
            case 13: dim.Block = new BlockRecord("*D2") { IsAnonymous = true }; break;
            case 14: dim.Style = new DimensionStyle("Other"); break;
        }
        Assert.True(dim.PictureNeedsUpdate);
        Assert.Equal(new BoundingBox(dim.FirstPoint, dim.SecondPoint).Min, dim.GetBoundingBox().Min);
        Assert.Equal(new BoundingBox(dim.FirstPoint, dim.SecondPoint).Max, dim.GetBoundingBox().Max);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TransformAndCloneDoNotReuseStaleSnapshot(bool linear)
    {
        var dim = create(linear);
        var clone = (DimensionAligned)dim.Clone();
        clone.ApplyTransform(Transform.CreateTranslation(new XYZ(500, 600, 0)));
		Assert.True(clone.PictureNeedsUpdate);
        Assert.Equal(new XYZ(600, 800, 0), clone.GetBoundingBox().Min);
        dim.ApplyTransform(Transform.CreateTranslation(new XYZ(500, 600, 0)));
        Assert.True(dim.PictureNeedsUpdate);
        Assert.Equal(new XYZ(600, 800, 0), dim.GetBoundingBox().Min);
    }

    [Fact]
    public void MutableOverridesInvalidatePictureIncludingBinaryValues()
    {
        var dim = create();
        var record = new ExtendedDataReal(2);
        var bytes = new byte[] { 1, 2 };
        dim.ExtendedData.Add(AppId.DefaultName, new ExtendedData(new ExtendedDataRecord[] { record, new ExtendedDataBinaryChunk(bytes) }));
        dim.CaptureImportedPicture();
        record.Value = 3;
        Assert.True(dim.PictureNeedsUpdate);
        record.Value = 2;
        Assert.False(dim.PictureNeedsUpdate);
        bytes[0] = 4;
        Assert.True(dim.PictureNeedsUpdate);
    }

    [Fact]
    public void MutableArrowGeometryInvalidatesPicture()
    {
        var dim = create();
        var arrow = new BlockRecord("Arrow");
        var line = new Line(XYZ.Zero, XYZ.AxisX);
        arrow.Entities.Add(line);
        dim.Style.ArrowBlock = arrow;
        dim.CaptureImportedPicture();
        Assert.False(dim.PictureNeedsUpdate);
        line.EndPoint = 3 * XYZ.AxisX;
        Assert.True(dim.PictureNeedsUpdate);
        Assert.Equal(dim.FirstPoint, dim.GetBoundingBox().Min);
    }

    [Fact]
    public void MutableArrowBasePointInvalidatesPicture()
    {
        var dim = create();
        var arrow = new BlockRecord("Arrow");
        arrow.Entities.Add(new Line(XYZ.Zero, XYZ.AxisX));
        dim.Style.ArrowBlock = arrow;
        dim.CaptureImportedPicture();
        arrow.BlockEntity.BasePoint = XYZ.AxisY;
        Assert.True(dim.PictureNeedsUpdate);
        Assert.Equal(dim.FirstPoint, dim.GetBoundingBox().Min);
    }

    [Fact]
    public void CloneWithDroppedStyleOverridesDoesNotTrustOriginalPicture()
    {
        var dim = create();
        var overridden = (DimensionStyle)dim.Style.Clone();
        overridden.ArrowSize *= 2;
        dim.SetDimensionOverride(overridden);
        dim.CaptureImportedPicture();
        Assert.True(dim.HasStyleOverride);
        var clone = (DimensionAligned)dim.Clone();
        Assert.False(clone.HasStyleOverride);
        Assert.True(clone.PictureNeedsUpdate);
        Assert.Equal(clone.FirstPoint, clone.GetBoundingBox().Min);
    }

    [Fact]
    public void AttachingToDifferentSameNameStyleMarksPictureDirty()
    {
        var dim = create();
        var document = new CadDocument();
        document.DimensionStyles[dim.Style.Name].TextHeight *= 2;
        document.Entities.Add(dim);
        Assert.True(dim.PictureNeedsUpdate);
        Assert.Equal(dim.FirstPoint, dim.GetBoundingBox().Min);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RemovingThenEditingDimensionRetainsDirtyTracking(bool editStyle)
    {
        var dim = create();
        var document = new CadDocument();
        document.Entities.Add(dim);
        dim.CaptureImportedPicture();
        document.Entities.Remove(dim);
        Assert.False(dim.PictureNeedsUpdate);
        if (editStyle) dim.Style.TextHeight *= 2; else dim.FirstPoint += XYZ.AxisX;
        Assert.True(dim.PictureNeedsUpdate);
        document.Entities.Add(dim);
        Assert.True(dim.PictureNeedsUpdate);
    }

    [Fact]
    public void AttachingWithoutReplacingReferencesKeepsEditTracking()
    {
        var dim = create();
        var document = new CadDocument();
        dim.Style = document.DimensionStyles[dim.Style.Name];
        document.BlockRecords.Add(dim.Block);
        dim.CaptureImportedPicture();
        document.Entities.Add(dim);
        Assert.False(dim.PictureNeedsUpdate);
        dim.FirstPoint += XYZ.AxisX;
        Assert.True(dim.PictureNeedsUpdate);
    }

    [Fact]
    public void RebuildWithNativeStyleOverridesDoesNotClaimToApplyThem()
    {
        var dim = create();
        var overridden = (DimensionStyle)dim.Style.Clone();
        overridden.ArrowSize *= 2;
        dim.SetDimensionOverride(overridden);
        dim.CaptureImportedPicture();
        Assert.False(dim.PictureNeedsUpdate);
        dim.UpdateBlock();
        Assert.True(dim.PictureNeedsUpdate);
    }

    private class UnsupportedPictureLine : Line
    {
        public override BoundingBox GetBoundingBox() => throw new System.NotSupportedException("Unsupported picture component");
    }

    [Fact]
    public void UnsupportedPictureComponentDoesNotMakeDimensionBoundsThrow()
    {
        var dim = create();
        dim.Block.Entities.Add(new UnsupportedPictureLine());
        Assert.Equal(dim.FirstPoint, dim.GetBoundingBox().Min);
        Assert.False(dim.PictureNeedsUpdate);
    }

    [Fact]
    public void DirectPictureEditsAreReflectedWithoutCachingItsBox()
    {
        var dim = create();
        ((Line)dim.Block.Entities.First()).StartPoint = new XYZ(90, 190, 0);
        Assert.Equal(new XYZ(90, 190, 0), dim.GetBoundingBox().Min);
    }

    [Fact]
    public void NestedDimensionRetainsLocalGeometryFallback()
    {
        var dim = create();
        var document = new CadDocument();
        var owner = new BlockRecord("Nested");
        document.BlockRecords.Add(owner);
        owner.Entities.Add(dim);
        dim.CaptureImportedPicture();
        Assert.Equal(dim.FirstPoint, dim.GetBoundingBox().Min);
        Assert.Equal(dim.SecondPoint, dim.GetBoundingBox().Max);
    }

    [Fact]
    public void AlreadyStaleNativePictureIsNotTrustedOnImport()
    {
        var dim = create();
        foreach (var entity in dim.Block.Entities)
            entity.ApplyTransform(Transform.CreateTranslation(new XYZ(-200000, -170000, 0)));
        dim.CaptureImportedPicture();
        Assert.Equal(dim.FirstPoint, dim.GetBoundingBox().Min);
        Assert.Equal(dim.SecondPoint, dim.GetBoundingBox().Max);
    }

    [Fact]
    public void PictureWithoutNativeReferencePointsUsesGeometryFallback()
    {
        var dim = create();
        foreach (var point in dim.Block.Entities.OfType<Point>().ToArray()) dim.Block.Entities.Remove(point);
        dim.CaptureImportedPicture();
        Assert.Equal(dim.FirstPoint, dim.GetBoundingBox().Min);
    }

    [Theory]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(double.PositiveInfinity, 2)]
    [InlineData(double.NaN, 0)]
    public void NonFiniteDimensionReferenceCannotValidateFinitePicture(double value, int axis)
    {
        var dim = create();
        dim.FirstPoint = axis switch
        {
            0 => new XYZ(value, 200, 0),
            1 => new XYZ(100, value, 0),
            _ => new XYZ(100, 200, value),
        };
        dim.CaptureImportedPicture();
        Assert.False(dim.TryGetImportedPictureBoundingBox(out _));
    }

    [Fact]
    public void MovingNativeReferencePointInvalidatesCapturedPicture()
    {
        var dim = create();
        var point = dim.Block.Entities.OfType<Point>().First();
        var original = point.Location;
        point.Location += XYZ.AxisY;
        Assert.True(dim.PictureNeedsUpdate);
        Assert.Equal(dim.FirstPoint, dim.GetBoundingBox().Min);
        point.Location = original;
        Assert.False(dim.PictureNeedsUpdate);
        Assert.Equal(new XYZ(98, 200, 0), dim.GetBoundingBox().Min);
    }

    [Fact]
    public void NativeDimensionPictureIncludesExtensionAndArrowGeometry()
    {
        var file = Path.Combine(TestVariables.SamplesFolder, "regressions", "dimassoc-apparent-intersection-AC1021.dwg");
        var document = DwgReader.Read(file);
        var dim = Assert.Single(document.Entities.OfType<DimensionAligned>());
        Assert.Equal(-3.25, dim.GetBoundingBox().Min.Y, 8);
        Assert.Equal(dim.Block.GetBoundingBox().Min, dim.GetBoundingBox().Min);
        Assert.Equal(dim.Block.GetBoundingBox().Max, dim.GetBoundingBox().Max);
    }

    [Fact]
    public void CyclicAndNonFinitePicturesFallBack()
    {
        var dim = create();
        dim.Block.Entities.Add(new Insert(dim.Block));
        Assert.Equal(dim.FirstPoint, dim.GetBoundingBox().Min);
        dim.Block.Entities.Clear();
        dim.Block.Entities.Add(new Line(new XYZ(double.NaN, 0, 0), XYZ.AxisX));
        Assert.Equal(dim.FirstPoint, dim.GetBoundingBox().Min);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExplicitRebuildUsesNewPictureBounds(bool linear)
    {
        var dim = create(linear);
        dim.FirstPoint = new XYZ(500, 600, 0);
        dim.SecondPoint = new XYZ(510, 600, 0);
        dim.DefinitionPoint = new XYZ(510, 605, 0);
        Assert.True(dim.PictureNeedsUpdate);
        dim.UpdateBlock();
        Assert.False(dim.PictureNeedsUpdate);
        Assert.True(dim.GetBoundingBox().Max.Y > 600);
        Assert.True(dim.GetBoundingBox().Min.X > 490);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void WriterReportsDirtyPictureUnlessExplicitUpdateIsEnabled(bool dxf, bool update)
    {
        var document = new CadDocument();
        var dim = create(true);
        document.Entities.Add(dim);
        dim.CaptureImportedPicture();
        dim.SecondPoint += XYZ.AxisX;
        using var stream = new MemoryStream();
        var notes = new List<string>();
        if (dxf)
        {
            using var writer = new DxfWriter(stream, document);
            writer.Configuration.UpdateDimensionsInModel = update;
            writer.OnNotification += (_, e) => notes.Add(e.Message);
            writer.Write();
        }
        else
        {
            using var writer = new DwgWriter(stream, document);
            writer.Configuration.UpdateDimensionsInModel = update;
            writer.OnNotification += (_, e) => notes.Add(e.Message);
            writer.Write();
        }
        Assert.Equal(!update, notes.Any(n => n.Contains("outdated picture block")));
    }
}
