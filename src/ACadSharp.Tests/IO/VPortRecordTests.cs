using ACadSharp.IO;
using ACadSharp.Tables;
using ACadSharp.Tests.Common;
using CSMath;
using System.IO;
using Xunit;

namespace ACadSharp.Tests.IO;

public class VPortRecordTests
{
	public static TheoryData<ACadVersion> Versions => new TheoryData<ACadVersion>
	{
		ACadVersion.AC1015,
		ACadVersion.AC1018,
		ACadVersion.AC1024,
		ACadVersion.AC1032,
	};

	[Theory]
	[MemberData(nameof(Versions))]
	public void DxfKeepsTheFieldsOfTheActiveViewport(ACadVersion version)
	{
		//The DXF writer stopped the VPORT record after group 41 and then jumped to 75, so the lens
		//length, the clipping planes, the snap rotation, the view twist, the view mode, the UCS of
		//the viewport and the grid settings were all dropped. An application had to push the bytes
		//into the stream itself.
		CadDocument doc = new CadDocument();
		doc.Header.Version = version;

		VPort vport = doc.VPorts[VPort.DefaultName];
		this.fill(vport);

		MemoryStream ms = new MemoryStream();
		using (DxfWriter writer = new DxfWriter(ms, doc, false))
		{
			writer.Write();
		}

		this.assert(DxfReader.Read(new MemoryStream(ms.ToArray())).VPorts[VPort.DefaultName], version);
	}

	[Theory]
	[MemberData(nameof(Versions))]
	public void DwgKeepsTheFieldsOfTheActiveViewport(ACadVersion version)
	{
		CadDocument doc = new CadDocument();
		doc.Header.Version = version;

		VPort vport = doc.VPorts[VPort.DefaultName];
		this.fill(vport);

		MemoryStream ms = new MemoryStream();
		using (DwgWriter writer = new DwgWriter(ms, doc))
		{
			writer.Write();
		}

		this.assert(DwgReader.Read(new MemoryStream(ms.ToArray())).VPorts[VPort.DefaultName], version);
	}

	private void fill(VPort vport)
	{
		vport.LensLength = 35.5;
		vport.FrontClippingPlane = 1.25;
		vport.BackClippingPlane = -2.5;
		vport.SnapRotation = MathHelper.DegToRad(30);
		vport.TwistAngle = MathHelper.DegToRad(15);
		vport.ViewMode = ViewModeType.FrontClipping | ViewModeType.BackClipping;
		vport.CircleZoomPercent = 500;
		vport.UcsIconDisplay = UscIconType.OnLower;
		vport.IsometricSnap = true;
		vport.SnapIsoPair = 2;
		vport.Origin = new XYZ(1, 2, 3);
		vport.OrthographicType = OrthographicType.Top;
		vport.Elevation = 4.5;
		vport.MinorGridLinesPerMajorGridLine = 7;
	}

	private void assert(VPort vport, ACadVersion version)
	{
		AssertUtils.AreEqual(35.5, vport.LensLength, nameof(vport.LensLength));
		AssertUtils.AreEqual(1.25, vport.FrontClippingPlane, nameof(vport.FrontClippingPlane));
		AssertUtils.AreEqual(-2.5, vport.BackClippingPlane, nameof(vport.BackClippingPlane));
		AssertUtils.AreEqual(MathHelper.DegToRad(30), vport.SnapRotation, nameof(vport.SnapRotation));
		AssertUtils.AreEqual(MathHelper.DegToRad(15), vport.TwistAngle, nameof(vport.TwistAngle));

		Assert.Equal(ViewModeType.FrontClipping | ViewModeType.BackClipping, vport.ViewMode);
		Assert.Equal((short)500, vport.CircleZoomPercent);
		Assert.Equal(UscIconType.OnLower, vport.UcsIconDisplay);
		Assert.True(vport.IsometricSnap);
		Assert.Equal((short)2, vport.SnapIsoPair);
		AssertUtils.AreEqual(new XYZ(1, 2, 3), vport.Origin, nameof(vport.Origin));
		Assert.Equal(OrthographicType.Top, vport.OrthographicType);
		AssertUtils.AreEqual(4.5, vport.Elevation, nameof(vport.Elevation));

		if (version >= ACadVersion.AC1021)
		{
			Assert.Equal((short)7, vport.MinorGridLinesPerMajorGridLine);
		}
	}
}
