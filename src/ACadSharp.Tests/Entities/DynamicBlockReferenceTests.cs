using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.Entities;

/// <summary>
/// Answering "which dynamic block is this reference?" from an <see cref="Insert"/>.
/// </summary>
/// <remarks>
/// The obvious route is wrong and quietly so. A reference to a dynamic block does not point at the
/// dynamic block: it points at the anonymous evaluated block AutoCAD generates for that particular
/// instance (<c>*U6</c> and the like), which carries no evaluation graph, so
/// <see cref="BlockRecord.IsDynamic"/> on it is false. Every placed dynamic block therefore looks
/// static to a caller that asks the block record. The definition is recorded on the reference.
/// </remarks>
public class DynamicBlockReferenceTests
{
	private static string samples => Path.Combine(TestVariables.SamplesFolder, "dynamic-blocks");

	[Theory]
	[InlineData("BLOCKPOLARPARAMETER.dwg", "BLOCK_POLAR_PARAMETER")]
	[InlineData("BLOCKVISIBILITYPARAMETER.dwg", "block_visibility_parameter")]
	[InlineData("BLOCKLOOKUPPARAMETER.dwg", "My_Look_Block")]
	public void AReferenceNamesTheDynamicBlockItCameFrom(string sample, string definition)
	{
		CadDocument doc = DwgReader.Read(Path.Combine(samples, sample));

		Insert[] dynamic = this.inserts(doc).Where(i => i.IsDynamicBlockReference).ToArray();

		Assert.NotEmpty(dynamic);
		Assert.All(dynamic, i => Assert.Equal(definition, i.DynamicBlockDefinition.Name));
	}

	[Fact]
	public void TheBlockAReferencePointsAtIsNotTheDefinition()
	{
		//The point of the property. The reference points at an anonymous evaluated block, and asking
		//that block whether it is dynamic says no - which is why a caller that trusts it sees every
		//dynamic block in a drawing as an ordinary one.
		CadDocument doc = DwgReader.Read(Path.Combine(samples, "BLOCKPOLARPARAMETER.dwg"));

		Insert insert = this.inserts(doc).First(i => i.IsDynamicBlockReference);

		Assert.StartsWith("*U", insert.Block.Name);
		Assert.False(insert.Block.IsDynamic);
		Assert.NotEqual(insert.Block.Name, insert.DynamicBlockDefinition.Name);
		Assert.True(insert.DynamicBlockDefinition.IsDynamic);
	}

	[Fact]
	public void AnOrdinaryReferenceHasNoDynamicDefinition()
	{
		CadDocument doc = new CadDocument();
		BlockRecord record = new BlockRecord("PLAIN");
		doc.BlockRecords.Add(record);
		Insert insert = new Insert(record);
		doc.Entities.Add(insert);

		Assert.False(insert.IsDynamicBlockReference);
		Assert.Null(insert.DynamicBlockDefinition);
	}

	[Theory]
	[InlineData("BLOCKPOLARPARAMETER.dwg", "BLOCK_POLAR_PARAMETER")]
	[InlineData("BLOCKVISIBILITYPARAMETER.dwg", "block_visibility_parameter")]
	public void TheLinkSurvivesADwgRoundTrip(string sample, string definition)
	{
		CadDocument doc = DwgReader.Read(Path.Combine(samples, sample));

		CadDocument back = this.roundTripDwg(doc);

		Insert[] dynamic = this.inserts(back).Where(i => i.IsDynamicBlockReference).ToArray();
		Assert.NotEmpty(dynamic);
		Assert.All(dynamic, i => Assert.Equal(definition, i.DynamicBlockDefinition.Name));
	}

	[Theory]
	[InlineData("BLOCKPOLARPARAMETER.dwg", "BLOCK_POLAR_PARAMETER")]
	[InlineData("BLOCKVISIBILITYPARAMETER.dwg", "block_visibility_parameter")]
	public void TheLinkSurvivesADxfRoundTrip(string sample, string definition)
	{
		//The DXF leg is the one that matters for a consumer that never writes DWG.
		CadDocument doc = DwgReader.Read(Path.Combine(samples, sample));

		CadDocument back = this.roundTripDxf(doc);

		Insert[] dynamic = this.inserts(back).Where(i => i.IsDynamicBlockReference).ToArray();
		Assert.NotEmpty(dynamic);
		Assert.All(dynamic, i => Assert.Equal(definition, i.DynamicBlockDefinition.Name));
	}

	[Fact]
	public void TheLinkIsLostWhenDynamicBlockDataIsNotWritten()
	{
		//WriteDynamicBlockData is what carries this, and a caller that turns it off is turning the
		//dynamic blocks into ordinary ones on purpose. Said here so the loss is a documented choice
		//rather than a surprise.
		CadDocument doc = DwgReader.Read(Path.Combine(samples, "BLOCKPOLARPARAMETER.dwg"));
		Assert.Contains(this.inserts(doc), i => i.IsDynamicBlockReference);

		using MemoryStream stream = new();
		using (DwgWriter writer = new(stream, doc))
		{
			writer.Configuration.WriteDynamicBlockData = false;
			writer.Write();
		}

		CadDocument back = DwgReader.Read(new MemoryStream(stream.ToArray()));
		Assert.DoesNotContain(this.inserts(back), i => i.IsDynamicBlockReference);
	}

	private Insert[] inserts(CadDocument doc)
	{
		return doc.BlockRecords.SelectMany(b => b.Entities.OfType<Insert>()).ToArray();
	}

	private CadDocument roundTripDwg(CadDocument doc)
	{
		using MemoryStream stream = new();
		using (DwgWriter writer = new(stream, doc))
		{
			writer.Write();
		}

		return DwgReader.Read(new MemoryStream(stream.ToArray()));
	}

	private CadDocument roundTripDxf(CadDocument doc)
	{
		using MemoryStream stream = new();
		using (DxfWriter writer = new(stream, doc, false))
		{
			writer.Write();
		}

		return DxfReader.Read(new MemoryStream(stream.ToArray()));
	}
}
