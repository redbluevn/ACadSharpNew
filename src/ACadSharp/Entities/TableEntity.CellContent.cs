using ACadSharp.Attributes;
using ACadSharp.Tables;
using static ACadSharp.Objects.TableStyle;

namespace ACadSharp.Entities;

public partial class TableEntity
{
	/// <summary>
	/// Represents the content of a table cell, including its type, format, and associated CAD value.
	/// </summary>
	/// <remarks>Use this class to encapsulate the data and metadata for a single cell in a table, such as in a
	/// CAD drawing or spreadsheet context. The properties provide access to the cell's content type, formatting
	/// information, and the underlying CAD value.</remarks>
	public class CellContent
	{
		/// <summary>
		/// Gets or sets the type of content contained in the table cell.
		/// </summary>
		[DxfCodeValue(90)]
		public TableCellContentType ContentType { get; set; }

		/// <summary>
		/// Gets the format used to interpret or serialize the content.
		/// </summary>
		public ContentFormat Format { get; } = new();

		/// <summary>
		/// Gets the value associated with the CAD entity or property.
		/// </summary>
		public CadValue CadValue { get; } = new();

		/// <summary>
		/// The block this content draws, when <see cref="ContentType"/> is
		/// <see cref="TableCellContentType.Block"/>.
		/// </summary>
		/// <remarks>
		/// Both readers read the handle and both threw it away: the DWG reader put it on
		/// <c>CadTableCellContentTemplate.BlockRecordHandle</c> and the DXF reader put it on the
		/// cell template's <c>ValueHandle</c>, and each <c>Build</c> either ignored the field or
		/// resolved it into a local it then did nothing with - an empty <c>if</c> body. Both writers
		/// then wrote a null handle in its place, so a block cell came back from AutoCAD as a
		/// <b>text</b> cell. Measured on AutoCAD's own re-export of a file written here, on the two
		/// block cells of <c>sample_AC1032</c> (T97).
		/// </remarks>
		public BlockRecord BlockRecord { get; set; }
	}
}