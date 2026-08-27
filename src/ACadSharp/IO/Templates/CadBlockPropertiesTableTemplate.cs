using ACadSharp.Objects.Evaluations;
using System.Collections.Generic;

namespace ACadSharp.IO.Templates;

internal class CadBlockPropertiesTableTemplate : CadBlock1PtParameterTemplate
{
	public List<ulong> ColumnParameterHandles { get; } = new();

	public CadBlockPropertiesTableTemplate()
		: base(new BlockPropertiesTable())
	{
	}

	public CadBlockPropertiesTableTemplate(BlockPropertiesTable table)
		: base(table)
	{
	}

	protected override void build(CadDocumentBuilder builder)
	{
		base.build(builder);

		BlockPropertiesTable table = this.CadObject as BlockPropertiesTable;
		for (int i = 0; i < this.ColumnParameterHandles.Count && i < table.Columns.Count; i++)
		{
			ulong handle = this.ColumnParameterHandles[i];
			if (handle == 0)
			{
				continue;
			}

			if (builder.TryGetCadObject(handle, out BlockParameter parameter))
			{
				table.Columns[i].Parameter = parameter;
			}
			else
			{
				builder.Notify($"[{table}] column parameter with handle {handle} not found.");
			}
		}
	}
}
