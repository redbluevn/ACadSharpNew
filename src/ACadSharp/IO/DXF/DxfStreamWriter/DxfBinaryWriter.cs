using ACadSharp.Exceptions;
using ACadSharp.IO.DXF.DxfStreamWriter;
using System;
using System.IO;

namespace ACadSharp.IO.DXF
{
	internal class DxfBinaryWriter : DxfStreamWriterBase
	{
		private BinaryWriter _stream;

		public DxfBinaryWriter(BinaryWriter stream)
		{
			this._stream = stream;

			//string sentinel = "AutoCAD Binary DXF\r\n\0";
			byte[] sentinel = { 65, 117, 116, 111, 67, 65, 68, 32, 66, 105, 110, 97, 114, 121, 32, 68, 88, 70, 13, 10, 26, 0 };
			this._stream.Write(sentinel);
		}

		public override void Dispose()
		{
			this._stream.Dispose();
		}

		public override void Flush()
		{
			this._stream.Flush();
		}

		public override void Close()
		{
			this._stream.Close();
		}

		protected override void writeDxfCode(int code)
		{
			this._stream.Write((short)code);
		}

		protected override void writeValue(int code, object value)
		{
			GroupCodeValueType groupCode = GroupCodeValue.TransformValue(code);

			switch (groupCode)
			{
				case GroupCodeValueType.None:
					break;
				case GroupCodeValueType.String:
				case GroupCodeValueType.Comment:
				case GroupCodeValueType.ExtendedDataString:
					this._stream.Write(value.ToString().ToCharArray());
					this._stream.Write('\0');
					return;
				case GroupCodeValueType.Point3D:
				case GroupCodeValueType.Double:
				case GroupCodeValueType.ExtendedDataDouble:
					this._stream.Write(Convert.ToDouble(value));
					return;
				case GroupCodeValueType.Byte:
				case GroupCodeValueType.Int16:
				case GroupCodeValueType.ExtendedDataInt16:
					this._stream.Write(Convert.ToInt16(value));
					return;
				case GroupCodeValueType.Int32:
				case GroupCodeValueType.ExtendedDataInt32:
					this._stream.Write(Convert.ToInt32(value));
					return;
				case GroupCodeValueType.Int64:
					this._stream.Write(Convert.ToInt64(value));
					return;
				case GroupCodeValueType.Handle:
				case GroupCodeValueType.ObjectId:
				case GroupCodeValueType.ExtendedDataHandle:
					this._stream.Write(((ulong)value).ToString("X", System.Globalization.CultureInfo.InvariantCulture).ToCharArray());
					this._stream.Write('\0');
					return;
				case GroupCodeValueType.Bool:
					this._stream.Write(Convert.ToByte(value));
					return;
				case GroupCodeValueType.Chunk:
				case GroupCodeValueType.ExtendedDataChunk:
					const int maxChunkLength = 127;
					byte[] arr = value as byte[];
					int offset = 0;
					// Each chunk has its own group code and one-byte length. Casting the whole
					// payload length to byte corrupts the boundary of the following group.
					do
					{
						int length = Math.Min(maxChunkLength, arr.Length - offset);
						this._stream.Write((byte)length);
						this._stream.Write(arr, offset, length);
						offset += length;
						if (offset < arr.Length)
						{
							this.writeDxfCode(code);
						}
					}
					while (offset < arr.Length);
					return;
			}

			throw new DxfException($"Code: {code} doesn't belong to any GroupCodeValueType");
		}
	}
}
