using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.IO.DWG;
using ACadSharp.Objects;
using ACadSharp.Tables;
using ACadSharp.Text;
using CSMath;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using Xunit;

namespace ACadSharp.Tests.IO;

public class Review9RegressionTests
{
    [Theory]
    [InlineData(@"\\U+0041", @"\\U+0041")]
    [InlineData(@"\U+0041", "A")]
    [InlineData(@"\\\U+0041", @"\\A")]
    [InlineData(@"\\\\U+0041", @"\\\\U+0041")]
    [InlineData(@"\U+GGGG", @"\U+GGGG")]
    [InlineData(@"\U+005EJ", "^J")]
    public void MTextUnicodeRespectsEscapedSlashesAndChunkBoundaries(string value, string expected)
    {
        var doc = new CadDocument();
        doc.Entities.Add(new MText { Value = value });
        using var output = new MemoryStream();
        DxfWriter.Write(output, doc);
        string dxf = Encoding.UTF8.GetString(output.ToArray());
        foreach (int split in Enumerable.Range(1, value.Length - 1))
        {
            // Split the entity's text over group 3/1 without changing other data.
            string original = "  1" + Environment.NewLine + value + Environment.NewLine;
            string chunked = dxf.Replace(original, "  3" + Environment.NewLine + value.Substring(0, split) + Environment.NewLine + "  1" + Environment.NewLine + value.Substring(split) + Environment.NewLine);
            Assert.NotEqual(dxf, chunked);
            var back = DxfReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(chunked)));
            Assert.Equal(expected, Assert.Single(back.Entities.OfType<MText>()).Value);
        }
    }

    [Theory]
    [InlineData(@"\A")]
    [InlineData(@"prefix\C1")]
    [InlineData(@"prefix\H2x")]
    [InlineData(@"prefix\pxqc")]
    [InlineData(@"prefix\fArial")]
    public void MalformedMTextCommandsRemainLiteral(string value)
    {
        Assert.Equal(value, TextProcessor.Parse(value, out _));
        Assert.Equal(value, TextProcessor.Unescape(value));
    }

    [Fact]
    public void WriterChunkBoundaryKeepsLiteralUnicodeEscape()
    {
        string value = new string('x', 249) + @"\\U+0041";
        var doc = new CadDocument(); doc.Entities.Add(new MText { Value = value });
        using var bytes = new MemoryStream(); DxfWriter.Write(bytes, doc);
        var back = DxfReader.Read(new MemoryStream(bytes.ToArray()));
        var text = Assert.Single(back.Entities.OfType<MText>());
        Assert.Equal(value, text.Value);
        Assert.Equal(new string('x', 249) + @"\U+0041", text.PlainText);
    }

    [Theory]
    [InlineData(new byte[] { 0x14, 1, 2, 3, 0x48, 1, 0x11 })]
    [InlineData(new byte[] { 0x14, 1, 2, 3, 0x12, 0, 0, 0x11 })]
    public void BackReferencesCannotReadBeforeOutput(byte[] bytes)
    {
        using var input = new MemoryStream(bytes); using var output = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => DwgLZ77AC18Decompressor.DecompressToDest(input, output));
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
    }

    [Fact]
    public void OverlappingBackReferenceRepeatsOnlyValidOutputBytes()
    {
        using var input = new MemoryStream(new byte[] { 0x14, 1, 2, 3, 0x40, 0, 0x11 });
        using var output = new MemoryStream();
        DwgLZ77AC18Decompressor.DecompressToDest(input, output);
        Assert.Equal(new byte[] { 1, 2, 3, 3, 3, 3 }, output.ToArray());
    }

    [Fact]
    public void BackReferenceCannotReadFromPreviousPage()
    {
        using var input = new MemoryStream(new byte[] { 0x14, 1, 2, 3, 0x48, 1, 0x11 });
        using var output = new MemoryStream(); output.Write(new byte[] { 7, 8, 9 }, 0, 3);
        Assert.Throws<InvalidDataException>(() => DwgLZ77AC18Decompressor.DecompressToDest(input, output));
        Assert.Equal(new byte[] { 7, 8, 9, 1, 2, 3 }, output.ToArray());
    }

    [Fact]
    public void DetachAfterRawDictionaryRemovalDoesNotRetainOwner()
    {
        var doc = new CadDocument();
        var owner = AddAndDetachAfterRawRemoval(doc);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(owner.IsAlive);
        GC.KeepAlive(doc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddAndDetachAfterRawRemoval(CadDocument doc)
    {
        var material = new Material("removed"); doc.Materials.Add(material);
        var line = new Line { Material = material }; doc.Entities.Add(line);
        doc.RootDictionary.GetEntry<CadDictionary>(CadDictionary.AcadMaterial).Remove(material.Name);
        doc.Entities.Remove(line);
        return new WeakReference(line);
    }

    [Theory]
    [InlineData(new byte[] { 0x14, 1, 2, 3, 0x21 })]
    [InlineData(new byte[] { 0x14, 1, 2, 3, 0x21, 0 })]
    [InlineData(new byte[] { 0x14, 1, 2, 3 })]
    [InlineData(new byte[] { })]
    public void TruncatedOpcodesRejectIncompleteData(byte[] data)
    {
        using var source = new MemoryStream(data);
        using var destination = new ZeroWriteGuard();
        Assert.Throws<EndOfStreamException>(() => DwgLZ77AC18Decompressor.DecompressToDest(source, destination));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DimensionStyleReassignmentAndDetachDropOldReferences(bool clear)
    {
        var doc = new CadDocument();
        var a = new LineType("A"); var b = new LineType("B");
        doc.LineTypes.Add(a); doc.LineTypes.Add(b);
        var style = new DimensionStyle("probe") { LineType = a, LineTypeExt1 = a, LineTypeExt2 = a };
        doc.DimensionStyles.Add(style);
        style.LineType = clear ? null : b;
        style.LineTypeExt1 = null; style.LineTypeExt2 = null;
        Assert.Empty(doc.LineTypes.GetReferences("A"));
        doc.LineTypes.Remove("A");
        Assert.Same(clear ? null : b, style.LineType);
        doc.DimensionStyles.Remove(style.Name);
        Assert.DoesNotContain(style, doc.TextStyles.GetReferences(style.Style.Name));
        Assert.Empty(doc.LineTypes.GetReferences("B"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaterialRenameRemovalAndClearResetOwners(bool clear)
    {
        var doc = new CadDocument();
        var material = new Material("before"); doc.Materials.Add(material);
        var line = new Line { Material = material }; doc.Entities.Add(line);
        doc.UpdateCollections(false, false);
        material.Name = "after";
        Assert.Empty(doc.Materials.GetReferences("before"));
        Assert.Same(line, Assert.Single(doc.Materials.GetReferences("after")));
        if (clear) doc.Materials.Clear(); else doc.Materials.Remove("after");
        Assert.Null(line.Material);
        Assert.Empty(doc.Materials.GetReferences("after"));
    }

    [Fact]
    public void MaterialReferencesAcceptNameAndDictionaryAlias()
    {
        var doc = new CadDocument();
        var material = new Material("display"); doc.Materials.Add(material);
        var line = new Line { Material = material }; doc.Entities.Add(line);
        var dictionary = doc.RootDictionary.GetEntry<CadDictionary>(CadDictionary.AcadMaterial);
        dictionary.Remove("display"); dictionary.Add("alias", material);
        Assert.Same(line, Assert.Single(doc.Materials.GetReferences("display")));
        Assert.Same(line, Assert.Single(doc.Materials.GetReferences("alias")));
        doc.Materials.Remove("alias");
        Assert.Null(line.Material);
        Assert.Empty(doc.Materials.GetReferences("display"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExistingVerticesFollowOwnerLineTypeOnlyWhenRequested(bool registered, bool match)
    {
        var polyline = new Polyline3D(new[] { XYZ.Zero, XYZ.AxisX });
        polyline.MatchVerticesEntityProperties = match;
        var doc = new CadDocument(); if (registered) doc.Entities.Add(polyline);
        polyline.LineType = new LineType("dash");
        Assert.All(polyline.Vertices, v => Assert.Equal(match ? "dash" : LineType.ByLayerName, v.LineType.Name));
    }

    private sealed class ZeroWriteGuard : MemoryStream
    {
        private int zeroWrites;
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count == 0 && ++this.zeroWrites == 4) throw new InvalidOperationException("Decoder did not advance.");
            base.Write(buffer, offset, count);
        }
    }
}
