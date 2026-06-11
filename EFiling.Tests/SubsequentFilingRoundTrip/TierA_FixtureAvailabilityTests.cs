using System.Xml.Linq;

namespace EFiling.Tests.SubsequentFilingRoundTrip;

/// <summary>
/// Tier A — Fixture availability scorecard.
///
/// <para>
/// This is the foundation of the T-2 round-trip harness: before we can round-trip samples, we
/// need to prove our registry in <see cref="CanonicalScenarios"/> exactly matches what's on disk.
/// These tests are expected GREEN from day 1 — a failure means either the sample set has drifted
/// or someone renamed/renumbered a scenario ID in the registry.
/// </para>
///
/// <para>
/// These tests are intentionally minimal and cover only fixture-level invariants: file exists,
/// file is well-formed XML, file has a recognizable JTI root element. They do NOT perform any
/// semantic parsing or round-tripping — that is the job of <c>TierA_RoundTripTests</c> (currently
/// placeholder, per T-2 Pass 2).
/// </para>
///
/// <para>
/// Coverage matrix is emitted as <c>Category_CountsMatchRegistry</c> and scenario-level tests
/// use the stable ID as the theory data parameter, so xUnit test output shows e.g.
/// <c>CIV-SUB-007</c> directly — matching <c>JTI_SUBSEQUENT_FILING_CATALOG.md</c> §2.
/// </para>
/// </summary>
public class TierA_FixtureAvailabilityTests
{
    [Fact]
    public void Registry_Has48BaselineScenarios()
    {
        Assert.Equal(48, CanonicalScenarios.All.Count);
    }

    [Fact]
    public void Registry_HasDistinctScenarioIds()
    {
        var ids = CanonicalScenarios.All.Select(s => s.Id).ToList();
        var duplicates = ids.GroupBy(id => id)
                            .Where(g => g.Count() > 1)
                            .Select(g => g.Key)
                            .ToList();
        Assert.True(duplicates.Count == 0,
            $"Duplicate scenario IDs in registry: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void Registry_CategoryCounts_MatchCatalog()
    {
        // Per JTI_SUBSEQUENT_FILING_CATALOG.md §2.7 enumeration-status table.
        Assert.Equal(32, CanonicalScenarios.ByCategory(CaseCategory.Civil).Count());
        Assert.Equal(10, CanonicalScenarios.ByCategory(CaseCategory.FamilyLaw).Count());
        Assert.Equal( 4, CanonicalScenarios.ByCategory(CaseCategory.Probate).Count());
        Assert.Equal( 2, CanonicalScenarios.ByCategory(CaseCategory.MentalHealth).Count());
    }

    [Fact]
    public void Registry_FilingTypeCounts_MatchCatalog()
    {
        // 13 CIV-INI + 4 FAM-INI + 3 PRO-INI + 2 MH-INI = 22 initiation.
        Assert.Equal(22, CanonicalScenarios.ByFilingType(FilingType.Initiation).Count());
        // 19 CIV-SUB + 6 FAM-SUB + 1 PRO-SUB + 0 MH-SUB = 26 subsequent.
        Assert.Equal(26, CanonicalScenarios.ByFilingType(FilingType.Subsequent).Count());
    }

    [Fact]
    public void Registry_IdFormat_IsStable()
    {
        // IDs must match [CAT]-[FT]-### where CAT is 2-4 uppercase letters, FT is INI or SUB, ### is 3 digits.
        var bad = CanonicalScenarios.All
            .Where(s => !System.Text.RegularExpressions.Regex.IsMatch(s.Id, @"^[A-Z]{2,4}-(INI|SUB)-\d{3}$"))
            .Select(s => s.Id)
            .ToList();
        Assert.True(bad.Count == 0,
            $"Scenario IDs not matching [CAT]-(INI|SUB)-### format: {string.Join(", ", bad)}");
    }

    [Fact]
    public void RepoRoot_IsDiscoverable()
    {
        var root = SampleLoader.RepoRoot;
        Assert.False(string.IsNullOrEmpty(root), "SampleLoader.RepoRoot returned empty.");
        Assert.True(Directory.Exists(root), $"Discovered repo root does not exist: {root}");

        var baseline = SampleLoader.BaselineRootAbsolute;
        Assert.True(Directory.Exists(baseline),
            $"Discovered baseline root does not exist: {baseline}\n"
            + $"Repo root: {root}\n"
            + $"Expected relative path: {CanonicalScenarios.BaselineRoot}");
    }

    [Theory]
    [MemberData(nameof(CanonicalScenarios.AllScenarioIds), MemberType = typeof(CanonicalScenarios))]
    public void Scenario_FileExists(string scenarioId)
    {
        var scenario = CanonicalScenarios.GetById(scenarioId);
        var path = SampleLoader.GetAbsolutePath(scenario);
        Assert.True(File.Exists(path),
            $"[{scenarioId}] Canonical sample missing at: {path}\n"
            + $"Scenario description: {scenario.Description}");
    }

    [Theory]
    [MemberData(nameof(CanonicalScenarios.AllScenarioIds), MemberType = typeof(CanonicalScenarios))]
    public void Scenario_IsWellFormedXml(string scenarioId)
    {
        var scenario = CanonicalScenarios.GetById(scenarioId);
        // LoadXDocument throws InvalidDataException with a detailed message on malformed XML.
        var doc = SampleLoader.LoadXDocument(scenario);
        Assert.NotNull(doc.Root);
    }

    [Theory]
    [MemberData(nameof(CanonicalScenarios.AllScenarioIds), MemberType = typeof(CanonicalScenarios))]
    public void Scenario_HasPlausibleJtiRoot(string scenarioId)
    {
        // JTI samples are either a bare <ReviewFilingRequestMessage> or a SOAP envelope
        // containing one. This test verifies at least one of those shapes is present, so
        // if we ever pull a non-ReviewFiling operation sample (GetCase, GetPolicy, etc.) into
        // this registry by mistake, it surfaces here rather than silently passing.
        var scenario = CanonicalScenarios.GetById(scenarioId);
        var doc = SampleLoader.LoadXDocument(scenario);
        var root = doc.Root!;

        bool isBareReviewFiling = root.Name.LocalName == "ReviewFilingRequestMessage";
        bool isSoapEnvelope = root.Name.LocalName == "Envelope"
            && root.Descendants().Any(e => e.Name.LocalName == "ReviewFilingRequestMessage");

        Assert.True(isBareReviewFiling || isSoapEnvelope,
            $"[{scenarioId}] Root element '{root.Name.LocalName}' is not ReviewFilingRequestMessage "
            + $"and no ReviewFilingRequestMessage found within SOAP Envelope. "
            + $"Sample path: {SampleLoader.GetAbsolutePath(scenario)}");
    }
}
