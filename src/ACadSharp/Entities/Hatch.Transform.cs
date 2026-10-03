using CSMath;
using System;

namespace ACadSharp.Entities;

public partial class Hatch
{
	private static void assertAffineTransform(Transform transform)
	{
		if (transform == null) throw new ArgumentNullException(nameof(transform));
		Matrix4 matrix = transform.Matrix;
		for (int i = 0; i < 16; i++)
			if (double.IsNaN(matrix[i]) || double.IsInfinity(matrix[i]))
				throw new NotSupportedException("Hatch transforms must have finite matrix entries.");
		if (matrix.M03 != 0 || matrix.M13 != 0 || matrix.M23 != 0 || matrix.M33 != 1)
			throw new NotSupportedException("Perspective hatch transforms are not supported.");
	}

	private static XYZ transformVector(Transform transform, XYZ vector)
		=> (transform.Matrix * new XYZM(vector.X, vector.Y, vector.Z, 0)).Convert<XYZ>();

	private static double similarityScale(XYZ x, XYZ y)
	{
		double sx = x.GetLength(), sy = y.GetLength();
		if (!(sx > 0) || !(sy > 0) || double.IsInfinity(sx) || double.IsInfinity(sy)
			|| Math.Abs(sx - sy) > 1e-10 * Math.Max(sx, sy)
			|| Math.Abs((x / sx).Dot(y / sy)) > 1e-10)
			throw new NotSupportedException("Nonuniform scaling, shear and degenerate hatch transforms are not supported.");
		return sx;
	}

	//Boundary curves live in XY. Their fixed radius/axis ratio can represent
	//similarities and reflections, but not a general affine projection.
	private static double planarSimilarityScale(Transform transform, out bool mirrored)
	{
		assertAffineTransform(transform);
		XYZ x = transformVector(transform, XYZ.AxisX);
		XYZ y = transformVector(transform, XYZ.AxisY);
		double scale = similarityScale(x, y);
		if (Math.Abs(x.Z) > 1e-10 * scale || Math.Abs(y.Z) > 1e-10 * scale)
			throw new NotSupportedException("Hatch boundary curves must remain in the XY plane.");
		mirrored = XY.Cross(new XY(x.X / scale, x.Y / scale), new XY(y.X / scale, y.Y / scale)) < 0;
		return scale;
	}

	private static double positiveAngle(double angle)
	{
		angle %= MathHelper.TwoPI;
		return angle < 0 ? angle + MathHelper.TwoPI : angle;
	}
}
