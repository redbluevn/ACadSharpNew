using System;
using System.IO;

namespace ACadSharp.IO.DXF.DxfStreamWriter;

internal class DxfAsciiWriter : DxfStreamWriterBase
{
	public int? DecimalPrecision { get; set; } = null;

	private TextWriter _stream;
	private readonly System.Text.Encoding _encoding;
	private char[] _chunkBuffer;

	public DxfAsciiWriter(StreamWriter stream)
	{
		this._stream = stream;
		this._encoding = stream.Encoding;
	}

	public override void Close()
	{
		this._stream.Close();
	}

	public override void Dispose()
	{
		this._stream.Dispose();
	}

	public override void Flush()
	{
		this._stream.Flush();
	}

	protected override void writeDxfCode(int code)
	{
		if (code < 10)
		{
			this._stream.WriteLine("  {0}", code.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}
		else if (code < 100)
		{
			this._stream.WriteLine(" {0}", code.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}
		else
		{
			this._stream.WriteLine(code.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}
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
				this._stream.WriteLine(this._encoding.CodePage == 65001
					? value.ToString()
					: CadUtils.EscapeUnsupportedCharacters(value.ToString(), this._encoding));
				return;
			case GroupCodeValueType.Point3D:
			case GroupCodeValueType.Double:
			case GroupCodeValueType.ExtendedDataDouble:
				if (this.DecimalPrecision.HasValue)
				{
					string format = "0." + new string('#', this.DecimalPrecision.Value);
					this._stream.WriteLine(Convert.ToDouble(value)
						.ToString(format, System.Globalization.CultureInfo.InvariantCulture));
							return;
				}
				else
				{
					this._stream.WriteLine(Convert.ToDouble(value)
						.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
					return;
				}
			case GroupCodeValueType.Byte:
			case GroupCodeValueType.Int16:
			case GroupCodeValueType.ExtendedDataInt16:
				this._stream.WriteLine(Convert.ToInt16(value).ToString(System.Globalization.CultureInfo.InvariantCulture));
				return;
			case GroupCodeValueType.Int32:
			case GroupCodeValueType.ExtendedDataInt32:
				this._stream.WriteLine(Convert.ToInt32(value).ToString(System.Globalization.CultureInfo.InvariantCulture));
				return;
			case GroupCodeValueType.Int64:
				this._stream.WriteLine(Convert.ToInt64(value).ToString(System.Globalization.CultureInfo.InvariantCulture));
				return;
			case GroupCodeValueType.Handle:
			case GroupCodeValueType.ObjectId:
			case GroupCodeValueType.ExtendedDataHandle:
				this._stream.WriteLine(((ulong)value).ToString("X", System.Globalization.CultureInfo.InvariantCulture));
				return;
			case GroupCodeValueType.Bool:
				this._stream.WriteLine(Convert.ToInt16(value).ToString(System.Globalization.CultureInfo.InvariantCulture));
				return;
			case GroupCodeValueType.Chunk:
			case GroupCodeValueType.ExtendedDataChunk:
				const int maxChunkLength = 127;
				byte[] arr = value as byte[];
				if (arr.Length == 0)
				{
					this._stream.WriteLine(string.Empty);
					return;
				}

				const string hex = "0123456789ABCDEF";
				// Reuse one line of scratch space, including across separate chunk groups.
				// Large payloads must not buffer their entire hexadecimal representation.
				char[] buffer = this._chunkBuffer ??= new char[maxChunkLength * 2];
				for (int offset = 0; offset < arr.Length;)
				{
					int length = Math.Min(maxChunkLength, arr.Length - offset);
					for (int i = 0; i < length; i++)
					{
						byte b = arr[offset + i];
						buffer[i * 2] = hex[b >> 4];
						buffer[i * 2 + 1] = hex[b & 0xF];
					}
					if (offset != 0)
						this._stream.WriteLine(code);
					this._stream.WriteLine(buffer, 0, length * 2);
					offset += length;
				}
				return;
		}

		this._stream.WriteLine(value.ToString());
	}
}
