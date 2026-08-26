using ACadSharp.Entities;
using ACadSharp.Tables;
using System.Collections.Generic;

namespace ACadSharp.IO.Templates;

internal partial class CadTableEntityTemplate
{
	internal class CadTableCellTemplate : ICadTemplate
	{
		public HashSet<(ulong, string)> AttributeHandles { get; } = new();

		public TableEntity.Cell Cell { get; }

		public List<CadTableCellContentTemplate> ContentTemplates { get; } = new();

		public double? FormatTextHeight { get; set; }

		public int StyleId { get; set; }

		public ulong? TextStyleOverrideHandle { get; set; }

		public ulong? UnknownHandle { get; set; }

		public ulong? ValueHandle { get; set; }

		public CadTableCellTemplate(TableEntity.Cell cell)
		{
			this.Cell = cell;
		}

		public void Build(CadDocumentBuilder builder)
		{
			if (StyleId != 0)
			{
			}

			//DXF puts the block of a block cell in group 340 of the cell, which the reader stores
			//here. Resolving it into a local and doing nothing with it - which is what stood here -
			//is how the cell lost the block it draws, and how AutoCAD came to read it back as a text
			//cell (T97).
			if (builder.TryGetCadObject(this.ValueHandle, out BlockRecord block) && this.Cell.Content != null)
			{
				this.Cell.Content.BlockRecord = block;
			}

			foreach (var contentTemplate in this.ContentTemplates)
			{
				contentTemplate.Build(builder);
			}
		}
	}
}