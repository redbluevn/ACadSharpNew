using ACadSharp.IO;
using ACadSharp.Objects;
using System.IO;
using System.Linq;
using Xunit;

namespace ACadSharp.Tests.IO.DWG;

/// <summary>
/// T5: an object the reader cannot model is captured as its raw DWG record and written back
/// verbatim into a file of the same version, instead of being dropped. The need was measured
/// before building: AutoCAD's own plain resave of a client drawing keeps all 62,554 of its
/// unknown objects count-for-count, so dropping them is a real loss - unlike the proxies of T74,
/// which AutoCAD itself drops. The record layout is version-specific, so a body read at one
/// version is never written into another; and R2007 stays out of reach for now because this
/// writer cannot produce R2007 files at all.
/// </summary>
public class UnknownObjectRawRoundTripTests
{
	private static string samplePath(string name) => Path.Combine(TestVariables.SamplesFolder, name);

	private static CadDocument readKeepingUnknown(string path)
	{
		using DwgReader reader = new(path);
		reader.Configuration.KeepUnknownNonGraphicalObjects = true;
		return reader.Read();
	}

	private static CadDocument readKeepingUnknown(MemoryStream stream)
	{
		using DwgReader reader = new(new MemoryStream(stream.ToArray()));
		reader.Configuration.KeepUnknownNonGraphicalObjects = true;
		return reader.Read();
	}

	private static UnknownNonGraphicalObject[] unknowns(CadDocument doc)
	{
		var seen = new System.Collections.Generic.List<UnknownNonGraphicalObject>();
		void walk(CadDictionary dict)
		{
			foreach (var entry in dict)
			{
				if (entry is UnknownNonGraphicalObject u)
				{
					seen.Add(u);
				}

				if (entry is CadDictionary child)
				{
					walk(child);
				}
			}
		}

		walk(doc.RootDictionary);
		return seen.ToArray();
	}

	[Theory]
	[InlineData("sample_AC1018.dwg", ACadVersion.AC1018)] //pre-R2010 record framing
	[InlineData("sample_AC1032.dwg", ACadVersion.AC1032)] //R2010+ framing, with the handle bit size
	public void AnUnknownObjectSurvivesASameVersionRoundTrip(string sample, ACadVersion version)
	{
		CadDocument doc = readKeepingUnknown(samplePath(sample));
		UnknownNonGraphicalObject[] before = unknowns(doc);
		Assert.NotEmpty(before);
		Assert.All(before, u => Assert.NotNull(u.RawObjectBody));
		Assert.All(before, u => Assert.Equal(version, u.RawObjectVersion));

		using MemoryStream stream = new();
		using (DwgWriter writer = new(stream, doc))
		{
			writer.Write();
		}

		UnknownNonGraphicalObject[] after = unknowns(readKeepingUnknown(stream));
		Assert.Equal(before.Length, after.Length);
		Assert.Equal(
			before.Select(u => u.ObjectName).OrderBy(n => n),
			after.Select(u => u.ObjectName).OrderBy(n => n));
	}

	[Fact]
	public void ACrossVersionWriteStillDropsAndSaysWhy()
	{
		//The record layout does not carry across versions, so this half keeps the old behavior -
		//and now says which version the body was read at.
		CadDocument doc = readKeepingUnknown(samplePath("sample_AC1018.dwg"));
		Assert.NotEmpty(unknowns(doc));

		doc.Header.Version = ACadVersion.AC1032;
		string reported = null;
		using MemoryStream stream = new();
		using (DwgWriter writer = new(stream, doc))
		{
			writer.OnNotification += (s, e) =>
			{
				if (e.Message.Contains("does not carry across versions"))
				{
					reported = e.Message;
				}
			};
			writer.Write();
		}

		Assert.NotNull(reported);
		Assert.Empty(unknowns(readKeepingUnknown(stream)));
	}

	[Fact]
	public void NothingIsCapturedWhenUnknownObjectsAreNotKept()
	{
		//The default configuration drops the objects, so there must be nothing to capture and the
		//write must behave exactly as before this change.
		CadDocument doc = DwgReader.Read(samplePath("sample_AC1032.dwg"));
		Assert.Empty(unknowns(doc));
	}
}
