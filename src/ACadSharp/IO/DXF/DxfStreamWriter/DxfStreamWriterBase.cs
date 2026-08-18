using CSMath;
using CSUtilities.Converters;
using System;

namespace ACadSharp.IO.DXF.DxfStreamWriter;

internal abstract class DxfStreamWriterBase : IDxfStreamWriter
{
	public bool WriteOptional { get; set; } = false;

	public abstract void Close();

	/// <inheritdoc/>
	public abstract void Dispose();

	public abstract void Flush();

	public void Write(DxfCode code, object value, DxfClassMap map = null)
	{
		this.Write((int)code, value, map);
	}

	public void Write(DxfCode code, CSMath.IVector value, DxfClassMap map = null)
	{
		this.Write((int)code, value, map);
	}

	public void Write(int code, CSMath.IVector value, DxfClassMap map = null)
	{
		for (int i = 0; i < value.Dimension; i++)
		{
			this.Write(code + i * 10, value[i], map);
		}
	}

	public void Write(int code, object value, DxfClassMap map = null)
	{
		if (value == null)
		{
			return;
		}

		if (map != null && map.DxfProperties.TryGetValue(code, out DxfProperty prop))
		{
			if (prop.ReferenceType.HasFlag(DxfReferenceType.Optional) && !WriteOptional)
			{
				return;
			}

			if (prop.ReferenceType.HasFlag(DxfReferenceType.IsAngle))
			{
				value = MathHelper.RadToDeg((double)value);
			}
		}

		this.writeDxfCode(code);

		if (value is string s)
		{
			s = s
				.Replace("^", "^ ")
				.Replace("\n", "^J")
				.Replace("\r", "^M")
				.Replace("\t", "^I");
			this.writeValue(code, s);
		}
		else
		{
			this.writeValue(code, value);
		}
	}

	public void WriteCmColor(int code, Color color, DxfClassMap map = null)
	{
		if (GroupCodeValue.TransformValue(code) == GroupCodeValueType.Int16)
		{
			//BS: Color Index
			this.Write(code, Convert.ToInt16(color.GetApproxIndex()));
		}
		else
		{
			//BL: the whole colour packed into one word, method byte on top. Writing 0xC1 for
			//every colour that is not a true colour said "by block" for all of them, and the
			//index went through a byte, so ByLayer (256) came out as 0 and ByEntity (257) as 1.
			this.Write(code, color.ToDxfColorWord(), map);
		}
	}

	public void WriteHandle(int code, IHandledCadObject value, DxfClassMap map = null)
	{
		if (value != null)
		{
			this.Write(code, value.Handle, map);
		}
	}

	public void WriteIfNotDefault<T>(int code, T value, T defaultValue, DxfClassMap map = null)
	{
		if (!value.Equals(defaultValue))
		{
			this.Write(code, value, map);
		}
	}

	public void WriteName(int code, INamedCadObject value, DxfClassMap map = null)
	{
		if (value != null)
		{
			this.Write(code, value.Name, map);
		}
	}

	public void WriteTrueColor(int code, Color color, DxfClassMap map = null)
	{
		byte[] arr = new byte[4];
		arr[0] = (byte)color.B;
		arr[1] = (byte)color.G;
		arr[2] = (byte)color.R;
		arr[3] = 0;

		this.Write(code, LittleEndianConverter.Instance.ToInt32(arr), map);
	}

	protected abstract void writeDxfCode(int code);

	protected abstract void writeValue(int code, object value);
}