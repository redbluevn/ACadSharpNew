using System;
using System.Collections.Generic;
using System.Globalization;

namespace ACadSharp.Objects
{
	/// <summary>
	/// Creates the set of <see cref="VisualStyle"/> objects that AutoCAD stores in the
	/// <see cref="CadDictionary.AcadVisualStyle"/> dictionary of every new drawing.
	/// </summary>
	/// <remarks>
	/// A DWG written without these objects makes viewers fall back to an undefined visual style,
	/// which shows up as missing geometry in DWG TrueView and as a blank page in Autodesk Inventor.
	/// The values come from a drawing created by AutoCAD and are identical in the files written by
	/// AutoCAD 2013 to 2027; they are stored positionally, see <see cref="VisualStyle.Properties"/>.
	/// </remarks>
	public static class DefaultVisualStyles
	{
		/// <summary>
		/// Creates the 24 visual styles of a new drawing, in the order AutoCAD writes them.
		/// </summary>
		public static IEnumerable<VisualStyle> Create()
		{
			return new List<VisualStyle>
			{
			createFlat(),
			createFlatwithedges(),
			createGouraud(),
			createGouraudwithedges(),
			create2Dwireframe(),
			createWireframe(),
			createHidden(),
			createBasic(),
			createRealistic(),
			createConceptual(),
			createDim(),
			createBrighten(),
			createThicken(),
			createLinepattern(),
			createFacepattern(),
			createColorchange(),
			createJitteroff(),
			createOverhangoff(),
			createEdgecoloroff(),
			createShadesOfGray(),
			createSketchy(),
			createXRay(),
			createShadedWithEdges(),
			createShaded(),
			};
		}

		/// <summary>
		/// Adds the styles created by <see cref="Create"/> to a dictionary, keeping the entries
		/// that are already there.
		/// </summary>
		/// <param name="dictionary">Dictionary to fill, usually the ACAD_VISUALSTYLE entry of the root dictionary.</param>
		public static void AddDefaults(CadDictionary dictionary)
		{
			if (dictionary == null)
			{
				throw new ArgumentNullException(nameof(dictionary));
			}

			foreach (VisualStyle style in Create())
			{
				dictionary.TryAdd(style);
			}
		}

		/// <summary>
		/// Builds a style out of the positional value list, checking each value against the type
		/// the layout expects so a wrong entry fails here instead of corrupting the written file.
		/// </summary>
		/// <param name="name">Name of the style.</param>
		/// <param name="type">Type of the style, DXF group code 70.</param>
		/// <param name="internalFlag">Internal use only flag, DXF group code 291.</param>
		/// <param name="flags">One character per property with its flag (DXF group code 176).</param>
		/// <param name="values">The <see cref="VisualStyle.PropertyCount"/> values, in file order.</param>
		private static VisualStyle create(string name, int type, bool internalFlag, string flags, params object[] values)
		{
			if (values.Length != VisualStyle.PropertyCount || flags.Length != VisualStyle.PropertyCount)
			{
				throw new ArgumentException($"A visual style needs {VisualStyle.PropertyCount} values and flags, {name} has {values.Length} values and {flags.Length} flags.");
			}

			VisualStyle style = new VisualStyle
			{
				Name = name,
				Type = type,
				InternalFlag = internalFlag,
			};

			for (int i = 0; i < VisualStyle.PropertyCount; i++)
			{
				VisualStylePropertyType expected = VisualStyle.GetPropertyType(i);
				object value = values[i];
				VisualStylePropertyType actual;
				switch (value)
				{
					case bool _:
						actual = VisualStylePropertyType.Boolean;
						break;
					case int _:
						actual = VisualStylePropertyType.Integer;
						break;
					case double _:
						actual = VisualStylePropertyType.Double;
						break;
					case Color _:
						actual = VisualStylePropertyType.Color;
						break;
					case string _:
						actual = VisualStylePropertyType.String;
						break;
					default:
						throw new ArgumentException($"Unsupported value type {value?.GetType().Name} at index {i} of the visual style {name}.");
				}

				if (actual != expected)
				{
					throw new ArgumentException($"The visual style {name} has a {actual} at index {i}, the layout expects a {expected}.");
				}

				short flag = short.Parse(flags[i].ToString(), CultureInfo.InvariantCulture);
				style.Properties.Add(new VisualStyleProperty(expected, value, flag));
			}

			style.ApplyPropertyList();

			return style;
		}

		/// <summary>
		/// Creates the <c>Flat</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createFlat()
		{
			return create(
				"Flat",
				0,
				true,
				"1111111111111111111111111111111111111111111111111110011111",
				2, 1, 1, 2,
				0.6, 30.0, new Color(255, 255, 255), 0,
				0, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 8, new Color((short)7),
				1.0, 1, 6, 2,
				new Color((short)7), 5, 0, 0,
				false, 13, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>FlatWithEdges</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createFlatwithedges()
		{
			return create(
				"FlatWithEdges",
				1,
				true,
				"1111111111111111111111111111111111111111111111111110011111",
				2, 1, 1, 2,
				0.6, 30.0, new Color(255, 255, 255), 1,
				0, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 0, Color.ByEntity,
				1.0, 1, 6, 2,
				new Color((short)7), 5, 0, 0,
				false, 13, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>Gouraud</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createGouraud()
		{
			return create(
				"Gouraud",
				2,
				true,
				"1111111111111111111111111111111111111111111111111110011111",
				2, 2, 1, 2,
				0.6, 30.0, new Color(255, 255, 255), 0,
				0, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 0, new Color((short)7),
				1.0, 1, 6, 2,
				new Color((short)7), 5, 0, 0,
				false, 13, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>GouraudWithEdges</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createGouraudwithedges()
		{
			return create(
				"GouraudWithEdges",
				3,
				true,
				"1111111111111111111111111111111111111111111111111110011111",
				2, 2, 1, 2,
				0.6, 30.0, new Color(255, 255, 255), 1,
				0, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 0, Color.ByEntity,
				1.0, 1, 6, 2,
				new Color((short)7), 5, 0, 0,
				false, 13, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>2dWireframe</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle create2Dwireframe()
		{
			return create(
				"2dWireframe",
				4,
				false,
				"1111111111111111111111111111111111111111111111111110011111",
				0, 2, 1, 0,
				0.6, 30.0, new Color(255, 255, 255), 1,
				4, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 0, Color.ByEntity,
				1.0, 1, 6, 2,
				new Color((short)7), 5, 0, 0,
				false, 1, 0.0, 0,
				true, true, false, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>Wireframe</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createWireframe()
		{
			return create(
				"Wireframe",
				5,
				false,
				"1111111111111111111111111111111111111111111111111110011111",
				0, 2, 0, 0,
				0.6, 30.0, new Color(255, 255, 255), 1,
				4, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 0, Color.ByEntity,
				1.0, 1, 6, 2,
				new Color((short)7), 3, 0, 0,
				false, 1, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>Hidden</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createHidden()
		{
			return create(
				"Hidden",
				6,
				false,
				"1111111111111111111111111111111111111111111111111110011111",
				1, 2, 2, 0,
				0.6, 30.0, new Color(255, 255, 255), 2,
				2, new Color((short)7), Color.ByEntity, 2,
				1, 40.0, 0, Color.ByEntity,
				1.0, 1, 6, 2,
				new Color((short)7), 3, 0, 0,
				false, 1, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>Basic</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createBasic()
		{
			return create(
				"Basic",
				7,
				true,
				"1111111111111111111111111111111111111111111111111110011111",
				1, 0, 1, 0,
				0.6, 30.0, new Color(255, 255, 255), 0,
				4, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 8, new Color((short)7),
				1.0, 1, 6, 2,
				new Color((short)7), 5, 0, 0,
				false, 1, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>Realistic</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createRealistic()
		{
			return create(
				"Realistic",
				8,
				false,
				"1111111111111111111111111111111111111111111111111110011111",
				2, 3, 0, 2,
				0.6, 30.0, new Color(255, 255, 255), 0,
				0, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 8, Color.ByEntity,
				1.0, 1, 6, 2,
				new Color((short)7), 3, 0, 0,
				false, 13, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>Conceptual</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createConceptual()
		{
			return create(
				"Conceptual",
				9,
				false,
				"1111111111111111111111111111111111111111111111111110011111",
				3, 2, 1, 0,
				0.6, 30.0, new Color(255, 255, 255), 2,
				2, new Color((short)7), Color.ByEntity, 1,
				1, 40.0, 8, new Color((short)7),
				1.0, 1, 6, 2,
				new Color((short)7), 3, 0, 0,
				false, 1, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>Dim</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createDim()
		{
			return create(
				"Dim",
				11,
				true,
				"1111111111111111111111111111111111111111111111111110011111",
				2, 2, 1, 0,
				0.6, 30.0, new Color(255, 255, 255), 1,
				4, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 8, new Color((short)7),
				1.0, 1, 6, 2,
				new Color((short)7), 5, 0, 0,
				false, 1, -50.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>Brighten</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createBrighten()
		{
			return create(
				"Brighten",
				12,
				true,
				"1111111111111111111111111111111111111111111111111110011111",
				2, 2, 1, 0,
				0.6, 30.0, new Color(255, 255, 255), 1,
				4, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 8, new Color((short)7),
				1.0, 1, 6, 2,
				new Color((short)7), 5, 0, 0,
				false, 1, 50.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>Thicken</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createThicken()
		{
			return create(
				"Thicken",
				13,
				true,
				"1111111111111111111111111111111111111111111111111110011111",
				2, 2, 1, 0,
				0.6, 30.0, new Color(255, 255, 255), 1,
				4, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 12, new Color((short)7),
				1.0, 1, 6, 2,
				new Color((short)7), 5, 0, 0,
				false, 1, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>Linepattern</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createLinepattern()
		{
			return create(
				"Linepattern",
				14,
				true,
				"1111111111111111111111111111111111111111111111111110011111",
				2, 2, 1, 0,
				0.6, 30.0, new Color(255, 255, 255), 1,
				4, new Color((short)7), Color.ByEntity, 7,
				7, 1.0, 8, new Color((short)7),
				1.0, 1, 6, 2,
				new Color((short)7), 5, 0, 0,
				false, 1, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>Facepattern</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createFacepattern()
		{
			return create(
				"Facepattern",
				15,
				true,
				"1111111111111111111111111111111111111111111111111110011111",
				2, 2, 1, 0,
				0.6, 30.0, new Color(255, 255, 255), 1,
				4, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 8, new Color((short)7),
				1.0, 1, 6, 2,
				new Color((short)7), 5, 0, 0,
				false, 1, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>ColorChange</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createColorchange()
		{
			return create(
				"ColorChange",
				16,
				true,
				"1111111111111111111111111111111111111111111111111110011111",
				2, 2, 3, 0,
				0.6, 30.0, new Color(128, 128, 128), 1,
				4, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 8, new Color(128, 128, 128),
				1.0, 1, 6, 2,
				new Color((short)7), 5, 0, 0,
				false, 1, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>JitterOff</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createJitteroff()
		{
			return create(
				"JitterOff",
				20,
				true,
				"0000000000000020000000000000111111111111111111111110011111",
				2, 2, 0, 0,
				0.6, 30.0, new Color(255, 255, 255), 1,
				4, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 10, new Color((short)7),
				1.0, 1, 6, 2,
				new Color((short)7), 5, 0, 0,
				false, 1, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>OverhangOff</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createOverhangoff()
		{
			return create(
				"OverhangOff",
				21,
				true,
				"0000000000000020000000000000111111111111111111111110011111",
				2, 2, 0, 0,
				0.6, 30.0, new Color(255, 255, 255), 1,
				4, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 9, new Color((short)7),
				1.0, 1, 6, 2,
				new Color((short)7), 5, 0, 0,
				false, 1, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>EdgeColorOff</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createEdgecoloroff()
		{
			return create(
				"EdgeColorOff",
				22,
				true,
				"0000000000000020000000000000111111111111111111111110011111",
				2, 2, 0, 0,
				0.6, 30.0, new Color(255, 255, 255), 1,
				4, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 8, new Color((short)7),
				1.0, 1, 6, 2,
				new Color((short)7), 5, 0, 0,
				false, 1, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>Shades of Gray</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createShadesOfGray()
		{
			return create(
				"Shades of Gray",
				23,
				false,
				"1111111111111111111111111111111111111111111111111110011111",
				2, 2, 3, 0,
				0.6, 30.0, new Color(255, 255, 255), 2,
				2, new Color((short)7), new Color((short)7), 1,
				1, 40.0, 8, new Color((short)7),
				1.0, 1, 6, 2,
				new Color((short)7), 3, 0, 0,
				false, 1, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>Sketchy</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createSketchy()
		{
			return create(
				"Sketchy",
				24,
				false,
				"1111111111111111111111111111111111111111111111111110011111",
				1, 2, 2, 0,
				0.6, 30.0, new Color(255, 255, 255), 2,
				2, new Color((short)7), new Color((short)7), 1,
				1, 40.0, 11, new Color((short)7),
				1.0, 1, 6, 2,
				new Color((short)7), 6, 0, 0,
				false, 1, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>X-Ray</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createXRay()
		{
			return create(
				"X-Ray",
				25,
				false,
				"1111111111111111111111111111111111111111111111111110011111",
				2, 2, 1, 1,
				0.5, 30.0, new Color(255, 255, 255), 1,
				0, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 8, new Color((short)7),
				1.0, 1, 6, 2,
				new Color((short)7), 3, 0, 0,
				false, 13, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>Shaded with edges</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createShadedWithEdges()
		{
			return create(
				"Shaded with edges",
				26,
				false,
				"1111111111111111111111111111111111111111111111111110011111",
				2, 2, 1, 2,
				0.6, 30.0, new Color(255, 255, 255), 1,
				2, new Color((short)7), Color.ByEntity, 2,
				1, 1.0, 8, Color.ByEntity,
				1.0, 1, 6, 2,
				new Color((short)7), 3, 0, 0,
				false, 5, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}

		/// <summary>
		/// Creates the <c>Shaded</c> visual style with the values AutoCAD writes in a new drawing.
		/// </summary>
		private static VisualStyle createShaded()
		{
			return create(
				"Shaded",
				27,
				false,
				"1111111111111111111111111111111111111111111111111110011111",
				2, 2, 1, 2,
				0.6, 30.0, new Color(255, 255, 255), 0,
				4, new Color((short)7), Color.ByEntity, 1,
				1, 1.0, 8, Color.ByEntity,
				1.0, 1, 6, 2,
				new Color(120, 120, 120), 3, 0, 0,
				false, 5, 0.0, 0,
				false, true, true, false,
				false, false, false, false,
				false, 50, 0.0, 1.0,
				0, new Color(0, 0, 0), 50, 3,
				new Color(0, 0, 255), false, 50, 50,
				50, false, 50, Color.ByLayer,
				1.0, 2, "strokes_ogs.tif", false,
				1.0, 1.0);
		}
	}
}
