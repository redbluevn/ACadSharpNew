using ACadSharp.Objects;
using ACadSharp.Tables;
using Xunit;

namespace ACadSharp.Tests.Objects;

public class MultiLeaderStyleRemovalTests
{
	[Fact]
	public void RemovingATextStyleAMultiLeaderStyleUsesFallsBackToStandard()
	{
		CadDocument doc = new CadDocument();

		TextStyle style = new TextStyle("moredwg_style");
		doc.TextStyles.Add(style);

		MultiLeaderStyle mleaderStyle = new MultiLeaderStyle("moredwg_mleader");
		doc.MLeaderStyles.Add(mleaderStyle);
		mleaderStyle.TextStyle = style;

		//This used to throw: the handler looked the replacement up by the default layer name.
		doc.TextStyles.Remove(style.Name);

		Assert.Equal(TextStyle.DefaultName, mleaderStyle.TextStyle.Name);
	}
}
