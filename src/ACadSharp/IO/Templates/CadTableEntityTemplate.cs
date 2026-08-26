using ACadSharp.Entities;
using ACadSharp.Objects;
using System.Collections.Generic;
using static ACadSharp.Entities.TableEntity;
using static ACadSharp.IO.Templates.CadTableStyleTemplate;

namespace ACadSharp.IO.Templates;

internal partial class CadTableEntityTemplate : CadInsertTemplate
{
	public ulong? BlockOwnerHandle { get; set; }

	public List<CadTableCellTemplate> CadTableCellTemplates { get; } = new();

	public List<CadTableComponentTemplate> CadTableComponentTemplates { get; } = new();

	public Cell CurrentCell { get { return this.CurrentCellTemplate.Cell; } }

	public CadTableCellTemplate CurrentCellTemplate { get; private set; }

	public List<ulong> FieldHandles { get; } = new();

	public double? HorizontalMargin { get; set; }

	/// <summary>
	/// The override text heights read before the first cell, one per column, in order.
	/// </summary>
	/// <remarks>
	/// They cannot be handed to the columns as they arrive: in AutoCAD's order the heights come
	/// BEFORE the column widths in 142, and it is 142 that creates the columns - so at that point
	/// there is nothing to put them on. Collected here and applied in build, once the columns exist.
	/// Assigning them where they are read looks right and quietly drops every one of them, which is
	/// what the first attempt at this did.
	/// </remarks>
	public List<double> ColumnTextHeights { get; } = new();

	public ulong? NullHandle { get; set; }

	public ulong? StyleHandle { get; set; }

	public TableEntity TableEntity { get { return this.CadObject as TableEntity; } }

	public CadCellStyleTemplate CellStyleTemplate { get; set; }

	private int _currCellIndex = 0;

	public CadTableEntityTemplate() : base(new TableEntity())
	{
	}

	public CadTableEntityTemplate(TableEntity table) : base(table)
	{
	}

	public void CreateCell(CellType type)
	{
		var rowIndex = this._currCellIndex / this.TableEntity.Columns.Count;

		var cell = new Cell();
		cell.Type = type;

		this.TableEntity.Rows[rowIndex].Cells.Add(cell);

		this.CurrentCellTemplate = new CadTableCellTemplate(cell);

		this.CadTableCellTemplates.Add(this.CurrentCellTemplate);

		this._currCellIndex++;
	}

	protected override void build(CadDocumentBuilder builder)
	{
		base.build(builder);

		if (builder.TryGetObjectTemplate<CadTableStyleTemplate>(this.StyleHandle, out var tableStyle))
		{
			this.TableEntity.Style = tableStyle.CadObject;
			tableStyle.Build(builder);
		}
		else
		{
			builder.Notify($"[{nameof(TableStyle)}] {this.StyleHandle} not found for table with handle {this.CadObject.Handle}", NotificationType.Warning);
		}

		foreach (var cellTemplate in this.CadTableCellTemplates)
		{
			cellTemplate.Build(builder);
		}

		foreach (var component in this.CadTableComponentTemplates)
		{
			component.Build(builder, this.TableEntity.Style);
		}

		foreach (var handle in this.FieldHandles)
		{
		}

		this.CellStyleTemplate?.Build(builder);

		//The columns exist by now, so the override heights collected while reading can be placed.
		for (int i = 0; i < this.ColumnTextHeights.Count && i < this.TableEntity.Columns.Count; i++)
		{
			this.TableEntity.Columns[i].CellStyleOverride.TextHeight = this.ColumnTextHeights[i];
		}
	}
}