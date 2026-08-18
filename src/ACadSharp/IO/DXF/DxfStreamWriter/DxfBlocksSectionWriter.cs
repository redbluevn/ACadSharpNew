using ACadSharp.Blocks;
using ACadSharp.Entities;
using ACadSharp.IO.DXF.DxfStreamWriter;
using ACadSharp.Tables;

namespace ACadSharp.IO.DXF
{
	internal class DxfBlocksSectionWriter : DxfSectionWriterBase
	{
		public override string SectionName { get { return DxfFileToken.BlocksSection; } }

		public DxfBlocksSectionWriter(IDxfStreamWriter writer, CadDocument document, CadObjectHolder objectHolder, DxfWriterConfiguration configuration)
			: base(writer, document, objectHolder, configuration) { }

		protected override void writeSection()
		{
			foreach (BlockRecord b in this._document.BlockRecords)
			{
				this.writeBlock(b.BlockEntity);
				this.processEntities(b);
				this.writeBlockEnd(b.BlockEnd);
			}
		}

		private void writeBlock(Block block)
		{
			DxfClassMap map = DxfClassMap.Create<Block>();

			this._writer.Write(DxfCode.Start, block.ObjectName);

			this.writeCommonObjectData(block);

			this.writeCommonEntityData(block);

			this._writer.Write(DxfCode.Subclass, DxfSubclassMarker.BlockBegin);

			if (!string.IsNullOrEmpty(block.XRefPath))
			{
				this._writer.Write(1, block.XRefPath, map);
			}
			this._writer.Write(2, block.Name, map);

			//A block is anonymous only when its name is one AutoCAD generated, which always starts
			//with an asterisk. A drawing can carry the flag on a block with a real name - xref bound
			//blocks of one production drawing do - and AutoCAD, asked to save the same drawing,
			//leaves the flag out for those and audits "Invalid anonymous block" when it is there.
			this._writer.Write(70, (short)CadUtils.EffectiveBlockFlags(block.Flags, block.Name), map);

			if (this.Version >= ACadVersion.AC1015 && block.IsUnloaded)
			{
				this._writer.Write(71, block.IsUnloaded ? 1 : 0, map);
			}

			this._writer.Write(10, block.BasePoint, map);

			this._writer.Write(3, block.Name, map);
			this._writer.Write(4, block.Comments, map);
		}

		private void processEntities(BlockRecord b)
		{
			if (b.Name == BlockRecord.ModelSpaceName || b.Name == BlockRecord.PaperSpaceName)
			{
				foreach (Entity e in b.Entities)
				{
					if(e is Seqend)
					{

					}

					this.Holder.Entities.Enqueue(e);
				}
			}
			else
			{
				foreach (Entity e in b.Entities)
				{
					this.writeEntity(e);
				}
			}
		}

		private void writeBlockEnd(BlockEnd block)
		{
			this._writer.Write(DxfCode.Start, block.ObjectName);

			this.writeCommonObjectData(block);

			this._writer.Write(DxfCode.Subclass, DxfSubclassMarker.Entity);

			this._writer.Write(8, block.Layer.Name);

			this._writer.Write(DxfCode.Subclass, DxfSubclassMarker.BlockEnd);
		}
	}
}
