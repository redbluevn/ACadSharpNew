using ACadSharp.Attributes;
using ACadSharp.Tables;
using ACadSharp.Text;
using CSMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ACadSharp.Entities;

/// <summary>
/// Represents a <see cref="MText"/> entity.
/// </summary>
/// <remarks>
/// Object name <see cref="DxfFileToken.EntityMText"/> <br/>
/// Dxf class name <see cref="DxfSubclassMarker.MText"/>
/// </remarks>
[DxfName(DxfFileToken.EntityMText)]
[DxfSubClass(DxfSubclassMarker.MText)]
public partial class MText : Entity, IText
{
	/// <inheritdoc/>
	[DxfCodeValue(11, 21, 31)]
	public XYZ AlignmentPoint { get; set; } = XYZ.AxisX;

	/// <summary>
	/// Attachment point
	/// </summary>
	[DxfCodeValue(71)]
	public AttachmentPointType AttachmentPoint { get; set; } = AttachmentPointType.TopLeft;

	/// <summary>
	/// Background fill color
	/// </summary>
	/// <remarks>
	/// Color to use for background fill when group code 90 is 1.
	/// </remarks>
	[DxfCodeValue(63, 421, 430)]
	public Color BackgroundColor { get; set; }

	/// <summary>
	/// Background fill setting
	/// </summary>
	[DxfCodeValue(90)]
	public BackgroundFillFlags BackgroundFillFlags { get; set; } = BackgroundFillFlags.None;

	/// <summary>
	/// Determines how much border there is around the text.
	/// </summary>
	[DxfCodeValue(45)]
	public double BackgroundScale { get; set; } = 1.5;

	/// <summary>
	/// Transparency of background fill color
	/// </summary>
	[DxfCodeValue(441)]
	public Transparency BackgroundTransparency { get; set; }

	/// <summary>
	/// Gets or sets the text column data associated with this instance.
	/// </summary>
	public TextColumnData ColumnData { get; private set; } = new();

	/// <summary>
	/// Drawing direction
	/// </summary>
	[DxfCodeValue(72)]
	public DrawingDirectionType DrawingDirection { get; set; } = DrawingDirectionType.LeftToRight;

	/// <summary>
	/// Gets a value indicating whether the current object contains any columns.
	/// </summary>
	public bool HasColumns
	{
		get
		{
			return this.ColumnData.ColumnType != ColumnType.NoColumns;
		}
	}

	/// <inheritdoc/>
	[DxfCodeValue(40)]
	public double Height { get; set; } = 1.0d;

	/// <summary>
	/// Horizontal width of the characters that make up the mtext entity.
	/// This value will always be equal to or less than the value of group code 41
	/// </summary>
	/// <remarks>
	/// read-only, ignored if supplied
	/// </remarks>
	[DxfCodeValue(DxfReferenceType.Ignored, 42)]
	public double HorizontalWidth { get; set; } = 0.9;

	/// <summary>
	/// A 3D WCS coordinate representing the insertion or origin point.
	/// </summary>
	[DxfCodeValue(10, 20, 30)]
	public XYZ InsertPoint { get; set; } = XYZ.Zero;

	public bool IsAnnotative { get; set; } = false;

	/// <summary>
	/// Mtext line spacing factor.
	/// </summary>
	/// <remarks>
	/// Percentage of default (3-on-5) line spacing to be applied.Valid values range from 0.25 to 4.00
	/// </remarks>
	[DxfCodeValue(44)]
	public double LineSpacing { get; set; } = 1.0;

	/// <summary>
	/// Mtext line spacing style.
	/// </summary>
	[DxfCodeValue(73)]
	public LineSpacingStyleType LineSpacingStyle { get; set; }

	/// <inheritdoc/>
	[DxfCodeValue(210, 220, 230)]
	public XYZ Normal { get; set; } = XYZ.AxisZ;

	/// <inheritdoc/>
	public override string ObjectName => DxfFileToken.EntityMText;

	/// <inheritdoc/>
	public override ObjectType ObjectType => ObjectType.MTEXT;

	/// <summary>
	/// Gets the plain text representation of the processed value.
	/// </summary>
	/// <remarks>This property processes the underlying value and returns its plain text equivalent. The parsing
	/// operation may involve removing formatting or extracting meaningful content.</remarks>
	public string PlainText
	{
		get
		{
			return TextProcessor.Parse(this.Value, out _);
		}
	}

	/// <summary>
	/// Reference rectangle height.
	/// </summary>
	[DxfCodeValue(46)]
	public double RectangleHeight { get; set; }

	/// <summary>
	/// Reference rectangle width.
	/// </summary>
	[DxfCodeValue(41)]
	public double RectangleWidth { get; set; }

	/// <inheritdoc/>
	/// <remarks>
	/// The rotation is only valid if the <see cref="Normal"/> is set to the Z axis.
	/// </remarks>
	[DxfCodeValue(DxfReferenceType.IsAngle | DxfReferenceType.Ignored, 50)]
	public double Rotation
	{
		get
		{
			return new XY(this.AlignmentPoint.X, this.AlignmentPoint.Y).GetAngle();
		}
	}

	/// <inheritdoc/>
	[DxfCodeValue(DxfReferenceType.Name | DxfReferenceType.Optional, 7)]
	public TextStyle Style
	{
		get { return this._style; }
		set
		{
			if (value == null)
			{
				throw new ArgumentNullException(nameof(value));
			}

			this._style = this.updateTableEntry(value, s => this._style = s, this.Document?.TextStyles);
		}
	}

	/// <inheritdoc/>
	public override string SubclassMarker => DxfSubclassMarker.MText;

	/// <inheritdoc/>
	[DxfCodeValue(1)]
	public string Value { get; set; } = string.Empty;

	/// <summary>
	/// Vertical height of the mtext entity
	/// </summary>
	/// <remarks>
	/// read-only, ignored if supplied
	/// </remarks>
	[DxfCodeValue(DxfReferenceType.Ignored, 43)]
	public double VerticalHeight { get; set; } = 0.2;

	private TextStyle _style = TextStyle.Default;

	private bool _hasMeasuredExtents;
	private TextColumnData _extentColumns;
	private double[] _extentColumnHeights;
	private string _extentValue, _extentFont, _extentBigFont;
	private double _extentTextHeight, _extentRectangleWidth, _extentRectangleHeight, _extentSpacing;
	private double _extentMeasuredWidth, _extentMeasuredHeight;
	private double _extentStyleWidth, _extentStyleHeight, _extentStyleOblique;
	private LineSpacingStyleType _extentSpacingStyle;
	private DrawingDirectionType _extentDirection;
	private StyleFlags _extentStyleFlags;
	private TextMirrorFlag _extentMirror;
	private FontFlags _extentFontFlags;

	internal void SetMeasuredExtents(double width, double height)
	{
		this._extentMeasuredWidth = this.HorizontalWidth = width;
		this._extentMeasuredHeight = this.VerticalHeight = height;
		this._hasMeasuredExtents = width > 0 && height > 0 && !double.IsInfinity(width) && !double.IsInfinity(height);
		this._extentColumns = this.ColumnData.Clone();
		this._extentColumnHeights = this.ColumnData.Heights.ToArray();
		this._extentValue = this.Value;
		this._extentTextHeight = this.Height;
		this._extentRectangleWidth = this.RectangleWidth;
		this._extentRectangleHeight = this.RectangleHeight;
		this._extentSpacing = this.LineSpacing;
		this._extentSpacingStyle = this.LineSpacingStyle;
		this._extentDirection = this.DrawingDirection;
		this._extentFont = this.Style.Filename;
		this._extentBigFont = this.Style.BigFontFilename;
		this._extentStyleWidth = this.Style.Width;
		this._extentStyleHeight = this.Style.Height;
		this._extentStyleOblique = this.Style.ObliqueAngle;
		this._extentStyleFlags = this.Style.Flags;
		this._extentMirror = this.Style.MirrorFlag;
		this._extentFontFlags = this.Style.TrueType;
	}

	internal bool TryGetMeasuredExtents(out double width, out double height)
	{
		width = height = 0;
		//The stored dimensions describe the imported layout, not a new font measurement.
		//After text/layout/style edits, retain the point fallback until measured again.
		if (!this._hasMeasuredExtents || this._extentValue != this.Value
			|| this._extentColumns.ColumnType != this.ColumnData.ColumnType
			|| this._extentColumns.ColumnCount != this.ColumnData.ColumnCount
			|| this._extentColumns.AutoHeight != this.ColumnData.AutoHeight
			|| this._extentColumns.FlowReversed != this.ColumnData.FlowReversed
			|| this._extentColumns.Width != this.ColumnData.Width || this._extentColumns.Gutter != this.ColumnData.Gutter
			|| !this._extentColumnHeights.SequenceEqual(this.ColumnData.Heights)
			|| this._extentMeasuredWidth != this.HorizontalWidth || this._extentMeasuredHeight != this.VerticalHeight
			|| this._extentRectangleHeight != this.RectangleHeight
			|| this._extentTextHeight != this.Height || this._extentRectangleWidth != this.RectangleWidth
			|| this._extentSpacing != this.LineSpacing || this._extentSpacingStyle != this.LineSpacingStyle
			|| this._extentDirection != this.DrawingDirection || this._extentFont != this.Style.Filename
			|| this._extentBigFont != this.Style.BigFontFilename || this._extentStyleWidth != this.Style.Width
			|| this._extentStyleHeight != this.Style.Height || this._extentStyleOblique != this.Style.ObliqueAngle
			|| this._extentStyleFlags != this.Style.Flags || this._extentMirror != this.Style.MirrorFlag
			|| this._extentFontFlags != this.Style.TrueType) return false;
		width = this.HorizontalWidth;
		height = this.VerticalHeight;
		return width > 0 && height > 0 && !double.IsInfinity(width) && !double.IsInfinity(height);
	}

	/// <inheritdoc/>
	public MText() : base() { }

	/// <summary>
	/// Initializes a new instance of the <see cref="MText"/> class with the specified text value.
	/// </summary>
	/// <param name="value">The text value to initialize the instance with. Cannot be <see langword="null"/>.</param>
	public MText(string value) : this()
	{
		this.Value = value;
	}

	/// <inheritdoc/>
	public override void ApplyTransform(Transform transform)
	{
		//The transformed layout must be measured again by a text engine.
		this._hasMeasuredExtents = false;
		XYZ newInsert = transform.ApplyTransform(this.InsertPoint);
		XYZ newNormal = this.transformNormal(transform, this.Normal);

		var transformation = this.getWorldMatrix(transform, Normal, newNormal, out Matrix3 transOW, out Matrix3 transWO);

		transWO = transWO.Transpose();

		List<XY> uv = applyRotation(
			new[]
			{
				XY.AxisX, XY.AxisY
			},
			this.Rotation);

		XYZ v;
		v = transOW * new XYZ(uv[0].X, uv[0].Y, 0.0);
		v = transformation * v;
		v = transWO * v;
		XY newUvector = new XY(v.X, v.Y);

		// the MText entity does not support non-uniform scaling
		double scale = newUvector.GetLength();

		v = transOW * new XYZ(uv[1].X, uv[1].Y, 0.0);
		v = transformation * v;
		v = transWO * v;
		XY newVvector = new XY(v.X, v.Y);

		double newRotation = newUvector.GetAngle();

		if (XY.Cross(newUvector, newVvector) < 0.0)
		{
			if (newUvector.Dot(uv[0]) < 0.0)
			{
				newRotation += 180;

				switch (this.AttachmentPoint)
				{
					case AttachmentPointType.TopLeft:
						this.AttachmentPoint = AttachmentPointType.TopRight;
						break;
					case AttachmentPointType.TopRight:
						this.AttachmentPoint = AttachmentPointType.TopLeft;
						break;
					case AttachmentPointType.MiddleLeft:
						this.AttachmentPoint = AttachmentPointType.MiddleRight;
						break;
					case AttachmentPointType.MiddleRight:
						this.AttachmentPoint = AttachmentPointType.MiddleLeft;
						break;
					case AttachmentPointType.BottomLeft:
						this.AttachmentPoint = AttachmentPointType.BottomRight;
						break;
					case AttachmentPointType.BottomRight:
						this.AttachmentPoint = AttachmentPointType.BottomLeft;
						break;
				}
			}
			else
			{
				switch (this.AttachmentPoint)
				{
					case AttachmentPointType.TopLeft:
						this.AttachmentPoint = AttachmentPointType.BottomLeft;
						break;
					case AttachmentPointType.TopCenter:
						this.AttachmentPoint = AttachmentPointType.BottomCenter;
						break;
					case AttachmentPointType.TopRight:
						this.AttachmentPoint = AttachmentPointType.BottomRight;
						break;
					case AttachmentPointType.BottomLeft:
						this.AttachmentPoint = AttachmentPointType.TopLeft;
						break;
					case AttachmentPointType.BottomCenter:
						this.AttachmentPoint = AttachmentPointType.TopCenter;
						break;
					case AttachmentPointType.BottomRight:
						this.AttachmentPoint = AttachmentPointType.TopRight;
						break;
				}
			}
		}

		double newHeight = this.Height * scale;
		newHeight = MathHelper.IsZero(newHeight) ? MathHelper.Epsilon : newHeight;

		this.InsertPoint = newInsert;
		this.Normal = newNormal;
		this.Height = newHeight;
		this.RectangleWidth *= scale;
	}

	/// <inheritdoc/>
	public override CadObject Clone()
	{
		MText clone = (MText)base.Clone();

		clone.Style = (TextStyle)(this.Style?.Clone());
		clone.ColumnData = this.ColumnData?.Clone();

		return clone;
	}

	/// <inheritdoc/>
	public override BoundingBox GetBoundingBox()
	{
		if (this.HasColumns || !this.TryGetMeasuredExtents(out double width, out double height)
			|| this.AlignmentPoint.GetLength() == 0 || this.Normal.GetLength() == 0)
			return new BoundingBox(this.InsertPoint);
		XYZ normal = this.Normal.Normalize();
		XYZ x = this.AlignmentPoint - normal * this.AlignmentPoint.Dot(normal);
		if (!(x.GetLength() > MathHelper.Epsilon)) return new BoundingBox(this.InsertPoint);
		x = x.Normalize();
		XYZ y = XYZ.Cross(normal, x).Normalize();
		int attachment = (int)this.AttachmentPoint - 1;
		if (attachment < 0 || attachment > 8) return new BoundingBox(this.InsertPoint);
		double left = -width * (attachment % 3) / 2.0;
		double bottom = height * ((attachment / 3) / 2.0 - 1);
		return BoundingBox.FromPoints(new[]
		{
			this.InsertPoint + x * left + y * bottom,
			this.InsertPoint + x * (left + width) + y * bottom,
			this.InsertPoint + x * left + y * (bottom + height),
			this.InsertPoint + x * (left + width) + y * (bottom + height),
		});
	}

	/// <summary>
	/// Splits the plain text into an array of lines based on common line break sequences.
	/// </summary>
	/// <remarks>The method splits the text using the following line break sequences:  carriage return and line feed
	/// ("\r\n"), carriage return ("\r"), line feed ("\n"), and the Unicode paragraph separator ("\P").
	/// The resulting array includes all lines, including empty ones.
	/// </remarks>
	/// <returns>An array of strings, where each string represents a line of text. The array may contain empty strings if the plain
	/// text includes consecutive line break sequences.</returns>
	public string[] GetPlainTextLines()
	{
		return this.PlainText.Split(
			new string[] { "\r\n", "\r", "\n", "\\P" },
			StringSplitOptions.None
		);
	}

	/// <summary>
	/// Splits the text into an array of lines based on common line break sequences.
	/// </summary>
	/// <remarks>The method splits the text using the following line break sequences: carriage return and line feed
	/// ("\r\n"), carriage return ("\r"),  line feed ("\n"), and the Unicode paragraph separator ("\P").
	/// The resulting array includes all lines, including empty ones.
	/// </remarks>
	/// <returns>
	/// An array of strings, where each string represents a line of text. The array may contain empty strings if the
	/// text includes consecutive line break sequences.
	/// </returns>
	public string[] GetTextLines()
	{
		return this.Value.Split(
			new string[] { "\r\n", "\r", "\n", "\\P" },
			StringSplitOptions.None
		);
	}

	internal override void AssignDocument(CadDocument doc)
	{
		base.AssignDocument(doc);

		this.updateTableEntry(this._style, s => this._style = s, doc.TextStyles);
	}

	internal override void UnassignDocument()
	{
		this.Document.TextStyles.RemoveReference(this.Style.Name, this);

		base.UnassignDocument();

		this._style = (TextStyle)this.Style?.Clone();
	}
}
