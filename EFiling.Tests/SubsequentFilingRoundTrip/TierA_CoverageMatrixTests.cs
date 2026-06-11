using System.Xml.Linq;

namespace EFiling.Tests.SubsequentFilingRoundTrip;

/// <summary>
/// Tier A — Pass 2 coverage matrix enforcement.
///
/// <para>
/// Locks in the classType / tag usage counts documented in
/// <c>JTI_SUBSEQUENT_FILING_CATALOG.md</c> §2.6.1 (Pass 2 coverage matrix). If a canonical
/// sample is modified, added, or removed in a way that changes the overall classType / tag
/// distribution, these tests fail and force an explicit update of the catalog's coverage matrix.
/// </para>
///
/// <para>
/// The values here are the ground-truth extracted by the Pass 2 regex sweep against the 26
/// baseline subsequent-filing samples. They must not drift without a corresponding catalog edit.
/// If you need to update them: run the Pass 2 extraction PowerShell (see catalog change log),
/// update this file's <see cref="ExpectedClassTypeCounts"/> / <see cref="ExpectedTagCounts"/>,
/// and update <c>JTI_SUBSEQUENT_FILING_CATALOG.md</c> §2.6.1 in the same commit.
/// </para>
///
/// <para>
/// These tests only scan <b>subsequent-filing</b> samples. Case Initiation samples use a
/// different grammar (directly-typed NIEM elements, no <c>classType</c> / <c>tagType</c> meta-
/// grammar) and are exempted. Per catalog §2.6.1 strategic implication #5, the initiation
/// grammar will be documented separately in T-1.A.
/// </para>
/// </summary>
public class TierA_CoverageMatrixTests
{
    /// <summary>
    /// Expected classType occurrence count across the 26 baseline subsequent samples.
    /// Must match <c>JTI_SUBSEQUENT_FILING_CATALOG.md</c> §2.6.1 "ClassType coverage" table exactly.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> ExpectedClassTypeCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["caseParticipant"] = 26, // 100% — every subsequent sample
            ["caseAssignment"] = 14,  // 54% — verified by XDocument scan
            ["contact"]         =  5, // 19%
            ["codeList"]        =  2, //  8% — FAM-SUB-003, FAM-SUB-005
            ["date"]            =  2, //  8% — CIV-SUB-016, CIV-SUB-017
            ["boolean"]         =  2, //  8% — CIV-SUB-018, FAM-SUB-004
        };

    /// <summary>
    /// Expected tagType occurrence count across the 26 baseline subsequent samples.
    /// Must match <c>JTI_SUBSEQUENT_FILING_CATALOG.md</c> §2.6.1 "Tag coverage" table exactly.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> ExpectedTagCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["E_SERVICE"]                  = 8, // 31% — verified by XDocument scan
            ["FEE_EXEMPTION"]              = 2, //  8% — CIV-SUB-002 + CIV-SUB-003 (audit C-3 confirmed: same tagType, differing semantics)
            ["EFSP_FIRST_APPEARANCE_PAID"] = 1, //  4% — CIV-SUB-004
            ["EFSP_EMAIL"]                 = 1, //  4% — FAM-SUB-004 (paired with E_SERVICE)
        };

    /// <summary>
    /// Extract distinct classType element values from a sample. Handles namespaced elements
    /// (e.g., <c>&lt;ns8:classType&gt;caseParticipant&lt;/ns8:classType&gt;</c>) by matching on
    /// local name — the ns8 namespace is JTI's DocumentValue extension and always wraps these.
    /// </summary>
    private static IReadOnlyCollection<string> DistinctClassTypes(XDocument doc) =>
        doc.Descendants()
            .Where(e => e.Name.LocalName == "classType")
            .Select(e => e.Value.Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>Extract distinct tagType element values from a sample.</summary>
    private static IReadOnlyCollection<string> DistinctTagTypes(XDocument doc) =>
        doc.Descendants()
            .Where(e => e.Name.LocalName == "tagType")
            .Select(e => e.Value.Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void Subsequent_ClassTypeCounts_MatchCatalog_Section2_6_1()
    {
        var subsequent = CanonicalScenarios
            .ByFilingType(FilingType.Subsequent)
            .ToList();
        Assert.Equal(26, subsequent.Count);

        // Count distinct-per-sample classType occurrences across all subsequent samples.
        var observed = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var scenario in subsequent)
        {
            var doc = SampleLoader.LoadXDocument(scenario);
            foreach (var ct in DistinctClassTypes(doc))
            {
                observed.TryGetValue(ct, out var prev);
                observed[ct] = prev + 1;
            }
        }

        // Every observed classType must be documented in the catalog with matching count.
        var mismatches = new List<string>();
        foreach (var (classType, expectedCount) in ExpectedClassTypeCounts)
        {
            observed.TryGetValue(classType, out var actual);
            if (actual != expectedCount)
                mismatches.Add($"  - '{classType}': expected {expectedCount}, observed {actual}");
        }

        // Any classType observed but NOT in the expected table is also a drift — the catalog
        // should declare its existence (even if with a count of 0 historically, here just flag).
        foreach (var classType in observed.Keys)
        {
            if (!ExpectedClassTypeCounts.ContainsKey(classType))
                mismatches.Add($"  - '{classType}': observed {observed[classType]} times but NOT declared in ExpectedClassTypeCounts. Update catalog §2.6.1 and this test.");
        }

        Assert.True(mismatches.Count == 0,
            "ClassType coverage drift detected. Update JTI_SUBSEQUENT_FILING_CATALOG.md §2.6.1 "
            + "and ExpectedClassTypeCounts in this test.\n"
            + string.Join("\n", mismatches));
    }

    [Fact]
    public void Subsequent_TagTypeCounts_MatchCatalog_Section2_6_1()
    {
        var subsequent = CanonicalScenarios
            .ByFilingType(FilingType.Subsequent)
            .ToList();

        var observed = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var scenario in subsequent)
        {
            var doc = SampleLoader.LoadXDocument(scenario);
            foreach (var tag in DistinctTagTypes(doc))
            {
                observed.TryGetValue(tag, out var prev);
                observed[tag] = prev + 1;
            }
        }

        var mismatches = new List<string>();
        foreach (var (tag, expectedCount) in ExpectedTagCounts)
        {
            observed.TryGetValue(tag, out var actual);
            if (actual != expectedCount)
                mismatches.Add($"  - '{tag}': expected {expectedCount}, observed {actual}");
        }
        foreach (var tag in observed.Keys)
        {
            if (!ExpectedTagCounts.ContainsKey(tag))
                mismatches.Add($"  - '{tag}': observed {observed[tag]} times but NOT declared in ExpectedTagCounts. Update catalog §2.6.1 and this test.");
        }

        Assert.True(mismatches.Count == 0,
            "Tag coverage drift detected. Update JTI_SUBSEQUENT_FILING_CATALOG.md §2.6.1 "
            + "and ExpectedTagCounts in this test.\n"
            + string.Join("\n", mismatches));
    }

    [Fact]
    public void Initiation_DoesNot_UseSubsequentMetaGrammar()
    {
        // Catalog §2.6.1 implication: Case Initiation samples never use classType/tagType
        // meta-grammar. They use directly-typed NIEM elements. This test locks that in so
        // any future initiation sample that introduces the meta-grammar is caught.
        var initiation = CanonicalScenarios
            .ByFilingType(FilingType.Initiation)
            .ToList();
        Assert.Equal(22, initiation.Count);

        var offenders = new List<string>();
        foreach (var scenario in initiation)
        {
            var doc = SampleLoader.LoadXDocument(scenario);
            var classTypes = DistinctClassTypes(doc);
            var tagTypes = DistinctTagTypes(doc);
            if (classTypes.Count > 0 || tagTypes.Count > 0)
            {
                offenders.Add(
                    $"  - {scenario.Id} ({scenario.Description}): "
                    + $"classTypes=[{string.Join(",", classTypes)}], tagTypes=[{string.Join(",", tagTypes)}]");
            }
        }

        Assert.True(offenders.Count == 0,
            "Case Initiation samples were expected to never use the classType/tagType meta-grammar "
            + "(catalog §2.6.1 implication). The following samples violate this assumption — "
            + "either the samples changed, or catalog §2.6.1's grammar-asymmetry statement needs updating:\n"
            + string.Join("\n", offenders));
    }
}
