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
	/// The layout was read off a real section with <c>OracleDump acdsdump</c> rather than from a
	/// specification. What is written here is the smallest store that carries payloads: the six
	/// schemas AutoCAD always defines, one schema-data segment, and one data segment holding every
	/// payload inline. No thumbnails, no free-space map, no previous-save copy and no blob paging -
	/// each of those is optional to the reader, and none of them carries geometry.
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

		//Segment numbering. Zero is left unused, as it is in the files measured: a data index entry
		//with segment zero is a stub the reader skips.
		private const uint SchemaDataSegment = 1;

		private const uint SchemaIndexSegment = 2;

		private const uint SearchSegment = 3;

		private const uint DataIndexSegment = 4;

		private const uint DataSegment = 5;

		private const uint SegmentIndexSegment = 6;

		private const uint FreeSpaceSegment = 7;

		private const uint PreviousSaveSegment = 8;

		private const uint SegmentCount = 9;

		//Four eight-byte entries sit in front of the schemas in a schema-data segment, and the schema
		//index has to declare them or the reader will look for the schemas in the wrong place. Their
		//meaning is not known; the shape and the values are the ones every file measured carries.
		private static readonly uint[] UnknownSchemaPropertyFlags = { 1, 1, 1, 0 };

		private const int UnknownSchemaPropertySize = 8;

		//The property names of every schema, in one table shared by the single schema-data segment.
		private const int NameAcDbDsId = 0;

		private const int NameThumbnailData = 1;

		private const int NameTreatedAsObjectData = 2;

		private const int NameLegacy = 3;

		private const int NameIndexable = 4;

		private const int NameHandleAttribute = 5;

		private const int NameAsmData = 6;

		private static readonly string[] PropertyNames =
		{
			"AcDbDs::ID",
			"Thumbnail_Data",
			"AcDbDs::TreatedAsObjectData",
			"AcDbDs::Legacy",
			"AcDs:Indexable",
			"AcDbDs::HandleAttribute",
			"ASM_Data",
		};

		//The six schemas AutoCAD defines, in its order and numbering. Only the last one is used here,
		//but a file carrying only that one is not what AutoCAD writes, and the DXF form of this
		//section was measured to want the whole set too.
		private static readonly string[] SchemaNames =
		{
			"AcDb_Thumbnail_Schema",
			"AcDbDs::TreatedAsObjectDataSchema",
			"AcDbDs::LegacySchema",
			"AcDbDs::IndexedPropertySchema",
			"AcDbDs::HandleAttributeSchema",
			"AcDb3DSolid_ASM_Data",
		};

		private const int AsmSchemaIndex = 5;

		private readonly IList<Payload> _payloads;

		public DwgPrototype1bWriter(IList<Payload> payloads)
		{
			this._payloads = payloads;
		}

		/// <summary>
		/// The modeler geometry of a document that has a payload the data store should carry, in
		/// handle order - the order the search entry lists them in.
		/// </summary>
		public static IList<Payload> CollectPayloads(CadDocument document)
		{
			List<Payload> found = new();
			foreach (Tables.BlockRecord record in document.BlockRecords)
			{
				foreach (ModelerGeometry geometry in record.Entities.OfType<ModelerGeometry>())
				{
					if (geometry.AcisData != null && geometry.AcisData.Length > 0)
					{
						found.Add(new Payload(geometry.Handle, geometry.AcisData));
					}
				}
			}

			return found.OrderBy(p => p.Handle).ToList();
		}

		public MemoryStream Write()
		{
			byte[] schemaData = this.writeSchemaData();
			byte[] schemaIndex = this.writeSchemaIndex();
			byte[] search = this.writeSearch();
			byte[] dataIndex = this.writeDataIndex();
			byte[] data = this.writeData();

			//Offsets can only be filled in once every segment size is known, so the segments are
			//built first and the index that points at them last.
			Dictionary<uint, (ulong offset, uint size)> placed = new();
			int position = FileHeaderSize;
			void place(uint index, byte[] built)
			{
				placed[index] = ((ulong)position, (uint)built.Length);
				position += built.Length;
			}

			place(SchemaDataSegment, schemaData);
			place(SchemaIndexSegment, schemaIndex);
			place(SearchSegment, search);
			place(DataIndexSegment, dataIndex);
			place(DataSegment, data);

			byte[] freeSpace = writeFreeSpace();
			place(FreeSpaceSegment, freeSpace);

			ulong segmentIndexOffset = (ulong)position;
			byte[] segmentIndex = this.writeSegmentIndex(placed, segmentIndexOffset);
			position += segmentIndex.Length;

			//The previous-save copy repeats the file header, so it can only be built once the header
			//it repeats is known.
			byte[] previousSave = this.writePreviousSave(segmentIndexOffset, position);
			placed[PreviousSaveSegment] = ((ulong)position, (uint)previousSave.Length);
			int total = position + previousSave.Length;

			//Adding the previous-save segment moved the end of the file, so the index is rebuilt with
			//it in place. Its own size does not change, so no offset already written moves.
			segmentIndex = this.writeSegmentIndex(placed, segmentIndexOffset);

			MemoryStream stream = new();
			using BinaryWriter writer = new(stream, Encoding.ASCII, true);
			this.writeFileHeader(writer, segmentIndexOffset, total);
			writer.Write(schemaData);
			writer.Write(schemaIndex);
			writer.Write(search);
			writer.Write(dataIndex);
			writer.Write(data);
			writer.Write(freeSpace);
			writer.Write(segmentIndex);
			writer.Write(previousSave);
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
			writer.Write((int)PreviousSaveSegment);
			writer.Write(fileSize);
			writer.Write(0);
			writer.Write((int)FreeSpaceSegment);
			writer.Write(1);                            //One free-space entry
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

		private byte[] writeFreeSpace()
		{
			MemoryStream stream = new();
			using (BinaryWriter writer = new(stream, Encoding.ASCII, true))
			{
				writer.Write(0UL);
				//Nothing in this store is free: every segment is written full and padded to its
				//boundary. The map still has to exist, so it describes an empty region.
				writer.Write(0UL);
				writer.Write(0u);
			}

			return buildSegment("freesp", FreeSpaceSegment, stream.ToArray());
		}

		private byte[] writePreviousSave(ulong segmentIndexOffset, int fileSize)
		{
			MemoryStream stream = new();
			using (BinaryWriter writer = new(stream, Encoding.ASCII, true))
			{
				this.writeFileHeader(writer, segmentIndexOffset, fileSize);
			}

			return buildSegment("prvsav", PreviousSaveSegment, stream.ToArray());
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
			schema(writer, new ulong[] { 0, 1 }, new[]
			{
				property(NameAcDbDsId, 10, new byte[8], new byte[] { 3, 0, 0, 0, 0, 0, 0, 0 }),
				property(NameThumbnailData, 15),
			});
			mark(1);
			schema(writer, Array.Empty<ulong>(), new[] { property(NameTreatedAsObjectData, 1) });
			mark(2);
			schema(writer, Array.Empty<ulong>(), new[] { property(NameLegacy, 1) });
			mark(3);
			schema(writer, Array.Empty<ulong>(), new[] { property(NameIndexable, 1) });
			mark(4);
			schema(writer, Array.Empty<ulong>(), new[] { handleAttributeProperty() });
			mark(5);
			schema(writer, new ulong[] { 6, 4 }, new[]
			{
				property(NameAcDbDsId, 10, new byte[8], new byte[] { 5, 0, 0, 0, 0, 0, 0, 0 }),
				property(NameAsmData, 15),
			});
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
			MemoryStream stream = new();
			using (BinaryWriter writer = new(stream, Encoding.ASCII, true))
			{
				writeUnknownSchemaProperties(writer);
				this.writeSchemas(writer, null);

				align(writer, 16);

				writer.Write((uint)PropertyNames.Length);
				foreach (string name in PropertyNames)
				{
					writer.Write(Encoding.UTF8.GetBytes(name));
					writer.Write((byte)0);
				}
			}

			return buildSegment("schdat", SchemaDataSegment, stream.ToArray());
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

			MemoryStream stream = new();
			using (BinaryWriter writer = new(stream, Encoding.ASCII, true))
			{
				writer.Write((uint)SchemaNames.Length);
				writer.Write(0x55555555u);
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
					writer.Write((uint)i);
					writer.Write(SchemaDataSegment);
					writer.Write((uint)(i * UnknownSchemaPropertySize));
				}

				writer.Write(0u);

				align(writer, 16);

				writer.Write((uint)SchemaNames.Length);
				foreach (string name in SchemaNames)
				{
					writer.Write(Encoding.UTF8.GetBytes(name));
					writer.Write((byte)0);
				}
			}

			return buildSegment("schidx", SchemaIndexSegment, stream.ToArray());
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
					writer.Write(0u);
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
