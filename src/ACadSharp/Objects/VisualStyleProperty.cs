using System;
using System.Globalization;

namespace ACadSharp.Objects
{
	/// <summary>
	/// Value type of a <see cref="VisualStyleProperty"/>.
	/// </summary>
	public enum VisualStylePropertyType
	{
		/// <summary>
		/// 32 bit integer, DXF group code 90.
		/// </summary>
		Integer,

		/// <summary>
		/// Double, DXF group code 40.
		/// </summary>
		Double,

		/// <summary>
		/// Color, DXF group codes 62 (index) and 420 (true color).
		/// </summary>
		Color,

		/// <summary>
		/// Boolean, DXF group code 290.
		/// </summary>
		Boolean,

		/// <summary>
		/// String, DXF group code 1.
		/// </summary>
		String,
	}

	/// <summary>
	/// One entry of the <see cref="VisualStyle"/> property list used from AC1027 (R2013) onwards.
	/// </summary>
	/// <remarks>
	/// From R2013 AutoCAD stores a visual style as a flat, positional list of values: each entry is a
	/// value plus a flag (DXF group code 176). The position in the list identifies the property, there
	/// is no per-entry name or code in the file, which is why the entries are kept as an ordered list.
	/// </remarks>
	public class VisualStyleProperty : ICloneable
	{
		/// <summary>
		/// Flag of the property, DXF group code 176.
		/// </summary>
		/// <remarks>
		/// AutoCAD writes 1 for the styles a user can pick, 0 for the entries of the internal styles
		/// and 2 for a property that is set on the style itself instead of being inherited.
		/// </remarks>
		public short Flag { get; set; } = 1;

		/// <summary>
		/// Value of the property, the CLR type matches <see cref="ValueType"/>.
		/// </summary>
		public object Value { get; set; }

		/// <summary>
		/// Type of <see cref="Value"/>, it defines how the entry is stored in the file.
		/// </summary>
		public VisualStylePropertyType ValueType { get; set; }

		/// <summary>
		/// Default constructor.
		/// </summary>
		public VisualStyleProperty()
		{ }

		/// <summary>
		/// Initializes a property with a value and a flag.
		/// </summary>
		/// <param name="type">Value type of the entry.</param>
		/// <param name="value">Value of the entry.</param>
		/// <param name="flag">Flag of the entry, DXF group code 176.</param>
		public VisualStyleProperty(VisualStylePropertyType type, object value, short flag = 1)
		{
			this.ValueType = type;
			this.Value = value;
			this.Flag = flag;
		}

		/// <summary>
		/// Value as boolean.
		/// </summary>
		public bool AsBool()
		{
			return Convert.ToBoolean(this.Value, CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Value as color.
		/// </summary>
		public Color AsColor()
		{
			return this.Value is Color color ? color : default;
		}

		/// <summary>
		/// Value as double.
		/// </summary>
		public double AsDouble()
		{
			return Convert.ToDouble(this.Value, CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Value as integer.
		/// </summary>
		public int AsInt()
		{
			return Convert.ToInt32(this.Value, CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Value as string.
		/// </summary>
		public string AsString()
		{
			return this.Value as string;
		}

		/// <inheritdoc/>
		public object Clone()
		{
			return new VisualStyleProperty(this.ValueType, this.Value, this.Flag);
		}

		/// <inheritdoc/>
		public override string ToString()
		{
			return $"{this.ValueType}: {this.Value} (flag {this.Flag})";
		}
	}
}
