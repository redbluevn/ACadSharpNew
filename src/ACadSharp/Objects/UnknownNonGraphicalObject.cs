using ACadSharp.Classes;

namespace ACadSharp.Objects
{
	/// <summary>
	/// Class that holds the basic information for an unknown <see cref="NonGraphicalObject"/>.
	/// </summary>
	/// <remarks>
	/// Unknown entities may appear in the <see cref="CadDocument"/> if the cad file contains proxies or objects not yet supported by ACadSharp.
	/// </remarks>
	public class UnknownNonGraphicalObject : NonGraphicalObject
	{
		/// <inheritdoc/>
		public override ObjectType ObjectType => ObjectType.UNDEFINED;

		/// <inheritdoc/>
		public override string ObjectName
		{
			get
			{
				if (this.DxfClass == null)
				{
					return "UNKNOWN";
				}
				else
				{
					return this.DxfClass.DxfName;
				}
			}
		}

		/// <inheritdoc/>
		public override string SubclassMarker
		{
			get
			{
				if (this.DxfClass == null)
				{
					return DxfSubclassMarker.Entity;
				}
				else
				{
					return this.DxfClass.CppClassName;
				}
			}
		}

		/// <summary>
		/// Dxf class linked to this entity.
		/// </summary>
		public DxfClass DxfClass { get; }

		/// <summary>
		/// The raw DWG record body of this object, captured on read when
		/// <see cref="ACadSharp.IO.CadReaderConfiguration.KeepUnknownNonGraphicalObjects"/> is on.
		/// The DWG writer emits it verbatim when it writes at <see cref="RawObjectVersion"/> - the
		/// class numbers and the handles it refers to are preserved by the writer, so the record
		/// stays valid. At any other version it cannot be re-expressed and the object is left out
		/// with a notification, as before.
		/// </summary>
		public byte[] RawObjectBody { get; internal set; }

		/// <summary>
		/// The file version <see cref="RawObjectBody"/> was read from. The record layout is
		/// version-specific, so it is only written back into a file of the same version.
		/// </summary>
		public ACadVersion RawObjectVersion { get; internal set; } = ACadVersion.Unknown;

		/// <summary>
		/// The R2010+ record framing carries the bit size of the handle stream next to the byte
		/// size of the body; kept so the framing can be rebuilt exactly. 0 before R2010.
		/// </summary>
		public ulong RawHandleBitSize { get; internal set; }

		internal UnknownNonGraphicalObject(DxfClass dxfClass)
		{
			this.DxfClass = dxfClass;
		}
	}
}
