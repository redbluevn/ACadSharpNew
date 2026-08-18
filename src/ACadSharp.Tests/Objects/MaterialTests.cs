using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.Objects;

public class MaterialTests
{
	[Fact]
	public void DefaultsAreReasonable()
	{
		Material material = new Material("Default");

		Assert.Equal("Default", material.Name);
		Assert.Equal(MaterialChannelFlags.UseDiffuse, material.ChannelFlags);
		Assert.Equal(MaterialIlluminationModel.BlinnShader, material.IlluminationModel);
		Assert.Equal(MaterialMode.Realistic, material.Mode);
	}

	[Fact]
	public void DwgRoundTripPreservesColorMethodsAndEnums()
	{
		CadDocument doc = new CadDocument(ACadVersion.AC1027);

		Material material = new Material("RoundTripMaterial")
		{
			Description = "Round-trip test",
			AmbientColorMethod = ColorMethod.Override,
			AmbientColor = new Color(10, 20, 30),
			DiffuseColorMethod = ColorMethod.Override,
			DiffuseColor = new Color(200, 100, 50),
			SpecularColorMethod = ColorMethod.Override,
			SpecularColor = new Color(255, 255, 255),
			SpecularGlossFactor = 0.5,
			Opacity = 0.8,
			RefractionIndex = 1.2,
			Translucence = 0.1,
			Reflectivity = 0.3,
			ChannelFlags = MaterialChannelFlags.UseDiffuse | MaterialChannelFlags.UseBump,
			IlluminationModel = MaterialIlluminationModel.MetalShader,
			Mode = MaterialMode.Advanced,
		};
		doc.Materials.Add(material);

		Mesh anchor = new Mesh
		{
			Material = material,
		};
		anchor.Vertices.Add(new XYZ(0, 0, 0));
		anchor.Vertices.Add(new XYZ(1, 0, 0));
		anchor.Vertices.Add(new XYZ(0, 1, 0));
		anchor.Faces.Add(new[] { 0, 1, 2 });
		doc.Entities.Add(anchor);

		MemoryStream ms = new MemoryStream();
		DwgWriter.Write(ms, doc);
		using MemoryStream readStream = new MemoryStream(ms.ToArray());
		CadDocument rt = DwgReader.Read(readStream);

		Assert.True(rt.Materials.TryGet("RoundTripMaterial", out Material got));
		Assert.Equal("Round-trip test", got.Description);
		Assert.Equal(ColorMethod.Override, got.AmbientColorMethod);
		Assert.Equal(new Color(10, 20, 30), got.AmbientColor);
		Assert.Equal(ColorMethod.Override, got.DiffuseColorMethod);
		Assert.Equal(new Color(200, 100, 50), got.DiffuseColor);
		Assert.Equal(ColorMethod.Override, got.SpecularColorMethod);
		Assert.Equal(new Color(255, 255, 255), got.SpecularColor);
		Assert.Equal(0.5, got.SpecularGlossFactor, 9);
		Assert.Equal(0.8, got.Opacity, 9);
		Assert.Equal(1.2, got.RefractionIndex, 9);
		Assert.Equal(0.1, got.Translucence, 9);
		Assert.Equal(0.3, got.Reflectivity, 9);
		Assert.Equal(MaterialChannelFlags.UseDiffuse | MaterialChannelFlags.UseBump, got.ChannelFlags);
		Assert.Equal(MaterialIlluminationModel.MetalShader, got.IlluminationModel);
		Assert.Equal(MaterialMode.Advanced, got.Mode);

		Mesh rtAnchor = rt.Entities.OfType<Mesh>().Single();
		Assert.NotNull(rtAnchor.Material);
		Assert.Equal("RoundTripMaterial", rtAnchor.Material.Name);
	}

	[Fact]
	public void NewDocumentWiresTheDefaultMaterials()
	{
		CadDocument doc = new CadDocument();

		//AutoCAD points every layer at "Global" and CMATERIAL at "ByLayer".
		Assert.True(doc.Materials.TryGet(Material.GlobalName, out Material global));
		Assert.True(doc.Materials.TryGet(Material.ByLayerName, out Material byLayer));
		Assert.True(doc.Materials.TryGet(Material.ByBlockName, out Material _));

		Assert.Same(global, doc.Layers[Layer.DefaultName].Material);
		Assert.Same(byLayer, doc.Header.CurrentMaterial);
	}

	[Theory]
	[InlineData(ACadVersion.AC1024)]
	[InlineData(ACadVersion.AC1032)]
	public void DwgRoundTripKeepsTheLayerAndHeaderMaterial(ACadVersion version)
	{
		CadDocument doc = new CadDocument(version);

		MemoryStream ms = new MemoryStream();
		DwgWriter.Write(ms, doc);
		using MemoryStream readStream = new MemoryStream(ms.ToArray());
		CadDocument rt = DwgReader.Read(readStream);

		Layer layer = rt.Layers[Layer.DefaultName];
		Assert.NotNull(layer.Material);
		Assert.Equal(Material.GlobalName, layer.Material.Name);

		Assert.NotNull(rt.Header.CurrentMaterial);
		Assert.Equal(Material.ByLayerName, rt.Header.CurrentMaterial.Name);
	}

	[Fact]
	public void ReadsTheLayerMaterialOfAFileWrittenByAutoCad()
	{
		string path = Path.Combine(TestVariables.SamplesFolder, "sample_base", "empty.dwg");
		CadDocument doc = DwgReader.Read(path);

		Layer layer = doc.Layers[Layer.DefaultName];
		Assert.NotNull(layer.Material);
		Assert.Equal(Material.GlobalName, layer.Material.Name);
	}

	[Fact]
	public void DwgRoundTripPreservesDiffuseMapMetadata()
	{
		CadDocument doc = new CadDocument(ACadVersion.AC1027);

		Material material = new Material("Textured")
		{
			DiffuseColorMethod = ColorMethod.Override,
			DiffuseColor = new Color(128, 128, 128),
			DiffuseMapSource = MapSource.UseImageFile,
			DiffuseMapFileName = "diffuse.png",
			DiffuseMapBlendFactor = 1.0,
		};
		doc.Materials.Add(material);

		MemoryStream ms = new MemoryStream();
		DwgWriter.Write(ms, doc);
		using MemoryStream readStream = new MemoryStream(ms.ToArray());
		CadDocument rt = DwgReader.Read(readStream);

		Assert.True(rt.Materials.TryGet("Textured", out Material got));
		Assert.Equal(MapSource.UseImageFile, got.DiffuseMapSource);
		Assert.Equal("diffuse.png", got.DiffuseMapFileName);
		Assert.Equal(1.0, got.DiffuseMapBlendFactor, 9);
	}
}