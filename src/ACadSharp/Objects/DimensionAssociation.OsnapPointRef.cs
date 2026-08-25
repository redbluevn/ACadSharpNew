using ACadSharp.Attributes;
using CSMath;

namespace ACadSharp.Objects;

public partial class DimensionAssociation
{
	public class OsnapPointRef
	{
		/// <summary>
		/// Gets or sets the geometry parameter used for the Near object snap (Osnap).
		/// </summary>
		[DxfCodeValue(40)]
		public double GeometryParameter { get; set; }

		/// <summary>
		/// Gets or sets the object snap type associated with the entity.
		/// </summary>
		[DxfCodeValue(72)]
		public ObjectOsnapType ObjectOsnapType { get; set; }

		/// <summary>
		/// Gets or sets the object snap (Osnap) point in world coordinate system (WCS).
		/// </summary>
		/// <remarks>The Osnap point is used to specify precise locations on geometry for object snapping
		/// operations.</remarks>
		[DxfCodeValue(10, 20, 30)]
		public XYZ OsnapPoint { get; set; }

		/// <summary>
		/// Gets or sets which part of the referenced entity this osnap point attaches to.
		/// </summary>
		/// <remarks>
		/// The previous summary here described a rotated dimension being parallel or perpendicular,
		/// which belongs to a different field entirely.
		/// </remarks>
		[DxfCodeValue(73)]
		public SubentType SubentType { get; set; } = SubentType.None;

		/// <summary>
		/// Gets or sets the graphics system marker (GsMarker) associated with the main object.
		/// </summary>
		[DxfCodeValue(91)]
		public int GsMarker { get; set; }

		//332
		//ID of intersection object (geometry)

		/// <summary>
		/// Gets or sets which part of the intersection object the osnap point attaches to.
		/// </summary>
		[DxfCodeValue(74)]
		public SubentType IntersectionSubType { get; set; }

		[DxfCodeValue(92)]
		public int IntersectionGsMarker { get; set; }

		/// <summary>
		/// Whether this reference attaches to the intersection of its object with a second one.
		/// </summary>
		/// <remarks>
		/// The DXF reader takes 74 and 92 in but drops the 332 that names the second object, and no
		/// writer emits any of the three - so a reference that arrives with an intersection leaves
		/// without one. The writers ask this to report that loss instead of making it silently
		/// (T85); filling 74 and 92 in without 332 would be half a structure, and no drawing on hand
		/// carries one to measure the rest against.
		/// </remarks>
		internal bool HasIntersectionReference
			=> this.IntersectionSubType != SubentType.None || this.IntersectionGsMarker != 0;

		//302
		//Handle(string) of intersection Xref object

		[DxfCodeValue(75)]
		public bool HasLastPointRef { get; set; }

		/// <summary>
		/// Gets or sets the associated geometry object.
		/// </summary>
		[DxfCodeValue(DxfReferenceType.Handle, 331)]
		public CadObject Geometry { get; set; }
	}
}