using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Objects.Evaluations;
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

	[Fact]
	public void TheActiveVisibilityStateOfEachInstanceIsReadable()
	{
		//The question an app asks most often about a dynamic block. The state name is group code 1 of
		//the record belonging to the visibility parameter's node, keyed by that node's Id - not its
		//Index, which also resolves and gives a different node.
		CadDocument doc = DwgReader.Read(Path.Combine(samples, "BLOCKVISIBILITYPARAMETER.dwg"));

		string[] active = this.inserts(doc)
			.Where(i => i.IsDynamicBlockReference)
			.Select(this.activeVisibilityState)
			.Where(s => s != null)
			.OrderBy(s => s)
			.ToArray();

		Assert.Equal(new[] { "HideAll", "ShowAll", "VisibilityState0", "VisibilityState1" }, active);
	}

	[Fact]
	public void EveryActiveStateIsOneTheDefinitionDeclares()
	{
		CadDocument doc = DwgReader.Read(Path.Combine(samples, "BLOCKVISIBILITYPARAMETER.dwg"));

		foreach (Insert insert in this.inserts(doc).Where(i => i.IsDynamicBlockReference))
		{
			string state = this.activeVisibilityState(insert);
			BlockVisibilityParameter parameter = (BlockVisibilityParameter)insert.DynamicBlockDefinition
				.EvaluationGraph.Nodes.First(n => n.Expression is BlockVisibilityParameter).Expression;

			Assert.Contains(state, parameter.States.Keys);
		}
	}

	[Fact]
	public void TheActiveVisibilityStateSurvivesBothRoundTrips()
	{
		CadDocument doc = DwgReader.Read(Path.Combine(samples, "BLOCKVISIBILITYPARAMETER.dwg"));
		string[] before = this.activeStates(doc);
		Assert.Equal(4, before.Length);

		Assert.Equal(before, this.activeStates(this.roundTripDwg(DwgReader.Read(Path.Combine(samples, "BLOCKVISIBILITYPARAMETER.dwg")))));
		Assert.Equal(before, this.activeStates(this.roundTripDxf(DwgReader.Read(Path.Combine(samples, "BLOCKVISIBILITYPARAMETER.dwg")))));
	}

	private string[] activeStates(CadDocument doc)
	{
		return this.inserts(doc)
			.Where(i => i.IsDynamicBlockReference)
			.Select(this.activeVisibilityState)
			.Where(s => s != null)
			.OrderBy(s => s)
			.ToArray();
	}

	private string activeVisibilityState(Insert insert)
	{
		EvaluationGraph.Node node = insert.DynamicBlockDefinition?.EvaluationGraph?.Nodes
			.FirstOrDefault(n => n.Expression is BlockVisibilityParameter);
		if (node == null || insert.XDictionary == null)
		{
			return null;
		}

		foreach (NonGraphicalObject entry in insert.XDictionary)
		{
			if (entry is not CadDictionary representation) continue;
			foreach (NonGraphicalObject cacheEntry in representation)
			{
				if (cacheEntry is not CadDictionary cache) continue;
				foreach (NonGraphicalObject dataEntry in cache)
				{
					if (dataEntry is not CadDictionary data || dataEntry.Name != "ACAD_ENHANCEDBLOCKDATA") continue;
					foreach (NonGraphicalObject stateRecord in data)
					{
						if (stateRecord.Name != node.Id.ToString() || stateRecord is not XRecord record) continue;
						foreach (XRecord.Entry value in record.Entries)
						{
							if (value.Code == 1) return value.Value?.ToString();
						}
					}
				}
			}
		}

		return null;
	}

	[Theory]
	[InlineData("BLOCKPOLARPARAMETER.dxf", "BLOCK_POLAR_PARAMETER")]
	[InlineData("BLOCKLINEARPARAMETER.dwg", "LINEAR_PARAM")]
	[InlineData("BLOCKROTATIONPARAMETER.dxf", "dynamic_block")]
	public void AnInstanceStillAtItsDefaultsIsAlsoADynamicBlockReference(string sample, string definition)
	{
		//The case the first version of this API got wrong. An instance not evaluated away from its
		//default values needs no anonymous block and carries no representation record: it references
		//the dynamic block itself. It is every bit a dynamic block reference, and six of the ten
		//samples contain one, so a caller filtering on IsDynamicBlockReference silently skipped them.
		CadDocument doc = sample.EndsWith(".dxf")
			? DxfReader.Read(Path.Combine(samples, sample))
			: DwgReader.Read(Path.Combine(samples, sample));

		Insert direct = this.inserts(doc).Single(i => i.Block.Name == definition);

		Assert.True(direct.IsDynamicBlockReference);
		Assert.Equal(definition, direct.DynamicBlockDefinition.Name);
		Assert.Same(direct.Block, direct.DynamicBlockDefinition);
	}

	[Fact]
	public void BothKindsOfInstanceResolveToTheSameDefinition()
	{
		//The two shapes side by side in one drawing: one still at defaults pointing straight at the
		//definition, one evaluated pointing at an anonymous block. Both name the same dynamic block.
		CadDocument doc = DxfReader.Read(Path.Combine(samples, "BLOCKPOLARPARAMETER.dxf"));

		Insert[] all = this.inserts(doc).Where(i => i.IsDynamicBlockReference).ToArray();

		Assert.Equal(2, all.Length);
		Assert.Contains(all, i => !i.Block.Name.StartsWith("*U"));
		Assert.Contains(all, i => i.Block.Name.StartsWith("*U"));
		Assert.All(all, i => Assert.Equal("BLOCK_POLAR_PARAMETER", i.DynamicBlockDefinition.Name));
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
