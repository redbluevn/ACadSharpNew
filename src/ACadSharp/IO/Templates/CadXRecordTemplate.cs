using ACadSharp.Objects;
using System;

namespace ACadSharp.IO.Templates
{
	internal class CadXRecordTemplate : CadTemplate<XRecord>
	{
		private readonly System.Collections.Generic.List<Tuple<int, ulong, XRecord.Entry>> _entries = new();

		public CadXRecordTemplate() : base(new XRecord()) { }

		public CadXRecordTemplate(XRecord cadObject) : base(cadObject) { }

		/// <summary>
		/// Registers a handle that has to be resolved once the document is built.
		/// </summary>
		/// <param name="code">Group code of the entry.</param>
		/// <param name="handle">Handle of the referenced object.</param>
		/// <param name="entry">
		/// Entry already created in the position the record has in the file, its value is filled in
		/// by <see cref="build"/>. When it is null the entry is appended instead, which changes the
		/// order of the record and should only be used when the position is not known.
		/// </param>
		public void AddHandleReference(int code, ulong handle, XRecord.Entry entry = null)
		{
			_entries.Add(new Tuple<int, ulong, XRecord.Entry>(code, handle, entry));
		}

		protected override void build(CadDocumentBuilder builder)
		{
			base.build(builder);

			//A handle of 0 is a null reference AutoCAD writes on purpose; a non-zero handle that
			//resolves to nothing is stale, and AutoCAD saves it as 0 too (its own save of a production
			//drawing holds 20,022 such zeros where the original held 20,022 handles that name
			//nothing). The entry keeps its place with a null value either way - what differs is only
			//whether it is worth a word, and if so, one per record rather than one per entry.
			int stale = 0;
			ulong firstStale = 0;
			foreach (var entry in _entries)
			{
				if (builder.TryGetCadObject<CadObject>(entry.Item2, out CadObject obj))
				{
					if (entry.Item3 == null)
					{
						this.CadObject.CreateEntry(entry.Item1, obj);
					}
					else
					{
						entry.Item3.Value = obj;
					}
				}
				else if (entry.Item2 != 0)
				{
					if (stale == 0)
					{
						firstStale = entry.Item2;
					}

					stale++;
				}
			}

			if (stale > 0)
			{
				builder.Notify(
					$"XRecord {this.CadObject.Handle}: {stale} of {_entries.Count} handle entries refer to objects that are not in the drawing (first: {firstStale}); kept in place as null references, which is how AutoCAD saves them",
					NotificationType.Warning);
			}
		}
	}
}