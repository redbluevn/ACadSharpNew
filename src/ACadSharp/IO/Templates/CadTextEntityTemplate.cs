using ACadSharp.Entities;
using ACadSharp.Tables;

namespace ACadSharp.IO.Templates
{
	internal class CadTextEntityTemplate : CadEntityTemplate
	{
		public ulong? StyleHandle { get; set; }

		public string StyleName { get; set; }

		internal double? MTextExtentsWidth { get; set; }

		internal double? MTextExtentsHeight { get; set; }

		public CadTextEntityTemplate(Entity entity) : base(entity) { }

		private System.Text.StringBuilder _dxfMText;

		internal void AppendDxfMText(string value)
		{
			(this._dxfMText ??= new()).Append(value);
		}

		internal void CompleteDxfMText()
		{
			if (this._dxfMText == null) return;
			((MText)this.CadObject).Value = CadUtils.UnescapeDxfMText(this._dxfMText.ToString());
			this._dxfMText = null;
		}

		protected override void build(CadDocumentBuilder builder)
		{
			base.build(builder);

			IText text = (IText)this.CadObject;

			if (this.getTableReference(builder, this.StyleHandle, this.StyleName, out TextStyle style))
			{
				text.Style = style;
			}
			if (text is MText mtext && this.MTextExtentsWidth.HasValue && this.MTextExtentsHeight.HasValue)
			{
				mtext.SetMeasuredExtents(this.MTextExtentsWidth.Value, this.MTextExtentsHeight.Value);
			}
		}
	}
}
