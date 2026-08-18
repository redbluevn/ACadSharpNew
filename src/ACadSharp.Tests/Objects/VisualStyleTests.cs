using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.Objects;

public class VisualStyleTests
{
	/// <summary>
	/// Names and types of the styles AutoCAD creates in a new drawing, taken from a file written
	/// by AutoCAD (samples/sample_base/empty.dwg) and identical in the files of AutoCAD 2013 to 2027.
	/// </summary>
	public static readonly (string Name, int Type)[] ExpectedDefaults = new[]
	{
		("Flat", 0), ("FlatWithEdges", 1), ("Gouraud", 2), ("GouraudWithEdges", 3),
		("2dWireframe", 4), ("Wireframe", 5), ("Hidden", 6), ("Basic", 7),
		("Realistic", 8), ("Conceptual", 9), ("Dim", 11), ("Brighten", 12),
		("Thicken", 13), ("Linepattern", 14), ("Facepattern", 15), ("ColorChange", 16),
		("JitterOff", 20), ("OverhangOff", 21), ("EdgeColorOff", 22), ("Shades of Gray", 23),
		("Sketchy", 24), ("X-Ray", 25), ("Shaded with edges", 26), ("Shaded", 27),
	};

	[Fact]
	public void DefaultVisualStylesMatchTheStylesAutoCadCreates()
	{
		VisualStyle[] styles = DefaultVisualStyles.Create().ToArray();

		Assert.Equal(ExpectedDefaults.Length, styles.Length);
		foreach ((string name, int type) in ExpectedDefaults)
		{
			VisualStyle style = styles.SingleOrDefault(s => s.Name == name);
			Assert.NotNull(style);
			Assert.Equal(type, style.Type);
			Assert.Equal(VisualStyle.PropertyCount, style.Properties.Count);
		}
	}

	[Fact]
	public void EveryDefaultPropertyHasTheTypeTheLayoutExpects()
	{
		foreach (VisualStyle style in DefaultVisualStyles.Create())
		{
			for (int i = 0; i < VisualStyle.PropertyCount; i++)
			{
				Assert.Equal(VisualStyle.GetPropertyType(i), style.Properties[i].ValueType);
			}
		}
	}

	[Fact]
	public void NamedPropertiesFollowThePropertyList()
	{
		VisualStyle wireframe = DefaultVisualStyles.Create().Single(s => s.Name == VisualStyle.DefaultName);

		//Values of 2dWireframe as written by AutoCAD.
		Assert.Equal(FaceLightingModelType.Invisible, wireframe.FaceLightingModel);
		Assert.Equal(FaceLightingQualityType.PerVertexLighting, wireframe.FaceLightingQuality);
		Assert.Equal(FaceColorMode.ObjectColor, wireframe.FaceColorMode);
		Assert.Equal(0.6, wireframe.FaceOpacityLevel, 9);
		Assert.Equal(30.0, wireframe.FaceSpecularLevel, 9);
		Assert.Equal(EdgeStyleModel.Isolines, wireframe.EdgeStyleModel);
		Assert.Equal(1.0, wireframe.OpacityLevel, 9);
		Assert.Equal(1, wireframe.DisplaySettings);
	}

	[Fact]
	public void NewDocumentHasTheDefaultStylesAndTheViewportPointsToThem()
	{
		CadDocument doc = new CadDocument();

		Assert.True(doc.RootDictionary.TryGetEntry(CadDictionary.AcadVisualStyle, out CadDictionary dictionary));
		Assert.Equal(ExpectedDefaults.Length, dictionary.Count());
		Assert.True(dictionary.TryGetEntry(VisualStyle.DefaultName, out VisualStyle _));

		VPort active = doc.VPorts[VPort.DefaultName];
		Assert.NotNull(active.VisualStyle);
		Assert.Equal(VisualStyle.DefaultName, active.VisualStyle.Name);
	}

	[Theory]
	[InlineData(ACadVersion.AC1027)]
	[InlineData(ACadVersion.AC1032)]
	public void DwgRoundTripPreservesEveryProperty(ACadVersion version)
	{
		CadDocument doc = new CadDocument(version);
		doc.Entities.Add(new Line(XYZ.Zero, new XYZ(100, 50, 0)));

		MemoryStream ms = new MemoryStream();
		DwgWriter.Write(ms, doc);
		using MemoryStream readStream = new MemoryStream(ms.ToArray());
		CadDocument rt = DwgReader.Read(readStream);

		Assert.True(doc.RootDictionary.TryGetEntry(CadDictionary.AcadVisualStyle, out CadDictionary original));
		Assert.True(rt.RootDictionary.TryGetEntry(CadDictionary.AcadVisualStyle, out CadDictionary result));
		Assert.Equal(original.Count(), result.Count());

		foreach (VisualStyle style in original.OfType<VisualStyle>())
		{
			Assert.True(result.TryGetEntry(style.Name, out VisualStyle got), $"{style.Name} is missing after the round trip");
			Assert.Equal(style.Type, got.Type);
			Assert.Equal(style.InternalFlag, got.InternalFlag);
			Assert.Equal(style.PropertyListVersion, got.PropertyListVersion);
			Assert.Equal(style.Properties.Count, got.Properties.Count);

			for (int i = 0; i < style.Properties.Count; i++)
			{
				VisualStyleProperty expected = style.Properties[i];
				VisualStyleProperty actual = got.Properties[i];

				Assert.Equal(expected.ValueType, actual.ValueType);
				Assert.Equal(expected.Flag, actual.Flag);
				if (expected.ValueType == VisualStylePropertyType.Double)
				{
					Assert.Equal(expected.AsDouble(), actual.AsDouble(), 9);
				}
				else
				{
					Assert.Equal(expected.Value, actual.Value);
				}
			}
		}
	}

	[Theory]
	[InlineData(ACadVersion.AC1027)]
	[InlineData(ACadVersion.AC1032)]
	public void DwgRoundTripKeepsTheViewportVisualStyleReference(ACadVersion version)
	{
		CadDocument doc = new CadDocument(version);

		MemoryStream ms = new MemoryStream();
		DwgWriter.Write(ms, doc);
		using MemoryStream readStream = new MemoryStream(ms.ToArray());
		CadDocument rt = DwgReader.Read(readStream);

		VPort active = rt.VPorts[VPort.DefaultName];
		Assert.NotNull(active.VisualStyle);
		Assert.Equal(VisualStyle.DefaultName, active.VisualStyle.Name);
	}

	[Fact]
	public void DxfRoundTripPreservesEveryProperty()
	{
		CadDocument doc = new CadDocument(ACadVersion.AC1032);

		MemoryStream ms = new MemoryStream();
		DxfWriter.Write(ms, doc, false);
		using MemoryStream readStream = new MemoryStream(ms.ToArray());
		CadDocument rt = DxfReader.Read(readStream);

		Assert.True(doc.RootDictionary.TryGetEntry(CadDictionary.AcadVisualStyle, out CadDictionary original));
		Assert.True(rt.RootDictionary.TryGetEntry(CadDictionary.AcadVisualStyle, out CadDictionary result));
		Assert.Equal(original.Count(), result.Count());

		foreach (VisualStyle style in original.OfType<VisualStyle>())
		{
			Assert.True(result.TryGetEntry(style.Name, out VisualStyle got), $"{style.Name} is missing after the dxf round trip");
			Assert.Equal(style.Type, got.Type);
			Assert.Equal(style.InternalFlag, got.InternalFlag);
			Assert.Equal(style.Properties.Count, got.Properties.Count);

			for (int i = 0; i < style.Properties.Count; i++)
			{
				VisualStyleProperty expected = style.Properties[i];
				VisualStyleProperty actual = got.Properties[i];

				Assert.Equal(expected.ValueType, actual.ValueType);
				Assert.Equal(expected.Flag, actual.Flag);
				switch (expected.ValueType)
				{
					case VisualStylePropertyType.Double:
						Assert.Equal(expected.AsDouble(), actual.AsDouble(), 9);
						break;
					case VisualStylePropertyType.Color:
						//DXF stores the index and, for a true colour, the rgb value.
						Assert.Equal(expected.AsColor().IsTrueColor, actual.AsColor().IsTrueColor);
						break;
					default:
						Assert.Equal(expected.Value, actual.Value);
						break;
				}
			}
		}
	}

	[Fact]
	public void ReadsTheStylesOfAFileWrittenByAutoCad()
	{
		string path = Path.Combine(TestVariables.SamplesFolder, "sample_base", "empty.dwg");
		CadDocument doc = DwgReader.Read(path);

		Assert.True(doc.RootDictionary.TryGetEntry(CadDictionary.AcadVisualStyle, out CadDictionary dictionary));
		foreach ((string name, int type) in ExpectedDefaults)
		{
			Assert.True(dictionary.TryGetEntry(name, out VisualStyle style), $"{name} is missing");
			Assert.Equal(type, style.Type);
			Assert.Equal(VisualStyle.PropertyCount, style.Properties.Count);
		}

		//Values that must survive: 2dWireframe has a "none" colour (index 257) that is easy to lose.
		VisualStyle wireframe = (VisualStyle)dictionary[VisualStyle.DefaultName];
		Assert.Equal(Color.ByEntity, wireframe.Properties[10].AsColor());
		Assert.Equal("strokes_ogs.tif", wireframe.Properties[54].AsString());

		VPort active = doc.VPorts[VPort.DefaultName];
		Assert.NotNull(active.VisualStyle);
		Assert.Equal(VisualStyle.DefaultName, active.VisualStyle.Name);
	}

	[Fact]
	public void StylesOfAnAutoCadFileSurviveADwgRoundTrip()
	{
		string path = Path.Combine(TestVariables.SamplesFolder, "sample_base", "empty.dwg");
		CadDocument doc = DwgReader.Read(path);

		MemoryStream ms = new MemoryStream();
		DwgWriter.Write(ms, doc);
		using MemoryStream readStream = new MemoryStream(ms.ToArray());
		CadDocument rt = DwgReader.Read(readStream);

		doc.RootDictionary.TryGetEntry(CadDictionary.AcadVisualStyle, out CadDictionary original);
		Assert.True(rt.RootDictionary.TryGetEntry(CadDictionary.AcadVisualStyle, out CadDictionary result));

		foreach (VisualStyle style in original.OfType<VisualStyle>())
		{
			Assert.True(result.TryGetEntry(style.Name, out VisualStyle got), $"{style.Name} is missing after the round trip");
			for (int i = 0; i < style.Properties.Count; i++)
			{
				Assert.Equal(style.Properties[i].ValueType, got.Properties[i].ValueType);
				Assert.Equal(style.Properties[i].Flag, got.Properties[i].Flag);
				if (style.Properties[i].ValueType == VisualStylePropertyType.Double)
				{
					Assert.Equal(style.Properties[i].AsDouble(), got.Properties[i].AsDouble(), 9);
				}
				else
				{
					Assert.Equal(style.Properties[i].Value, got.Properties[i].Value);
				}
			}
		}
	}
}
