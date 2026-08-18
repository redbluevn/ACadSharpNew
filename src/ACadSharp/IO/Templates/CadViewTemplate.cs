using ACadSharp.Tables;

namespace ACadSharp.IO.Templates
{
	internal class CadViewTemplate : CadTableEntryTemplate<View>
	{
		public ulong? VisualStyleHandle { get; set; }

		public ulong? NamedUcsHandle { get; set; }

		public ulong? UcsHandle { get; set; }

		public CadViewTemplate() : base(new View()) { }

		public CadViewTemplate(View entry) : base(entry) { }

		protected override void build(CadDocumentBuilder builder)
		{
			base.build(builder);

			if (builder.TryGetCadObject(this.VisualStyleHandle, out Objects.VisualStyle visualStyle))
			{
				this.CadObject.VisualStyle = visualStyle;
			}
			else if (this.VisualStyleHandle.HasValue && this.VisualStyleHandle > 0)
			{
				builder.Notify($"Visual style {this.VisualStyleHandle} not found for view {this.CadObject.Name}", NotificationType.Warning);
			}

			//TODO: assing ucs for view
		}
	}
}
