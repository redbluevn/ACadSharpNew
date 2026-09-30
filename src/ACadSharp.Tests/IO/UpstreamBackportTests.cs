using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.IO.DWG;
using ACadSharp.Header;
using CSMath;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO;

public class UpstreamBackportTests
{
	public static TheoryData<ACadVersion, SmoothSurfaceType, bool> PolylineCases
	{
		get
		{
			var cases = new TheoryData<ACadVersion, SmoothSurfaceType, bool>();
			foreach (var version in new[] { ACadVersion.AC1015, ACadVersion.AC1018, ACadVersion.AC1021, ACadVersion.AC1024, ACadVersion.AC1027, ACadVersion.AC1032 })
			foreach (var smooth in new[] { SmoothSurfaceType.NoSmooth, SmoothSurfaceType.Quadratic, SmoothSurfaceType.Cubic })
			foreach (bool closed in new[] { false, true })
				cases.Add(version, smooth, closed);
			return cases;
		}
	}

	[Theory]
	[InlineData(new byte[] { 0, 0, 0, 0 }, 4, MeasurementUnits.English)]
	[InlineData(new byte[] { 0, 0, 1, 0 }, 4, MeasurementUnits.Metric)]
	[InlineData(new byte[] { 3, 0, 65, 66, 67, 1, 0 }, 7, MeasurementUnits.Metric)]
	[InlineData(new byte[] { }, 0, null)]
	[InlineData(new byte[] { 0, 0, 0 }, 3, null)]
	[InlineData(new byte[] { 255, 255, 1, 0 }, 4, null)]
	[InlineData(new byte[] { 3, 0, 0, 0, 0, 0, 0 }, 4, null)]
	[InlineData(new byte[] { 0, 0, 2, 0 }, 4, null)]
	[InlineData(new byte[] { 0, 0, 255, 255 }, 4, null)]
	public void TemplateMetadataUsesLogicalLength(byte[] bytes, long logicalLength, MeasurementUnits? expected)
	{
		var reader = DwgStreamReaderBase.GetStreamHandler(ACadVersion.AC1032, new MemoryStream(bytes));
		Assert.Equal(expected, DwgReader.readTemplateMeasurement(reader, logicalLength));
	}

	[Theory]
	[MemberData(nameof(PolylineCases))]
	public void DwgKeepsPolylineSmoothSurfaceAndClosure(ACadVersion version, SmoothSurfaceType smooth, bool closed)
	{
		var doc = new CadDocument();
		doc.Header.Version = version;
		var points = new[] { XYZ.Zero, new XYZ(1, 2, 3), new XYZ(4, 5, 6) };
		doc.Entities.Add(new Polyline3D(points, closed) { SmoothSurface = smooth });
		using var stream = new MemoryStream();
		DwgWriter.Write(stream, doc);
		var back = DwgReader.Read(new MemoryStream(stream.ToArray())).Entities.OfType<Polyline3D>().Single();
		Assert.Equal(smooth, back.SmoothSurface);
		Assert.Equal(closed, back.IsClosed);
		Assert.Equal(smooth != SmoothSurfaceType.NoSmooth, back.Flags.HasFlag(PolylineFlags.SplineFit));
		Assert.Equal(points, back.Vertices.Select(v => v.Location));
	}

	[Theory]
	[InlineData(ACadVersion.AC1018, MeasurementUnits.English)]
	[InlineData(ACadVersion.AC1018, MeasurementUnits.Metric)]
	[InlineData(ACadVersion.AC1021, MeasurementUnits.English)]
	[InlineData(ACadVersion.AC1021, MeasurementUnits.Metric)]
	[InlineData(ACadVersion.AC1024, MeasurementUnits.English)]
	[InlineData(ACadVersion.AC1024, MeasurementUnits.Metric)]
	[InlineData(ACadVersion.AC1027, MeasurementUnits.English)]
	[InlineData(ACadVersion.AC1027, MeasurementUnits.Metric)]
	[InlineData(ACadVersion.AC1032, MeasurementUnits.English)]
	[InlineData(ACadVersion.AC1032, MeasurementUnits.Metric)]
	public void DwgKeepsMeasurement(ACadVersion version, MeasurementUnits units)
	{
		var doc = new CadDocument();
		doc.Header.Version = version;
		doc.Header.MeasurementUnits = units;
		using var stream = new MemoryStream();
		DwgWriter.Write(stream, doc);
		Assert.Equal(units, DwgReader.Read(new MemoryStream(stream.ToArray())).Header.MeasurementUnits);
	}
}

