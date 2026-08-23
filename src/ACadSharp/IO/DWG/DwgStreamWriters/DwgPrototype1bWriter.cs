using ACadSharp.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ACadSharp.IO.DWG.DwgStreamWriters
{
	/// <summary>
	/// Writes the <c>AcDb:AcDsPrototype_1b</c> section: the data store an R2013+ drawing keeps its
	/// ACIS geometry in.
	/// </summary>
	/// <remarks>
	/// From R2013 a region, solid or body does not carry its geometry: the entity says it has a
	/// payload and the payload lives here, keyed by the entity handle. Without this section those
	/// entities can only be written as shapes with no geometry, which is why they were left out of
	/// every R2013+ file this library produced.
	///
	/// The layout was read off real sections with <c>OracleDump acdsdump</c> rather than from a
	/// specification, and the one that mattered came from a drawing WBLOCKed out of an AutoCAD
	/// session: every store in a drawing AutoCAD has merely saved has been grown by earlier saves,
	/// with freed segments, a previous-save copy and a second schema-data segment. A WBLOCK writes
	/// one from scratch, and that is the shape reproduced here - the segment index first, six
	/// schemas in one schema-data segment, one data segment holding every payload inline, and no
	/// free-space map, previous-save copy or blob paging.
	///
	/// What kept AutoCAD from accepting a store this class built, through four earlier attempts, was
	/// two fields neither reader needed: the second word of a data record header, which AutoCAD
	/// writes as 1, and the system-data alignment of a segment header, which says where that
	/// segment's table of names begins. With the names unreachable AutoCAD cannot match a schema by
	/// name, so the record it holds belongs to nothing and the entity pointing at it is erased on
	/// load - which is exactly what its audit log said, and neither field changes what this library
	/// reads back.
	/// </remarks>
	internal class DwgPrototype1bWriter
	{
		/// <summary>Payload of one entity, keyed by the handle the entity is written with.</summary>
		public readonly struct Payload
		{
			public Payload(ulong handle, byte[] data)
			{
				this.Handle = handle;
				this.Data = data;
			}

			public byte[] Data { get; }

			public ulong Handle { get; }
		}

		//"jard" little-endian, the signature every section of this kind starts with.
		private const uint FileSignature = 0x6472616A;

		private const int FileHeaderSize = 128;

		//Every segment starts with one of these and is padded out to a multiple of the alignment.
		private const short SegmentSignature = unchecked((short)0xD5AC);

		private const int SegmentAlignment = 0x80;

		private const byte PaddingByte = 0x70;

		private const uint SegmentHeaderSize = 48;

		//The eight bytes that close a segment header in every file measured.
		private static readonly byte[] HeaderAlignmentBytes = { 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55 };

		private const int DataHeaderSize = 20;

		private const int SchemaDataRevision = 1;

		//Segment numbering and file order, both taken from a store AutoCAD built from scratch: a
		//drawing WBLOCKed out of a session, which is the only way to get AutoCAD to write a store
		//that has not been grown by repeated saves. The segment index comes first, right after the
		//file header, and there is no free-space map and no previous-save copy - a fresh store has
		//neither. Zero is left unused: a data index entry with segment zero is a stub the reader
		//skips.
		private const uint SegmentIndexSegment = 1;

		private const uint DataIndexSegment = 2;

		private const uint DataSegment = 3;

		private const uint SchemaIndexSegment = 4;

		private const uint SchemaDataSegment = 5;

		private const uint SearchSegment = 6;

		private const uint SegmentCount = 7;

		//Eight-byte entries sit in front of the schemas in a schema-data segment, and the schema index
		//has to declare them or the reader will look for the schemas in the wrong place. Their meaning
		//is not known, but their count is: AutoCAD writes **four per schema that has more than one
		//property**. A store with the thumbnail schema alone carries four; one that also has
		//AcDb3DSolid_ASM_Data carries eight. This store has both, so it needs eight - with four, the
		//two indices each of those schemas declares pointed past the end of the array.
		private static readonly uint[] UnknownSchemaPropertyFlags = { 1, 0, 1, 1, 1, 0, 1, 1 };

		//The identifiers AutoCAD puts on those entries in a freshly built store.
		private static readonly uint[] UnknownSchemaPropertyIds = { 0, 2, 5, 3, 4, 2, 5, 4 };

		private const int UnknownSchemaPropertySize = 8;

		//The property names, in the order the schemas that own them are written. Every schema names
		//its own properties: AcDbDs::ID appears once per schema that has one, never shared, which is
		//how every store AutoCAD writes does it.
		private const int NameAcDbDsId = 0;

		private const int NameThumbnailData = 1;

		private const int NameAsmId = 2;

		private const int NameAsmData = 3;

		private const int NameTreatedAsObjectData = 4;

		private const int NameLegacy = 5;

		private const int NameIndexable = 6;

		private const int NameHandleAttribute = 7;

		private static readonly string[] PropertyNames =
		{
			"AcDbDs::ID",
			"Thumbnail_Data",
			"AcDbDs::ID",
			"ASM_Data",
			"AcDbDs::TreatedAsObjectData",
			"AcDbDs::Legacy",
			"AcDs:Indexable",
			"AcDbDs::HandleAttribute",
		};

		//The six schemas AutoCAD defines, in the order and numbering a fresh store uses. Only the
		//second one carries anything here, but a store holding that one alone is not what AutoCAD
		//writes, and the DXF form of this section was measured to want the whole set too.
		private static readonly string[] SchemaNames =
		{
			"AcDb_Thumbnail_Schema",
			"AcDb3DSolid_ASM_Data",
			"AcDbDs::TreatedAsObjectDataSchema",
			"AcDbDs::LegacySchema",
			"AcDbDs::IndexedPropertySchema",
			"AcDbDs::HandleAttributeSchema",
		};

		private const int AsmSchemaIndex = 1;

		private readonly IList<Payload> _payloads;

		public DwgPrototype1bWriter(IList<Payload> payloads)
		{
			this._payloads = payloads;
		}

		/// <summary>
		/// The modeler geometry of a document that has a payload the data store should carry, in
		/// handle order - the order the search entry lists them in.
		/// </summary>
		public static IList<Payload> CollectPayloads(CadDocument document, ACadVersion version)
		{
			List<Payload> found = new();
			foreach (Tables.BlockRecord record in document.BlockRecords)
			{
				foreach (ModelerGeometry geometry in record.Entities.OfType<ModelerGeometry>())
				{
					if (KeepsGeometryInDataStore(geometry, version))
					{
						found.Add(new Payload(geometry.Handle, geometry.AcisData));
					}
				}
			}

			return found.OrderBy(p => p.Handle).ToList();
		}

		/// <summary>
		/// Whether the geometry of this object belongs in the data store rather than in the object
		/// itself, at the version being written.
		/// </summary>
		/// <remarks>
		/// The entity says so with a bit of its own, and the two have to agree: a bit set with no
		/// record behind it sends AutoCAD looking for geometry that is not there, and a record with
		/// no bit in front of it is never looked for.
		/// </remarks>
		public static bool KeepsGeometryInDataStore(CadObject cadObject, ACadVersion version)
		{
			return version >= ACadVersion.AC1027
				&& cadObject is ModelerGeometry geometry
				&& geometry.AcisData != null
				&& geometry.AcisData.Length > 0
				&& geometry.IsBinaryAcisData;
		}

		public MemoryStream Write()
		{
			byte[] schemaData = this.writeSchemaData();
			byte[] schemaIndex = this.writeSchemaIndex();
			byte[] search = this.writeSearch();
			byte[] dataIndex = this.writeDataIndex();
			byte[] data = this.writeData();

			//The segment index comes first, so its own size has to be known before anything is
			//placed; it is fixed by the entry count.
			ulong segmentIndexOffset = FileHeaderSize;
			Dictionary<uint, (ulong offset, uint size)> placed = new();
			int position = FileHeaderSize + segmentIndexSize();
			void place(uint index, byte[] built)
			{
				placed[index] = ((ulong)position, (uint)built.Length);
				position += built.Length;
			}

			place(DataIndexSegment, dataIndex);
			place(DataSegment, data);
			place(SchemaIndexSegment, schemaIndex);
			place(SchemaDataSegment, schemaData);
			place(SearchSegment, search);

			byte[] segmentIndex = this.writeSegmentIndex(placed, segmentIndexOffset);

			MemoryStream stream = new();
			using BinaryWriter writer = new(stream, Encoding.ASCII, true);
			this.writeFileHeader(writer, segmentIndexOffset, position);
			writer.Write(segmentIndex);
			writer.Write(dataIndex);
			writer.Write(data);
			writer.Write(schemaIndex);
			writer.Write(schemaData);
			writer.Write(search);
			return stream;
		}

		private void writeFileHeader(BinaryWriter writer, ulong segmentIndexOffset, int fileSize)
		{
			writer.Write(FileSignature);
			writer.Write((short)FileHeaderSize);
			writer.Write((short)1);                     //Flag, 1 in every file measured
			writer.Write(8);                            //8 in every file measured
			writer.Write(2);                            //Version
			writer.Write(0);
			writer.Write(SchemaDataRevision);
			writer.Write((int)segmentIndexOffset);
			writer.Write(0);
			writer.Write((int)SegmentCount);
			writer.Write((int)SchemaIndexSegment);
			writer.Write((int)DataIndexSegment);
			writer.Write((int)SearchSegment);
			writer.Write(0);                            //No previous save: this store is new
			writer.Write(fileSize);
			writer.Write(0);
			writer.Write(0);                            //No free-space map, and so no entries
			writer.Write(0);
			writer.Write(0);
			writer.Write(new byte[FileHeaderSize - 72]);
		}

		/// <summary>
		/// Wraps segment content in its header and pads the whole thing to the segment alignment.
		/// </summary>
		private static byte[] buildSegment(string name, uint index, byte[] content, int objectDataAlignmentOffset = 0, int systemDataAlignmentOffset = 0)
		{
			int total = (int)SegmentHeaderSize + content.Length;
			int padded = (total + SegmentAlignment - 1) / SegmentAlignment * SegmentAlignment;

			byte[] result = new byte[padded];
			using (MemoryStream stream = new(result, true))
			using (BinaryWriter writer = new(stream, Encoding.ASCII, true))
			{
				writer.Write(SegmentSignature);
				writer.Write(Encoding.ASCII.GetBytes(name.PadRight(6, (char)0)), 0, 6);
				writer.Write(index);
				writer.Write(0);                        //Not a blob
				writer.Write((uint)padded);
				writer.Write(0);
				writer.Write(SchemaDataRevision);
				writer.Write(0);
				writer.Write(systemDataAlignmentOffset);
				writer.Write(objectDataAlignmentOffset);
				writer.Write(HeaderAlignmentBytes);
				writer.Write(content);
			}

			for (int i = total; i < padded; i++)
			{
				result[i] = PaddingByte;
			}

			return result;
		}

		private byte[] writeSegmentIndex(Dictionary<uint, (ulong offset, uint size)> placed, ulong segmentIndexOffset)
		{
			//The index has an entry for every segment number, whether or not it is used; an entry of
			//zero size is a slot the reader skips.
			MemoryStream stream = new();
			using (BinaryWriter writer = new(stream, Encoding.ASCII, true))
			{
				for (uint i = 0; i < SegmentCount; i++)
				{
					if (i == SegmentIndexSegment)
					{
						//The index describes itself, and its own size is fixed by the entry count.
						writer.Write(segmentIndexOffset);
						writer.Write((uint)segmentIndexSize());
						continue;
					}

					if (placed.TryGetValue(i, out var entry))
					{
						writer.Write(entry.offset);
						writer.Write(entry.size);
					}
					else
					{
						writer.Write(0UL);
						writer.Write(0U);
					}
				}
			}

			return buildSegment("segidx", SegmentIndexSegment, stream.ToArray());
		}

		private static int segmentIndexSize()
		{
			int content = (int)SegmentCount * 12;
			int total = (int)SegmentHeaderSize + content;
			return (total + SegmentAlignment - 1) / SegmentAlignment * SegmentAlignment;
		}

		private void writeSchemas(BinaryWriter writer, int[] offsets)
		{
			void mark(int i)
			{
				if (offsets != null)
				{
					offsets[i] = (int)writer.BaseStream.Position;
				}
			}

			mark(0);
			schema(writer, new ulong[] { 0, 2 }, new[]
			{
				property(NameAcDbDsId, 10, new byte[] { 3, 0, 0, 0, 0, 0, 0, 0 }, new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }),
				property(NameThumbnailData, 15),
			});
			mark(1);
			schema(writer, new ulong[] { 4, 7 }, new[]
			{
				property(NameAsmId, 10, new byte[] { 6, 0, 0, 0, 0, 0, 0, 0 }, new byte[] { 5, 0, 0, 0, 0, 0, 0, 0 }),
				property(NameAsmData, 15),
			});
			mark(2);
			schema(writer, Array.Empty<ulong>(), new[] { property(NameTreatedAsObjectData, 1) });
			mark(3);
			schema(writer, Array.Empty<ulong>(), new[] { property(NameLegacy, 1) });
			mark(4);
			schema(writer, Array.Empty<ulong>(), new[] { property(NameIndexable, 1) });
			mark(5);
			schema(writer, Array.Empty<ulong>(), new[] { handleAttributeProperty() });
		}

		private static void writeUnknownSchemaProperties(BinaryWriter writer)
		{
			foreach (uint flags in UnknownSchemaPropertyFlags)
			{
				writer.Write((uint)UnknownSchemaPropertySize);
				writer.Write(flags);
			}
		}

		private byte[] writeSchemaData()
		{
			int names;
			MemoryStream stream = new();
			using (BinaryWriter writer = new(stream, Encoding.ASCII, true))
			{
				writeUnknownSchemaProperties(writer);
				this.writeSchemas(writer, null);

				align(writer, 16);
				names = nameTableAlignment(writer);

				writer.Write((uint)PropertyNames.Length);
				foreach (string name in PropertyNames)
				{
					writer.Write(Encoding.UTF8.GetBytes(name));
					writer.Write((byte)0);
				}
			}

			return buildSegment("schdat", SchemaDataSegment, stream.ToArray(), systemDataAlignmentOffset: names);
		}

		/// <summary>
		/// Where the table of names begins in a segment, in sixteen-byte units from the start of the
		/// segment header.
		/// </summary>
		/// <remarks>
		/// A segment header carries this as its system-data alignment, and it was written as zero
		/// here. That is what kept AutoCAD from accepting a store this writer built: with the names
		/// unreachable it cannot match a schema by name, so the record it holds belongs to nothing
		/// and the entity that points at it is erased on load.
		/// </remarks>
		private static int nameTableAlignment(BinaryWriter writer)
		{
			return (int)(((uint)SegmentHeaderSize + (uint)writer.BaseStream.Position) / 16);
		}

		/// <summary>The byte offset of each schema inside the schema-data segment content.</summary>
		private int[] schemaOffsets()
		{
			int[] offsets = new int[SchemaNames.Length];
			using MemoryStream stream = new();
			using BinaryWriter writer = new(stream, Encoding.ASCII, true);
			writeUnknownSchemaProperties(writer);
			this.writeSchemas(writer, offsets);
			return offsets;
		}

		private byte[] writeSchemaIndex()
		{
			int[] offsets = this.schemaOffsets();

			int names;
			MemoryStream stream = new();
			using (BinaryWriter writer = new(stream, Encoding.ASCII, true))
			{
				writer.Write((uint)SchemaNames.Length);
				writer.Write(0u);
				for (int i = 0; i < SchemaNames.Length; i++)
				{
					writer.Write((uint)i);              //The schema's own number
					writer.Write(SchemaDataSegment);    //The segment its definition sits in
					writer.Write((uint)offsets[i]);
				}

				//Four bytes, not eight: the reader calls this a raw long but reads an int.
				writer.Write(0xAF10C);                  //Constant in every file measured
				writer.Write(0u);                       //No property entries
				writer.Write((uint)UnknownSchemaPropertyFlags.Length);
				for (int i = 0; i < UnknownSchemaPropertyFlags.Length; i++)
				{
					writer.Write(UnknownSchemaPropertyIds[i]);
					writer.Write(SchemaDataSegment);
					writer.Write((uint)(i * UnknownSchemaPropertySize));
				}

				writer.Write(3u);

				align(writer, 16);
				names = nameTableAlignment(writer);

				writer.Write((uint)SchemaNames.Length);
				foreach (string name in SchemaNames)
				{
					writer.Write(Encoding.UTF8.GetBytes(name));
					writer.Write((byte)0);
				}
			}

			return buildSegment("schidx", SchemaIndexSegment, stream.ToArray(), systemDataAlignmentOffset: names);
		}

		private byte[] writeSearch()
		{
			MemoryStream stream = new();
			using (BinaryWriter writer = new(stream, Encoding.ASCII, true))
			{
				//One entry, for the schema that carries geometry. A schema with no data of its own
				//gets no entry, which is what the files measured do.
				writer.Write(1);
				writer.Write((uint)AsmSchemaIndex);

				writer.Write((ulong)this._payloads.Count);
				for (int i = 0; i < this._payloads.Count; i++)
				{
					writer.Write((ulong)i);
				}

				writer.Write(1u);                       //One list of ids
				writer.Write(0u);
				writer.Write((uint)this._payloads.Count);
				for (int i = 0; i < this._payloads.Count; i++)
				{
					writer.Write(this._payloads[i].Handle);
					writer.Write(1UL);
					writer.Write((ulong)i);
				}
			}

			return buildSegment("search", SearchSegment, stream.ToArray());
		}

		private byte[] writeDataIndex()
		{
			MemoryStream stream = new();
			using (BinaryWriter writer = new(stream, Encoding.ASCII, true))
			{
				writer.Write(this._payloads.Count);
				writer.Write(0);
				for (int i = 0; i < this._payloads.Count; i++)
				{
					writer.Write(DataSegment);
					writer.Write((uint)(i * DataHeaderSize));
					writer.Write((uint)AsmSchemaIndex);
				}
			}

			return buildSegment("datidx", DataIndexSegment, stream.ToArray());
		}

		private byte[] writeData()
		{
			//The records start at a 16-byte multiple far enough in to clear every header.
			int headerBytes = (int)SegmentHeaderSize + this._payloads.Count * DataHeaderSize;
			int alignmentUnits = (headerBytes + 15) / 16;
			int dataStart = alignmentUnits * 16;

			MemoryStream stream = new();
			using (BinaryWriter writer = new(stream, Encoding.ASCII, true))
			{
				int offset = 0;
				foreach (Payload payload in this._payloads)
				{
					writer.Write((uint)DataHeaderSize);
					writer.Write(1u);
					writer.Write(payload.Handle);
					writer.Write((uint)offset);
					offset += 4 + payload.Data.Length;
				}

				//Pad from the end of the headers to where the records begin.
				int written = (int)SegmentHeaderSize + (int)stream.Position;
				for (int i = written; i < dataStart; i++)
				{
					writer.Write(PaddingByte);
				}

				foreach (Payload payload in this._payloads)
				{
					writer.Write((uint)payload.Data.Length);
					writer.Write(payload.Data);
				}
			}

			return buildSegment("_data_", DataSegment, stream.ToArray(), alignmentUnits);
		}

		private static void schema(BinaryWriter writer, ulong[] indices, SchemaProperty[] properties)
		{
			writer.Write((ushort)indices.Length);
			foreach (ulong index in indices)
			{
				writer.Write(index);
			}

			writer.Write((ushort)properties.Length);
			foreach (SchemaProperty prop in properties)
			{
				writer.Write(prop.Flags);
				writer.Write((uint)prop.NameIndex);
				if ((prop.Flags & (1 << 1)) == 0)
				{
					writer.Write(prop.Type);
				}

				if (prop.Flags == 1 || prop.Flags == 8)
				{
					writer.Write(prop.Unknown);
				}

				writer.Write((ushort)prop.Values.Length);
				foreach (byte[] value in prop.Values)
				{
					writer.Write(value);
				}
			}
		}

		private readonly struct SchemaProperty
		{
			public SchemaProperty(int nameIndex, uint type, uint flags, uint unknown, byte[][] values)
			{
				this.NameIndex = nameIndex;
				this.Type = type;
				this.Flags = flags;
				this.Unknown = unknown;
				this.Values = values;
			}

			public uint Flags { get; }

			public int NameIndex { get; }

			public uint Type { get; }

			public uint Unknown { get; }

			public byte[][] Values { get; }
		}

		private static SchemaProperty property(int nameIndex, uint type, params byte[][] values)
		{
			return new SchemaProperty(nameIndex, type, 0u, 0u, values ?? Array.Empty<byte[]>());
		}

		private static SchemaProperty handleAttributeProperty()
		{
			//The one property whose flags say 8, which puts an extra word in front of its values.
			return new SchemaProperty(NameHandleAttribute, 7u, 8u, 1u, new[] { new byte[] { 0 } });
		}

		private static void align(BinaryWriter writer, int boundary)
		{
			long over = writer.BaseStream.Position % boundary;
			if (over == 0)
			{
				return;
			}

			writer.Write(new byte[boundary - over]);
		}
	}
}
