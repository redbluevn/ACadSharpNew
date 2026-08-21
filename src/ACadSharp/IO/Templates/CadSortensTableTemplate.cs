using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using System.Collections.Generic;

namespace ACadSharp.IO.Templates
{
	internal class CadSortensTableTemplate : CadTemplate<SortEntitiesTable>
	{
		public ulong? BlockOwnerHandle { get; set; }

		public List<(ulong?, ulong?)> Values { get; } = new List<(ulong?, ulong?)>();

		public CadSortensTableTemplate() : base(new SortEntitiesTable()) { }

		public CadSortensTableTemplate(SortEntitiesTable cadObject) : base(cadObject) { }

		protected override void build(CadDocumentBuilder builder)
		{
			base.build(builder);

			if (builder.TryGetCadObject(this.BlockOwnerHandle, out CadObject owner))
			{
				//Not always a block: a drawing can hold sort tables owned by a dictionary, and
				//AutoCAD writes and audits those without complaint. Keep whatever the file says so
				//the writers can put the same reference back.
				this.CadObject.BlockOwnerReference = owner;

				if (owner is BlockRecord record)
				{
					this.CadObject.BlockOwner = record;
				}
				else if (owner is null)
				{
					builder.Notify($"Block owner for SortEntitiesTable {this.CadObject.Handle} not found", NotificationType.Warning);
					return;
				}
			}

			//An entry whose entity is gone is stale, and AutoCAD drops it on open without counting it
			//as an error - one production drawing carries a table of 138,240 such entries, every one
			//of them pointing at an entity that is in no other part of the file, and AutoCAD's own
			//save of that drawing has neither the entries nor the table. Dropping them is right;
			//saying so once per entry was 87% of everything that drawing reported. Say it once.
			int stale = 0;
			ulong firstStale = 0;
			foreach ((ulong?, ulong?) pair in this.Values)
			{
				if (builder.TryGetCadObject(pair.Item2, out Entity entity))
				{
					this.CadObject.Add(entity, pair.Item1.Value);
				}
				else
				{
					if (stale == 0)
					{
						firstStale = pair.Item2 ?? 0;
					}

					stale++;
				}
			}

			if (stale > 0)
			{
				builder.Notify(
					$"SortEntitiesTable {this.CadObject.Handle}: {stale} of {this.Values.Count} entries refer to entities that are not in the drawing (first: {firstStale}); dropped, as AutoCAD does",
					NotificationType.Warning);
			}
		}
	}
}
