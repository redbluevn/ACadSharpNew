using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ACadSharp.IO;

/// <summary>
/// Reads the directory of a compiled shape file (.shx): which shape name each shape number
/// carries. That is the one piece of the file this library needs - a DWG stores a SHAPE entity by
/// its number in the shape file, a DXF stores it by name, so translating between the two formats
/// means opening the file both of them point at.
/// </summary>
/// <remarks>
/// The layout was probe-verified against a real shape file rather than taken from a specification:
/// an ASCII header ending in 0x1A (<c>AutoCAD-86 shapes 1.0</c>), three words - first shape
/// number, last shape number, count - then <c>count</c> directory entries of (number, defBytes),
/// then each definition as a NUL-terminated name followed by its code. The arithmetic closes: in
/// the reference file the single definition of 0x16B bytes runs from the end of the directory to
/// the trailing "EOF" exactly. Font forms of the format (<c>unifont</c>, <c>bigfont</c>) do not
/// carry a name per glyph, so they yield an empty dictionary rather than a wrong one.
/// </remarks>
public static class ShxShapeFile
{
	/// <summary>
	/// Reads the shape names of a compiled shape file, keyed by shape number.
	/// </summary>
	public static Dictionary<ushort, string> ReadShapeNames(string path)
	{
		using FileStream stream = File.OpenRead(path);
		return ReadShapeNames(stream);
	}

	/// <summary>
	/// Reads the shape names of a compiled shape file, keyed by shape number.
	/// </summary>
	/// <returns>
	/// One entry per named shape. Empty when the file is a font form of the format, which carries
	/// no names.
	/// </returns>
	/// <exception cref="FormatException">The stream is not a compiled shape file.</exception>
	public static Dictionary<ushort, string> ReadShapeNames(Stream stream)
	{
		var names = new Dictionary<ushort, string>();

		//ASCII header up to the 0x1A end-of-text marker.
		var header = new StringBuilder();
		int b;
		while ((b = stream.ReadByte()) != 0x1A)
		{
			if (b < 0 || header.Length > 64)
			{
				throw new System.FormatException("not a compiled shape file: no header terminator");
			}

			header.Append((char)b);
		}

		if (!header.ToString().Contains("shapes"))
		{
			//A unifont or bigfont: glyphs are code points, there are no names to read.
			return names;
		}

		ushort readWord()
		{
			int lo = stream.ReadByte(), hi = stream.ReadByte();
			if (lo < 0 || hi < 0)
			{
				throw new System.FormatException("not a compiled shape file: truncated");
			}

			return (ushort)(lo | (hi << 8));
		}

		readWord(); //first shape number
		readWord(); //last shape number
		ushort count = readWord();

		var directory = new (ushort number, ushort defBytes)[count];
		for (int i = 0; i < count; i++)
		{
			directory[i] = (readWord(), readWord());
		}

		foreach ((ushort number, ushort defBytes) in directory)
		{
			//The definition is its name, NUL-terminated, then the shape code; defBytes counts both.
			var name = new StringBuilder();
			int consumed = 0;
			while (consumed < defBytes)
			{
				int c = stream.ReadByte();
				if (c < 0)
				{
					throw new System.FormatException("not a compiled shape file: truncated definition");
				}

				consumed++;
				if (c == 0)
				{
					break;
				}

				name.Append((char)c);
			}

			stream.Seek(defBytes - consumed, SeekOrigin.Current);
			names[number] = name.ToString();
		}

		return names;
	}
}
