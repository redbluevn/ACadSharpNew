using ACadSharp.Attributes;
using ACadSharp.Classes;
using System.Collections.Generic;

namespace ACadSharp.Objects.Evaluations;

/// <summary>
/// Represents a BLOCKPROPERTIESTABLE object in a dynamic block evaluation graph.
/// </summary>
/// <remarks>
/// The public model preserves the complete native table envelope. Callers should validate the
/// currently understood constants before assigning product semantics to a table.
/// </remarks>
[DxfName(DxfFileToken.ObjectBlockPropertiesTable)]
[DxfSubClass(DxfSubclassMarker.BlockPropertiesTable)]
public class BlockPropertiesTable : Block1PtParameter, IDxfClassDefined
{
	/// <summary>Native table format version.</summary>
	public int Version { get; set; } = 2;

	/// <summary>Display label of the table parameter.</summary>
	public string Label { get; set; } = string.Empty;

	/// <summary>Display description of the table parameter.</summary>
	public string Description { get; set; } = string.Empty;

	/// <summary>Property columns controlled by the table.</summary>
	public List<Column> Columns { get; } = new();

	/// <summary>Native value written before the table-level flags.</summary>
	public int Value90 { get; set; } = 1;

	/// <summary>First native sentinel value.</summary>
	public short Value170A { get; set; } = -9999;

	/// <summary>Second native sentinel value.</summary>
	public short Value170B { get; set; } = -9999;

	/// <summary>First native table flag.</summary>
	public bool Value290 { get; set; }

	/// <summary>Second native table flag.</summary>
	public bool Value291 { get; set; } = true;

	/// <summary>Third native table flag.</summary>
	public bool Value292 { get; set; } = true;

	/// <summary>Fourth native table flag.</summary>
	public bool Value293 { get; set; }

	/// <summary>Fifth native table flag.</summary>
	public bool Value294 { get; set; }

	/// <summary>Native unmatched-value payload; an empty string represents AutoCAD's **Last** choice.</summary>
	public string UnmatchedValue { get; set; } = string.Empty;

	/// <summary>Rows in display order.</summary>
	public List<Row> Rows { get; } = new();

	/// <summary>Native row-selector expression identifier.</summary>
	public int Value93 { get; set; }

	/// <summary>Whether block properties must match a row in the table.</summary>
	public bool MustMatch { get; set; } = true;

	/// <summary>Native final visibility flag.</summary>
	public bool FinalValue291 { get; set; } = true;

	/// <summary>Native final unmatched-value flag.</summary>
	public bool FinalValue292 { get; set; }

	/// <inheritdoc/>
	public override string ObjectName => DxfFileToken.ObjectBlockPropertiesTable;

	/// <inheritdoc/>
	public override string SubclassMarker => DxfSubclassMarker.BlockPropertiesTable;

	/// <inheritdoc/>
	public DxfClass GetDxfClass()
	{
		return new DxfClass
		{
			CppClassName = DxfSubclassMarker.BlockPropertiesTable,
			DwgVersion = ACadVersion.AC1018,
			DxfName = DxfFileToken.ObjectBlockPropertiesTable,
			ItemClassId = 499,
			MaintenanceVersion = 55,
			ProxyFlags = ProxyFlags.EraseAllowed | ProxyFlags.CloningAllowed | ProxyFlags.DisablesProxyWarningDialog,
			WasZombie = false,
		};
	}

	/// <summary>Describes one native property column.</summary>
	public class Column
	{
		/// <summary>Parameter whose property is represented by the column.</summary>
		public BlockParameter Parameter { get; set; }

		/// <summary>Zero-based native property index.</summary>
		public short PropertyIndex { get; set; }

		/// <summary>Native column sentinel.</summary>
		public short Value171 { get; set; } = -1;

		/// <summary>Native unmatched-value label.</summary>
		public string UnmatchedValue { get; set; } = string.Empty;

		/// <summary>Evaluation-graph connection name.</summary>
		public string ConnectionName { get; set; } = string.Empty;
	}

	/// <summary>Describes one table row.</summary>
	public class Row
	{
		/// <summary>Zero-based native row index.</summary>
		public int Index { get; set; }

		/// <summary>Cell values in column order.</summary>
		public List<Value> Values { get; } = new();
	}

	/// <summary>Represents the bounded native evaluation variant used by a table cell.</summary>
	public class Value
	{
		/// <summary>DXF value group code. The proven numeric carrier uses 40.</summary>
		public short Code { get; set; } = 40;

		/// <summary>Numeric value for code 40.</summary>
		public double Number { get; set; }
	}
}
