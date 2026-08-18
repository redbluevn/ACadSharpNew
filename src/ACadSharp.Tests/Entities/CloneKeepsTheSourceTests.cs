using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ACadSharp.Tests.Entities;

/// <summary>
/// Cloning must not touch the entity it was cloned from. The three types below built their copy on
/// MemberwiseClone, which shares the list with the source, and then cleared that list: the source
/// lost its content and the clone got nothing, because the loop that refilled it read the list that
/// had just been emptied.
/// </summary>
public class CloneKeepsTheSourceTests
{
	[Fact]
	public void CloningAnMLineKeepsTheVerticesOfBoth()
	{
		MLine mline = new MLine();
		mline.Vertices.Add(new MLine.Vertex { Position = new XYZ(0, 0, 0) });
		mline.Vertices.Add(new MLine.Vertex { Position = new XYZ(10, 0, 0) });
		mline.Vertices.Add(new MLine.Vertex { Position = new XYZ(10, 10, 0) });

		MLine clone = (MLine)mline.Clone();

		Assert.Equal(3, mline.Vertices.Count);
		Assert.Equal(3, clone.Vertices.Count);
		Assert.NotSame(mline.Vertices, clone.Vertices);
		Assert.Equal(new XYZ(10, 10, 0), mline.Vertices[2].Position);
		Assert.Equal(new XYZ(10, 10, 0), clone.Vertices[2].Position);
	}

	[Fact]
	public void CloningAnMLineVertexKeepsTheSegmentsOfBoth()
	{
		MLine.Vertex vertex = new MLine.Vertex { Position = new XYZ(1, 2, 0) };
		MLine.Vertex.Segment segment = new MLine.Vertex.Segment();
		segment.Parameters.Add(1.5);
		vertex.Segments.Add(segment);

		MLine.Vertex clone = vertex.Clone();

		Assert.Single(vertex.Segments);
		Assert.Single(clone.Segments);
		Assert.NotSame(vertex.Segments, clone.Segments);
		Assert.Equal(1.5, Assert.Single(clone.Segments[0].Parameters));
	}

	[Fact]
	public void CloningAGradientPatternKeepsTheColoursOfBoth()
	{
		HatchGradientPattern pattern = new HatchGradientPattern("LINEAR");
		pattern.Colors.Add(new GradientColor { Value = 0, Color = new Color(1) });
		pattern.Colors.Add(new GradientColor { Value = 1, Color = new Color(2) });

		HatchGradientPattern clone = pattern.Clone();

		Assert.Equal(2, pattern.Colors.Count);
		Assert.Equal(2, clone.Colors.Count);
		Assert.NotSame(pattern.Colors, clone.Colors);
		Assert.Equal(new Color(2), clone.Colors[1].Color);
	}
}
