using ACadSharp.Entities;
using ACadSharp.Tests.Common;
using ACadSharp.IO;
using CSMath;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO;

public class WipeoutClipTests
{
	public static TheoryData<ACadVersion> Versions => new TheoryData<ACadVersion>
	{
		ACadVersion.AC1015,
		ACadVersion.AC1018,
		ACadVersion.AC1024,
		ACadVersion.AC1032,
	};

	private static readonly XY[] _polygon = new[]
	{
		new XY(-0.5, -0.5),
		new XY(4.5, -0.5),
		new XY(4.5, 3.5),
		new XY(2.0, 5.5),
		new XY(-0.5, 3.5),
	};

	[Theory]
	[MemberData(nameof(Versions))]
	public void DwgKeepsAPolygonalClip(ACadVersion version)
	{
		CadDocument doc = this.document(version);

		MemoryStream ms = new MemoryStream();
		using (DwgWriter writer = new DwgWriter(ms, doc))
		{
			writer.Write();
		}

		this.assertPolygon(DwgReader.Read(new MemoryStream(ms.ToArray())));
	}

	[Theory]
	[MemberData(nameof(Versions))]
	public void DxfKeepsAPolygonalClip(ACadVersion version)
	{
		//A polygonal boundary is closed in DXF by repeating the first vertex, which is what AutoCAD
		//writes and reads: the count is one more than the model holds. Reading the repeat back into
		//the model grew the boundary by a vertex on every round trip.
		CadDocument doc = this.document(version);

		MemoryStream ms = new MemoryStream();
		using (DxfWriter writer = new DxfWriter(ms, doc, false))
		{
			writer.Write();
		}

		this.assertPolygon(DxfReader.Read(new MemoryStream(ms.ToArray())));
	}

	private CadDocument document(ACadVersion version)
	{
		CadDocument doc = new CadDocument();
		doc.Header.Version = version;

		Wipeout wipeout = new Wipeout
		{
			InsertPoint = new XYZ(0, 0, 0),
			UVector = new XYZ(1, 0, 0),
			VVector = new XYZ(0, 1, 0),
			Size = new XY(10, 10),
			ClipType = ClipType.Polygonal,
			ClippingState = true,
		};
		wipeout.ClipBoundaryVertices.AddRange(_polygon);

		doc.ModelSpace.Entities.Add(wipeout);
		return doc;
	}

	private void assertPolygon(CadDocument doc)
	{
		Wipeout wipeout = Assert.Single(doc.ModelSpace.Entities.OfType<Wipeout>());

		Assert.Equal(ClipType.Polygonal, wipeout.ClipType);
		Assert.True(wipeout.ClippingState);
		Assert.Equal(_polygon.Length, wipeout.ClipBoundaryVertices.Count);

		for (int i = 0; i < _polygon.Length; i++)
		{
			AssertUtils.AreEqual(_polygon[i], wipeout.ClipBoundaryVertices[i]);
		}
	}
}
