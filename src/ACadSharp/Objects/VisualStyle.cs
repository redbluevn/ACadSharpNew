using ACadSharp.Attributes;
using ACadSharp.Classes;
using System.Collections.Generic;
using System.Xml.Linq;

namespace ACadSharp.Objects;

/// <summary>
/// Represents a <see cref="VisualStyle"/> object
/// </summary>
/// <remarks>
/// Object name <see cref="DxfFileToken.ObjectVisualStyle"/> <br/>
/// Dxf class name <see cref="DxfSubclassMarker.VisualStyle"/>
/// </remarks>
[DxfName(DxfFileToken.ObjectVisualStyle)]
[DxfSubClass(DxfSubclassMarker.VisualStyle)]
public class VisualStyle : NonGraphicalObject, IDxfClassDefined
{
	[DxfCodeValue(44)]
	public double Brightness { get; set; }

	/// <summary>
	/// Color
	/// </summary>
	[DxfCodeValue(62, 63)]
	public Color Color { get; set; }

	/// <summary>
	/// Description
	/// </summary>
	[DxfCodeValue(2)]
	public string Description { get; set; }

	[DxfCodeValue(93)]
	public int DisplaySettings { get; set; }

	[DxfCodeValue(174)]
	public short EdgeApplyStyleFlag { get; set; }

	[DxfCodeValue(66)]
	public int EdgeColor { get; set; }

	[DxfCodeValue(42)]
	public double EdgeCreaseAngle { get; set; }

	[DxfCodeValue(64)]
	public Color EdgeIntersectionColor { get; set; }

	[DxfCodeValue(175)]
	public int EdgeIntersectionLineType { get; set; }

	[DxfCodeValue(171)]
	public short EdgeIsolineCount { get; set; }

	[DxfCodeValue(78)]
	public int EdgeJitter { get; set; }

	[DxfCodeValue(92)]
	public int EdgeModifiers { get; set; }

	[DxfCodeValue(65)]
	public Color EdgeObscuredColor { get; set; }

	[DxfCodeValue(75)]
	public int EdgeObscuredLineType { get; set; }

	[DxfCodeValue(77)]
	public int EdgeOverhang { get; set; }

	[DxfCodeValue(67)]
	public Color EdgeSilhouetteColor { get; set; }

	[DxfCodeValue(79)]
	public int EdgeSilhouetteWidth { get; set; }

	[DxfCodeValue(91)]
	public int EdgeStyle { get; set; }

	[DxfCodeValue(74)]
	public EdgeStyleModel EdgeStyleModel { get; set; }

	[DxfCodeValue(76)]
	public int EdgeWidth { get; set; }

	[DxfCodeValue(73)]
	public FaceColorMode FaceColorMode { get; set; }

	/// <summary>
	/// Gets or sets the lighting model used to render the face.
	/// </summary>
	/// <remarks>The lighting model determines how light interacts with the face during rendering, affecting its
	/// appearance and shading. Set this property to control the visual style of the face in 3D scenes.</remarks>
	[DxfCodeValue(71)]
	public FaceLightingModelType FaceLightingModel { get; set; }

	[DxfCodeValue(72)]
	public FaceLightingQualityType FaceLightingQuality { get; set; }

	[DxfCodeValue(90)]
	public FaceModifierType FaceModifiers { get; set; }

	/// <summary>
	/// Face opacity level
	/// </summary>
	[DxfCodeValue(40)]
	public double FaceOpacityLevel { get; set; }

	/// <summary>
	/// Face specular level
	/// </summary>
	[DxfCodeValue(41)]
	public double FaceSpecularLevel { get; set; }

	/// <summary>
	/// Face style mono color
	/// </summary>
	[DxfCodeValue(421)]
	public Color FaceStyleMonoColor { get; set; }

	[DxfCodeValue(170)]
	public short HaloGap { get; set; }

	/// <summary>
	/// Internal use only flag
	/// </summary>
	[DxfCodeValue(291)]
	public bool InternalFlag { get; internal set; }

	/// <inheritdoc/>
	public override string ObjectName => DxfFileToken.ObjectVisualStyle;

	/// <inheritdoc/>
	public override ObjectType ObjectType => ObjectType.UNLISTED;

	[DxfCodeValue(43)]
	public double OpacityLevel { get; set; }

	/// <summary>
	/// Edge hide precision flag
	/// </summary>
	[DxfCodeValue(290)]
	public bool PrecisionFlag { get; set; }

	/// <summary>
	/// Raster file name
	/// </summary>
	[DxfCodeValue(1)]
	public string RasterFile { get; set; }

	[DxfCodeValue(173)]
	public short ShadowType { get; set; }

	/// <inheritdoc/>
	public override string SubclassMarker => DxfSubclassMarker.VisualStyle;

	/// <summary>
	/// Type
	/// </summary>
	[DxfCodeValue(70)]
	public int Type { get; set; }

	/// <summary>
	/// Positional property list used by AC1027 (R2013) and newer.
	/// </summary>
	/// <remarks>
	/// From R2013 the visual style is stored as an ordered list of values, each one with a flag
	/// (DXF group code 176); the position in the list identifies the property. Files older than
	/// R2013 use the named properties of this class instead and leave this list empty.
	/// The count written by AutoCAD is 58; see <see cref="PropertyCount"/>.
	/// </remarks>
	public IList<VisualStyleProperty> Properties { get; } = new List<VisualStyleProperty>();

	/// <summary>
	/// Version of the property list layout, DXF group code 177.
	/// </summary>
	/// <remarks>
	/// AutoCAD writes 2 in DWG and 3 in DXF for the 58-entry layout of R2013–R2018.
	/// </remarks>
	[DxfCodeValue(177)]
	public short PropertyListVersion { get; set; } = 2;

	/// <summary>
	/// Number of entries of the <see cref="Properties"/> list written by AutoCAD for
	/// <see cref="PropertyListVersion"/> 2, the value is not stored in the DWG file.
	/// </summary>
	public const int PropertyCount = 58;

	/// <summary>
	/// Default <see cref="VisualStyle"/> name.
	/// </summary>
	public const string DefaultName = "2dWireframe";

	/// <summary>
	/// Value type of each entry of the <see cref="Properties"/> list, index 0 to 57.
	/// </summary>
	/// <remarks>
	/// Established by comparing, for the same drawing, the DXF written by AutoCAD (where each
	/// entry carries its group code) with the DWG bit stream. Entries 0 to 27 match, in order,
	/// the named properties of this class; 28 to 57 are R2013 additions whose meaning is not
	/// documented, they are preserved as-is so a file can be round-tripped without losing them.
	/// </remarks>
	private static readonly VisualStylePropertyType[] _propertyTypes = new VisualStylePropertyType[]
	{
		VisualStylePropertyType.Integer,	//0  FaceLightingModel, 71
		VisualStylePropertyType.Integer,	//1  FaceLightingQuality, 72
		VisualStylePropertyType.Integer,	//2  FaceColorMode, 73
		VisualStylePropertyType.Integer,	//3  FaceModifiers, 90
		VisualStylePropertyType.Double,		//4  FaceOpacityLevel, 40
		VisualStylePropertyType.Double,		//5  FaceSpecularLevel, 41
		VisualStylePropertyType.Color,		//6  Color, 62 + 420
		VisualStylePropertyType.Integer,	//7  EdgeStyleModel, 74
		VisualStylePropertyType.Integer,	//8  EdgeStyle, 91
		VisualStylePropertyType.Color,		//9  EdgeIntersectionColor, 64
		VisualStylePropertyType.Color,		//10 EdgeObscuredColor, 65
		VisualStylePropertyType.Integer,	//11 EdgeObscuredLineType, 75
		VisualStylePropertyType.Integer,	//12 EdgeIntersectionLineType, 175
		VisualStylePropertyType.Double,		//13 EdgeCreaseAngle, 42
		VisualStylePropertyType.Integer,	//14 EdgeModifiers, 92
		VisualStylePropertyType.Color,		//15 EdgeColor, 66
		VisualStylePropertyType.Double,		//16 OpacityLevel, 43
		VisualStylePropertyType.Integer,	//17 EdgeWidth, 76
		VisualStylePropertyType.Integer,	//18 EdgeOverhang, 77
		VisualStylePropertyType.Integer,	//19 EdgeJitter, 78
		VisualStylePropertyType.Color,		//20 EdgeSilhouetteColor, 67
		VisualStylePropertyType.Integer,	//21 EdgeSilhouetteWidth, 79
		VisualStylePropertyType.Integer,	//22 HaloGap, 170
		VisualStylePropertyType.Integer,	//23 EdgeIsolineCount, 171
		VisualStylePropertyType.Boolean,	//24 PrecisionFlag, 290
		VisualStylePropertyType.Integer,	//25 DisplaySettings, 93
		VisualStylePropertyType.Double,		//26 Brightness, 44
		VisualStylePropertyType.Integer,	//27 ShadowType, 173
		VisualStylePropertyType.Boolean,	//28 R2013+, undocumented
		VisualStylePropertyType.Boolean,	//29
		VisualStylePropertyType.Boolean,	//30
		VisualStylePropertyType.Boolean,	//31
		VisualStylePropertyType.Boolean,	//32
		VisualStylePropertyType.Boolean,	//33
		VisualStylePropertyType.Boolean,	//34
		VisualStylePropertyType.Boolean,	//35
		VisualStylePropertyType.Boolean,	//36
		VisualStylePropertyType.Integer,	//37
		VisualStylePropertyType.Double,		//38
		VisualStylePropertyType.Double,		//39
		VisualStylePropertyType.Integer,	//40
		VisualStylePropertyType.Color,		//41
		VisualStylePropertyType.Integer,	//42
		VisualStylePropertyType.Integer,	//43
		VisualStylePropertyType.Color,		//44
		VisualStylePropertyType.Boolean,	//45
		VisualStylePropertyType.Integer,	//46
		VisualStylePropertyType.Integer,	//47
		VisualStylePropertyType.Integer,	//48
		VisualStylePropertyType.Boolean,	//49
		VisualStylePropertyType.Integer,	//50
		VisualStylePropertyType.Color,		//51
		VisualStylePropertyType.Double,		//52
		VisualStylePropertyType.Integer,	//53
		VisualStylePropertyType.String,		//54 raster file name
		VisualStylePropertyType.Boolean,	//55
		VisualStylePropertyType.Double,		//56
		VisualStylePropertyType.Double,		//57
	};

	/// <summary>
	/// Gets the value type of the entry at the given position of the <see cref="Properties"/> list.
	/// </summary>
	/// <param name="index">Position in the list, 0 to <see cref="PropertyCount"/> - 1.</param>
	public static VisualStylePropertyType GetPropertyType(int index)
	{
		if (index < 0 || index >= _propertyTypes.Length)
		{
			throw new System.ArgumentOutOfRangeException(nameof(index));
		}

		return _propertyTypes[index];
	}

	/// <summary>
	/// Copies the entries of <see cref="Properties"/> that have a documented meaning into the
	/// named properties of this class, so both views of the same style are consistent.
	/// </summary>
	/// <remarks>
	/// The list stays the authoritative source for DWG round-trip; this method only fills the
	/// convenience properties. Entries 28 to 57 have no named counterpart and are left in the list.
	/// </remarks>
	public void ApplyPropertyList()
	{
		if (this.Properties.Count < 28)
		{
			return;
		}

		this.FaceLightingModel = (FaceLightingModelType)this.Properties[0].AsInt();
		this.FaceLightingQuality = (FaceLightingQualityType)this.Properties[1].AsInt();
		this.FaceColorMode = (FaceColorMode)this.Properties[2].AsInt();
		this.FaceModifiers = (FaceModifierType)this.Properties[3].AsInt();
		this.FaceOpacityLevel = this.Properties[4].AsDouble();
		this.FaceSpecularLevel = this.Properties[5].AsDouble();
		this.Color = this.Properties[6].AsColor();
		this.EdgeStyleModel = (EdgeStyleModel)this.Properties[7].AsInt();
		this.EdgeStyle = this.Properties[8].AsInt();
		this.EdgeIntersectionColor = this.Properties[9].AsColor();
		this.EdgeObscuredColor = this.Properties[10].AsColor();
		this.EdgeObscuredLineType = this.Properties[11].AsInt();
		this.EdgeIntersectionLineType = this.Properties[12].AsInt();
		this.EdgeCreaseAngle = this.Properties[13].AsDouble();
		this.EdgeModifiers = this.Properties[14].AsInt();
		this.EdgeColor = this.Properties[15].AsColor().Index;
		this.OpacityLevel = this.Properties[16].AsDouble();
		this.EdgeWidth = this.Properties[17].AsInt();
		this.EdgeOverhang = this.Properties[18].AsInt();
		this.EdgeJitter = this.Properties[19].AsInt();
		this.EdgeSilhouetteColor = this.Properties[20].AsColor();
		this.EdgeSilhouetteWidth = this.Properties[21].AsInt();
		this.HaloGap = (short)this.Properties[22].AsInt();
		this.EdgeIsolineCount = (short)this.Properties[23].AsInt();
		this.PrecisionFlag = this.Properties[24].AsBool();
		this.DisplaySettings = this.Properties[25].AsInt();
		this.Brightness = this.Properties[26].AsDouble();
		this.ShadowType = (short)this.Properties[27].AsInt();
	}

	/// <inheritdoc/>
	public DxfClass GetDxfClass()
	{
		return new DxfClass
		{
			CppClassName = DxfSubclassMarker.VisualStyle,
			DwgVersion = ACadVersion.AC1021,
			DxfName = DxfFileToken.ObjectVisualStyle,
			ItemClassId = 499,
			MaintenanceVersion = 0,
			ProxyFlags = (ProxyFlags)4095,
			WasZombie = false,
		};
	}
}
