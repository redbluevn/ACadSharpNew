using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ACadSharp.IO;
using Xunit;
using Xunit.Abstractions;

namespace ACadSharp.Tests.IO.DWG;

/// <summary>
/// A DWG object is length delimited: the next object is found by offset, so a reader that stops
/// short of the end of an object, or runs past it into the string stream, is never punished by the
/// format. Every field it gets wrong from that point on is wrong in silence.
///
/// Three defects of this exact shape were found by hand in one week - a MULTILEADER that consumed a
/// leader root nobody wrote, a Solid3D missing its history handle for four versions, and a
/// PDFDEFINITION reading its file name out of the string stream. This asks the question they all
/// answer to, for every object in the sample drawings at once, so the next one does not need a week.
/// </summary>
public class DwgObjectBoundaryTests
{
	private readonly ITestOutputHelper _output;

	/// <summary>
	/// Classes this library does not decode fully, so the bits it leaves are expected rather than a
	/// defect. Each is a known gap, not a licence to ignore new ones: a name added here should come
	/// with a reason, and a name that stops appearing should be removed.
	/// </summary>
	private static readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase)
	{
		//Read as far as the common data and kept as unknown objects.
		"ACDBSECTIONVIEWSTYLE",
		"ACDBDETAILVIEWSTYLE",
		"ACDBASSOCPERSSUBENTMANAGER",
		"ACDBPERSSUBENTMANAGER",
		"ACDB_TEXTOBJECTCONTEXTDATA_CLASS",
		"WIPEOUTVARIABLES",
		//Only in the older samples, and undecoded there for the same reason.
		"CELLSTYLEMAP",
		"ACDBDICTIONARYWDFLT",
	};

	public DwgObjectBoundaryTests(ITestOutputHelper output)
	{
		this._output = output;
	}

	//Every version whose object header names the boundary. R2007 and R2000-R2004 were added on
	//2026-08-25, when extending the check to them found a REGION silhouette being read with the
	//wrong bit code on three client drawings.
	public static IEnumerable<object[]> Samples => new[]
	{
		new object[] { "sample_AC1015.dwg" },
		new object[] { "sample_AC1018.dwg" },
		new object[] { "sample_AC1021.dwg" },
		new object[] { "sample_AC1024.dwg" },
		new object[] { "sample_AC1027.dwg" },
		new object[] { "sample_AC1032.dwg" },
	};

	[Theory]
	[MemberData(nameof(Samples))]
	public void EveryObjectConsumesItsDataStream(string file)
	{
		string path = Path.Combine(TestVariables.SamplesFolder, file);
		if (!File.Exists(path))
		{
			return;
		}

		List<string> reported = new();
		using (DwgReader reader = new(path, (s, e) =>
		{
			if (e.Message.Contains("bits of its data were never read") || e.Message.Contains("bits past the end of its data"))
			{
				reported.Add(e.Message);
			}
		}))
		{
			reader.Configuration.ReportUnreadObjectBits = true;
			reader.Read();
		}

		List<string> unexpected = reported
			.Where(m => !_known.Any(k => m.StartsWith(k + " ", StringComparison.OrdinalIgnoreCase)))
			.ToList();

		foreach (string message in unexpected)
		{
			this._output.WriteLine(message);
		}

		Assert.Empty(unexpected);
	}
}
