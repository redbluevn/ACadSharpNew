using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO.DXF;

public class DxfViewportBoundaryTests
{
	[Fact]
	public void DxfRoundTripKeepsTheClipBoundaryOfAViewport()
	{
		//Group code 340 of a VIEWPORT is the entity that clips it. The reader had no case for it, so
		//it fell through to the generic path, which cannot put a handle into an entity reference.
		CadDocument doc = new CadDocument();

		LwPolyline boundary = new LwPolyline { IsClosed = true };
		boundary.Vertices.Add(new LwPolyline.Vertex(new XY(0, 0)));
		boundary.Vertices.Add(new LwPolyline.Vertex(new XY(10, 0)));
		boundary.Vertices.Add(new LwPolyline.Vertex(new XY(10, 10)));

		Viewport viewport = new Viewport
		{
			Center = new XYZ(5, 5, 0),
			Width = 10,
			Height = 10,
		};

		doc.PaperSpace.Entities.Add(boundary);
		doc.PaperSpace.Entities.Add(viewport);
		viewport.Boundary = boundary;

		MemoryStream ms = new MemoryStream();
		using (DxfWriter writer = new DxfWriter(ms, doc, false))
		{
			writer.Write();
		}

		using MemoryStream readStream = new MemoryStream(ms.ToArray());
		CadDocument rt = DxfReader.Read(readStream);

		//Paper space also holds the overall viewport, which has no boundary.
		Viewport got = Assert.Single(rt.PaperSpace.Entities.OfType<Viewport>().Where(v => v.Boundary != null));
		Assert.IsType<LwPolyline>(got.Boundary);
		Assert.Equal(boundary.Handle, got.Boundary.Handle);
	}
}
