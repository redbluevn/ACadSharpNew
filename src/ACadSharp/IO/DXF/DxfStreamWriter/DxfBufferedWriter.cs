using CSMath;
using System;
using System.Collections.Generic;

namespace ACadSharp.IO.DXF.DxfStreamWriter;

/// <summary>
/// Holds back everything written to it until the caller says the record is complete, so a record
/// that fails half way through can be dropped instead of being left in the file as a fragment.
/// </summary>
/// <remarks>
/// The DWG writer gets this for free: it builds each object in a buffer and only copies it into the
/// file when the object is finished, so a failure part way through has nothing to undo. DXF writes
/// its codes straight to the text stream, so the same guarantee has to be built - hence this
/// decorator, which records the calls and replays them into the real writer on
/// <see cref="Commit"/>.
///
/// It records the CALLS, not formatted text: the inner writer keeps its own rules about how a value
/// is written, what <see cref="WriteOptional"/> suppresses and how a handle or a colour is spelled,
/// and replaying the call means those rules still decide. Formatting here would be a second copy of
/// them, and a second copy is how the two drift apart.
/// </remarks>
internal sealed class DxfBufferedWriter : IDxfStreamWriter
{
	private readonly IDxfStreamWriter _inner;

	private readonly List<Action<IDxfStreamWriter>> _calls = new();

	public DxfBufferedWriter(IDxfStreamWriter inner)
	{
		this._inner = inner;
		this.WriteOptional = inner.WriteOptional;
	}

	/// <inheritdoc/>
	public bool WriteOptional { get; set; }

	/// <summary>
	/// Replays everything recorded into the writer underneath, in order.
	/// </summary>
	public void Commit()
	{
		bool optional = this._inner.WriteOptional;
		this._inner.WriteOptional = this.WriteOptional;
		foreach (Action<IDxfStreamWriter> call in this._calls)
		{
			call(this._inner);
		}

		this._inner.WriteOptional = optional;
		this._calls.Clear();
	}

	/// <summary>
	/// Throws away everything recorded; the file never sees it.
	/// </summary>
	public void Discard()
	{
		this._calls.Clear();
	}

	/// <inheritdoc/>
	public void Close()
	{
		//The buffer never owns the stream: closing is the real writer's business, and doing it here
		//would end the file in the middle of a record.
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		this._calls.Clear();
	}

	/// <inheritdoc/>
	public void Flush()
	{
		//Nothing to flush: a buffered record reaches the stream through Commit, or not at all.
	}

	/// <inheritdoc/>
	public void Write(DxfCode code, object value, DxfClassMap map = null)
	{
		this._calls.Add(w => w.Write(code, value, map));
	}

	/// <inheritdoc/>
	public void Write(DxfCode code, IVector value, DxfClassMap map = null)
	{
		this._calls.Add(w => w.Write(code, value, map));
	}

	/// <inheritdoc/>
	public void Write(int code, object value, DxfClassMap map = null)
	{
		this._calls.Add(w => w.Write(code, value, map));
	}

	/// <inheritdoc/>
	public void Write(int code, IVector value, DxfClassMap map = null)
	{
		this._calls.Add(w => w.Write(code, value, map));
	}

	/// <inheritdoc/>
	public void WriteCmColor(int code, Color color, DxfClassMap map = null)
	{
		this._calls.Add(w => w.WriteCmColor(code, color, map));
	}

	/// <inheritdoc/>
	public void WriteHandle(int code, IHandledCadObject value, DxfClassMap map = null)
	{
		this._calls.Add(w => w.WriteHandle(code, value, map));
	}

	/// <inheritdoc/>
	public void WriteIfNotDefault<T>(int code, T value, T defaultValue, DxfClassMap map = null)
	{
		this._calls.Add(w => w.WriteIfNotDefault(code, value, defaultValue, map));
	}

	/// <inheritdoc/>
	public void WriteName(int code, INamedCadObject value, DxfClassMap map = null)
	{
		this._calls.Add(w => w.WriteName(code, value, map));
	}

	/// <inheritdoc/>
	public void WriteTrueColor(int code, Color color, DxfClassMap map = null)
	{
		this._calls.Add(w => w.WriteTrueColor(code, color, map));
	}
}
