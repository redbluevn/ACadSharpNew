using ACadSharp.Attributes;
using ACadSharp.Tables;
using ACadSharp.XData;
using CSMath;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ACadSharp.Entities;

//An imported dimension picture is already in WCS. It is not an INSERT and must not
//be translated by DefinitionPoint or the block's base point a second time.
internal sealed class DimensionPicture
{
	private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _properties = new();
	private readonly State _dimension;
	private readonly StyleState _style;
	private readonly BlockRecord _block;
	private readonly (DxfCode Code, object Value)[] _overrides;
	private readonly XYZ[] _referencePoints;
	internal bool IsUsable => this._style.IsUsable && this.referencePointsMatch();

	internal DimensionPicture(Dimension dimension, Dictionary<DimensionStyle, StyleState> styles)
	{
		this._dimension = new State(dimension);
		if (styles == null || !styles.TryGetValue(dimension.Style, out this._style))
		{
			this._style = new StyleState(dimension.Style);
			styles?.Add(dimension.Style, this._style);
		}
		this._block = dimension.Block;
		var aligned = (DimensionAligned)dimension;
		this._referencePoints = new[] { aligned.FirstPoint, aligned.SecondPoint, aligned.DefinitionPoint };
		this._overrides = getOverrides(dimension).Select(r => (r.Code, copyValue(r.RawValue))).ToArray();
	}

	internal bool TryGetBoundingBox(Dimension dimension, out BoundingBox box)
	{
		box = BoundingBox.Null;
		if (!this.Matches(dimension))
			return false;
		//Read the live picture, so direct edits to its entities are reflected too.
		return tryBlockBox(this._block, out box);
	}

	internal bool Matches(Dimension dimension)
	{
		if (!ReferenceEquals(this._block, dimension.Block) || !this._dimension.Matches(dimension)
			|| !this._style.Matches(dimension.Style))
			return false;

		int i = 0;
		foreach (ExtendedDataRecord record in getOverrides(dimension))
		{
			if (i == this._overrides.Length || record.Code != this._overrides[i].Code
				|| !equalValue(record.RawValue, this._overrides[i].Value))
				return false;
			i++;
		}
		return i == this._overrides.Length && this.referencePointsMatch();
	}

	private bool referencePointsMatch()
	{
		//A file can already contain a stale anonymous picture, for example after MOVE.
		//Native reference POINTs identify the coordinate frame without distance/size heuristics.
		foreach (XYZ expected in this._referencePoints)
		{
			//An infinite reference would also make tolerance infinite, accepting an unrelated point.
			if (!finite(expected)) return false;
			double tolerance = 1e-8 + 1e-12 * Math.Max(Math.Abs(expected.X), Math.Max(Math.Abs(expected.Y), Math.Abs(expected.Z)));
			bool found = false;
			foreach (Entity entity in this._block.Entities)
			{
				if (entity is Point point && Math.Abs(point.Location.X - expected.X) <= tolerance
					&& Math.Abs(point.Location.Y - expected.Y) <= tolerance && Math.Abs(point.Location.Z - expected.Z) <= tolerance)
				{
					found = true;
					break;
				}
			}
			if (!found) return false;
		}
		return true;
	}

	private static bool finite(XYZ p) => !double.IsNaN(p.X) && !double.IsInfinity(p.X)
		&& !double.IsNaN(p.Y) && !double.IsInfinity(p.Y) && !double.IsNaN(p.Z) && !double.IsInfinity(p.Z);

	private static bool tryBlockBox(BlockRecord block, out BoundingBox box)
	{
		box = BoundingBox.Null;
		try
		{
			if (!isAcyclic(block, new HashSet<BlockRecord>())) return false;
			box = block.GetBoundingBox();
			return (box.Extent == BoundingBoxExtent.Finite || box.Extent == BoundingBoxExtent.Point)
				&& finite(box.Min) && finite(box.Max);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			//An unsupported picture component must not make a formerly usable dimension fail.
			return false;
		}
	}

	internal sealed class StyleState
	{
		private readonly State _style, _text;
		private readonly (BlockRecord Block, bool Valid, BoundingBox Box, XYZ BasePoint)[] _arrows;
		internal bool IsUsable => this._arrows.All(a => a.Valid);

		internal StyleState(DimensionStyle style)
		{
			this._style = new State(style);
			this._text = new State(style.Style);
			this._arrows = (style.SeparateArrowBlocks ? new[] { style.DimArrow1, style.DimArrow2 } : new[] { style.ArrowBlock })
				.Where(b => b != null).Distinct().Select(b =>
				{
					bool valid = tryBlockBox(b, out BoundingBox box);
					return (b, valid, box, b.BlockEntity.BasePoint);
				}).ToArray();
		}

		internal bool Matches(DimensionStyle style)
		{
			if (!this._style.Matches(style) || !this._text.Matches(style.Style)) return false;
			foreach (var arrow in this._arrows)
			{
				if (!arrow.Valid || !tryBlockBox(arrow.Block, out BoundingBox box)
					|| box.Min != arrow.Box.Min || box.Max != arrow.Box.Max
					|| arrow.Block.BlockEntity.BasePoint != arrow.BasePoint)
					return false;
			}
			return true;
		}
	}

	private static bool isAcyclic(BlockRecord block, HashSet<BlockRecord> path)
	{
		if (path.Count >= 64 || !path.Add(block)) return false;
		foreach (Entity entity in block.Entities)
		{
			//Dimension pictures contain drawing primitives and arrow INSERTs, not dimensions.
			if (entity is Dimension) return false;
			if (entity is Insert insert && insert.Block != null && !isAcyclic(insert.Block, path)) return false;
		}
		path.Remove(block);
		return true;
	}

	private static IEnumerable<ExtendedDataRecord> getOverrides(Dimension dimension)
		=> dimension.ExtendedData.TryGet(AppId.DefaultName, out ExtendedData data)
			? data.Records : Enumerable.Empty<ExtendedDataRecord>();

	private static object copyValue(object value) => value is byte[] bytes ? bytes.Clone() : value;
	private static bool equalValue(object a, object b) => a is byte[] bytes && b is byte[] other
		? bytes.SequenceEqual(other) : Equals(a, b);

	private sealed class State
	{
		private readonly object _source;
		private readonly PropertyInfo[] _members;
		private readonly object[] _values;

		internal State(object source)
		{
			this._source = source;
			this._members = source == null ? Array.Empty<PropertyInfo>() : _properties.GetOrAdd(source.GetType(), type =>
				type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
					.Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
						&& (typeof(Dimension).IsAssignableFrom(p.DeclaringType)
							|| p.DeclaringType == typeof(DimensionStyle) || p.DeclaringType == typeof(TextStyle))
						&& (p.PropertyType.IsValueType || p.PropertyType == typeof(string)
							|| typeof(CadObject).IsAssignableFrom(p.PropertyType))
						&& (p.IsDefined(typeof(DxfCodeValueAttribute), false) || p.Name == nameof(Dimension.Flags)))
					.ToArray());
			this._values = this._members.Select(p => p.GetValue(source)).ToArray();
		}

		internal bool Matches(object source)
		{
			if (!ReferenceEquals(this._source, source)) return false;
			for (int i = 0; i < this._members.Length; i++)
				if (!Equals(this._values[i], this._members[i].GetValue(source))) return false;
			return true;
		}
	}
}
