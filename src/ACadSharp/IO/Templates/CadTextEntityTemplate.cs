using ACadSharp.Entities;
using ACadSharp.Tables;

namespace ACadSharp.IO.Templates
{
	internal class CadTextEntityTemplate : CadEntityTemplate
	{
		public ulong? StyleHandle { get; set; }

		public string StyleName { get; set; }

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
		}
	}
}
