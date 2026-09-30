using ACadSharp.Entities;
using ACadSharp.IO.DWG;
using ACadSharp.Objects;
using ACadSharp.Tables;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;

namespace ACadSharp.Tests.IO;

public class McadReferenceAndDecompressionTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ReassigningLayerRemovesTheOldReferenceEvenAfterRename(bool rename)
	{
		CadDocument document = new();
		Layer previous = new("previous");
		Layer current = new("current");
		document.Layers.Add(previous);
		document.Layers.Add(current);
		Line line = new() { Layer = previous };
		document.Entities.Add(line);
		if (rename) previous.Name = "renamed";
		line.Layer = current;
		Assert.Empty(document.Layers.GetReferences(previous.Name));
		Assert.Same(line, Assert.Single(document.Layers.GetReferences(current.Name)));
		document.Layers.Remove(previous.Name);
		Assert.Same(current, line.Layer);
	}

	[Fact]
	public void ReassignedAndMovedEntityIsNotRetainedByItsOriginalDocument()
	{
		CadDocument original = new();
		Layer previous = new("previous");
		Layer current = new("current");
		original.Layers.Add(previous);
		original.Layers.Add(current);
		Line line = new() { Layer = previous };
		original.Entities.Add(line);
		line.Layer = current;
		original.Entities.Remove(line);
		Assert.Empty(original.Layers.GetReferences(previous.Name));
		Assert.Empty(original.Layers.GetReferences(current.Name));
		CadDocument destination = new();
		Layer destinationLayer = new(current.Name);
		destination.Layers.Add(destinationLayer);
		destination.Entities.Add(line);
		original.Layers.Remove(previous.Name);
		original.Layers.Remove(current.Name);
		Assert.Same(destinationLayer, line.Layer);
		destination.Layers.Remove(current.Name);
		Assert.Same(destination.Layers[Layer.DefaultName], line.Layer);
	}

	[Fact]
	public void ClearingNullableMaterialRemovesItsReference()
	{
		CadDocument document = new();
		Material material = new("material");
		document.Materials.Add(material);
		Line line = new();
		document.Entities.Add(line);
		line.Material = material;
		line.Material = null;
		Assert.Empty(document.Materials.GetReferences(material.Name));
	}

	[Fact]
	public void ClearedMaterialDoesNotAffectTheEntityAfterMovingDocuments()
	{
		CadDocument original = new();
		Material previous = new("previous");
		original.Materials.Add(previous);
		Line line = new();
		original.Entities.Add(line);
		line.Material = previous;
		line.Material = null;
		original.Entities.Remove(line);
		CadDocument destination = new();
		Material current = new("current");
		destination.Materials.Add(current);
		destination.Entities.Add(line);
		line.Material = current;
		original.Materials.Remove(previous.Name);
		Assert.Same(current, line.Material);
		Assert.Empty(original.Materials.GetReferences(previous.Name));
	}

	[Fact]
	public void SharedEntryKeepsIndependentPropertiesOnTheSameOwner()
	{
		CadDocument document = new();
		DimensionStyle style = new("references");
		document.DimensionStyles.Add(style);
		LineType shared = new("shared");
		LineType replacement = new("replacement");
		document.LineTypes.Add(shared);
		document.LineTypes.Add(replacement);
		style.LineType = shared;
		style.LineTypeExt1 = shared;
		style.LineTypeExt2 = shared;
		style.LineType = replacement;
		Assert.Same(style, Assert.Single(document.LineTypes.GetReferences(shared.Name)));
		document.LineTypes.Remove(shared.Name);
		Assert.Same(replacement, style.LineType);
		Assert.Null(style.LineTypeExt1);
		Assert.Null(style.LineTypeExt2);
	}

	[Fact]
	public void RemovalCallbacksCanRegisterReplacementReferences()
	{
		CadObjectReferenceHandler<string, Layer> handler = new();
		Line owner = new();
		Action<Layer> first = null;
		Action<Layer> second = null;
		first = value => handler.AddReference("first", owner, first);
		second = value => handler.AddReference("second", owner, second);
		handler.AddReference("removed", owner, first);
		handler.AddReference("removed", owner, second);
		handler.RemoveReference("removed", Layer.Default);
		Assert.Same(owner, Assert.Single(handler.GetReferences("first")));
		Assert.Same(owner, Assert.Single(handler.GetReferences("second")));
		handler.RemoveReference("first", owner);
		Assert.Empty(handler.GetReferences("first"));
		Assert.Same(owner, Assert.Single(handler.GetReferences("second")));
	}

	[Fact]
	public void RemovedReferenceDoesNotKeepItsOwnerAlive()
	{
		CadObjectReferenceHandler<string, Layer> handler = new();
		WeakReference owner = AddAndRemoveOwner(handler);
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		Assert.False(owner.IsAlive);
		GC.KeepAlive(handler);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference AddAndRemoveOwner(CadObjectReferenceHandler<string, Layer> handler)
	{
		Line owner = new();
		handler.AddReference("layer", owner, static (entity, value) => ((Line)entity).Layer = value);
		handler.RemoveReference("layer", owner);
		return new WeakReference(owner);
	}

	[Fact]
	public void LiteralCopyHandlesLegalShortReads()
	{
		using ShortReadStream compressed = new(new byte[] { 0x14, 1, 2, 3, 0x11 });
		using MemoryStream output = new();
		DwgLZ77AC18Decompressor.DecompressToDest(compressed, output);
		Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
	}

	[Fact]
	public void BackReferenceCopyHandlesLegalShortReads()
	{
		using MemoryStream compressed = new(new byte[] { 0x14, 1, 2, 3, 0x48, 0, 0x11 });
		using ShortReadStream output = new();
		DwgLZ77AC18Decompressor.DecompressToDest(compressed, output);
		Assert.Equal(new byte[] { 1, 2, 3, 1, 2, 3 }, output.ToArray());
	}

	[Fact]
	public void TruncatedLiteralFailsBeforeWritingPooledBytes()
	{
		using MemoryStream compressed = new(new byte[] { 0x14, 1, 2 });
		using MemoryStream output = new();
		Assert.Throws<EndOfStreamException>(() => DwgLZ77AC18Decompressor.DecompressToDest(compressed, output));
		Assert.Equal(0, output.Length);
	}

	[Fact]
	public void LiteralCopyCanGrowThePooledBuffer()
	{
		byte[] payload = Enumerable.Range(0, 40000).Select(i => (byte)i).ToArray();
		using MemoryStream encoded = new();
		encoded.WriteByte(0);
		for (int i = 0; i < 156; i++) encoded.WriteByte(0);
		encoded.WriteByte(202);
		encoded.Write(payload, 0, payload.Length);
		encoded.WriteByte(0x11);
		using ShortReadStream compressed = new(encoded.ToArray());
		using MemoryStream output = new();
		DwgLZ77AC18Decompressor.DecompressToDest(compressed, output);
		Assert.Equal(payload, output.ToArray());
	}

	private sealed class ShortReadStream : MemoryStream
	{
		public ShortReadStream() { }
		public ShortReadStream(byte[] bytes) : base(bytes) { }
		public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(1, count));
	}
}
