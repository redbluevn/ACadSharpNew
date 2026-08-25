using ACadSharp.Entities;
using ACadSharp.IO;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO;

/// <summary>
/// A DWG stores a SHAPE entity by its number in the shape file, a DXF stores it by name, and for
/// as long as nobody opened the .shx both formats point at, a shape read from DWG could not be
/// written to DXF - the one in-scope loss the fidelity gate carried (T12). The reader now resolves
/// the names itself; the file layout was probe-verified against samples/test_shape.shx with the
/// name AutoCAD's own DXF export gives that shape as the ground truth.
/// </summary>
public class ShxShapeFileTests
{
	private static string shxPath => Path.Combine(TestVariables.SamplesFolder, "test_shape.shx");

	private static string dwgPath => Path.Combine(TestVariables.SamplesFolder, "sample_AC1032.dwg");

	[Fact]
	public void TheShapeFileNamesAreReadByNumber()
	{
		var names = ShxShapeFile.ReadShapeNames(shxPath);

		Assert.Single(names);
		Assert.Equal("MY-SHAPE", names[1]);
	}

	[Fact]
	public void AShapeReadFromDwgKnowsItsName()
	{
		//The style records the absolute path of the machine the drawing was made on, which exists
		//nowhere else - the resolver has to fall back to the file name next to the drawing.
		CadDocument doc = DwgReader.Read(dwgPath);

		Shape shape = doc.BlockRecords.SelectMany(b => b.Entities).OfType<Shape>().First();

		Assert.Equal("MY-SHAPE", shape.ShapeName);
	}

	[Fact]
	public void AShapeSurvivesTheTripToDxf()
	{
		//The loss this closes: before the resolver, the DXF writer left the entity out with a
		//notification, because writing a name the shape file does not hold makes AutoCAD refuse
		//the whole drawing.
		CadDocument doc = DwgReader.Read(dwgPath);

		using MemoryStream stream = new();
		using (DxfWriter writer = new(stream, doc, false))
		{
			writer.Write();
		}

		CadDocument back = DxfReader.Read(new MemoryStream(stream.ToArray()));
		Shape shape = back.BlockRecords.SelectMany(b => b.Entities).OfType<Shape>().FirstOrDefault();

		Assert.NotNull(shape);
		Assert.Equal("MY-SHAPE", shape.ShapeName);
	}

	[Fact]
	public void TheResolutionCanBeTurnedOff()
	{
		using DwgReader reader = new(dwgPath);
		reader.Configuration.ResolveShapeNames = false;
		CadDocument doc = reader.Read();

		Shape shape = doc.BlockRecords.SelectMany(b => b.Entities).OfType<Shape>().First();

		Assert.True(string.IsNullOrEmpty(shape.ShapeName));
	}
}
