using ACadSharp.Objects;

namespace ACadSharp.IO.Templates
{
	/// <summary>
	/// Template of a <see cref="VisualStyle"/>.
	/// </summary>
	/// <remarks>
	/// The DXF record uses group code 70 twice: first for the type of the style and then for the
	/// number of entries of the property list. The reader needs to remember which one it has already
	/// seen, because a style whose type is 0 (Flat) cannot be told apart by its value alone.
	/// </remarks>
	internal class CadVisualStyleTemplate : CadTemplate<VisualStyle>
	{
		/// <summary>
		/// True once the group code 70 that holds the type has been read.
		/// </summary>
		public bool TypeRead { get; set; }

		public CadVisualStyleTemplate() : base(new VisualStyle()) { }

		public CadVisualStyleTemplate(VisualStyle visualStyle) : base(visualStyle) { }
	}
}
