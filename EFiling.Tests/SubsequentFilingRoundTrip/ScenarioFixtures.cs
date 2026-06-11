using EFiling.Core.Models;
using EFiling.Providers.JTI.Parsers;

namespace EFiling.Tests.SubsequentFilingRoundTrip;

/// <summary>
/// Convenience wrapper around <see cref="SampleLoader"/> + <see cref="ReviewFilingRequestParser"/>
/// for obtaining a <see cref="FilingSubmission"/> from a canonical scenario ID.
///
/// <para>
/// Tier A tests (CI + SF round-trip) already do this pattern inline. Tier B tests
/// (live Madera submit) need the same conversion PLUS the ability to filter the
/// 48-scenario canonical set by "which scenarios can actually run against Madera,
/// which ones are blocked because Madera doesn't expose the relevant case type."
/// This helper centralizes both.
/// </para>
///
/// <para>
/// <b>Madera-reachability derivation.</b> Per the live <c>madera_CASE_TYPE.xml</c>
/// codelist capture (catalog §2.6 Layer C, 2026-04-22 correction), Madera advertises
/// 5 case types covering 4 categories at the case-INITIATION surface: Family Law/Support
/// (211110), Civil Unlimited (411110), Civil Limited (421110), Probate (511110),
/// Small Claims (711110). Mental Health / Juvenile / Criminal / Appellate are NOT
/// exposed for case INITIATION. Among our 48 canonical baseline scenarios, exactly 2
/// fall outside Madera's case-initiation scope — both Mental Health Case Initiation
/// (<c>MH-INI-001</c>, <c>MH-INI-002</c>); they stay Tier A-only until another JTI court
/// with MH initiation jurisdiction onboards.
/// </para>
///
/// <para>
/// <b>Step #49 clarification:</b> Madera DOES expose <i>subsequent filing</i>
/// on existing Mental Health cases — Tier-B probe (playwright-cli) on Madera dockets
/// MMH00187 + MMH00192 (case type 611110 'WI6500-Mental Retarded/Dangerous', category
/// 613110 'Mental Health-Other') both render the SF page successfully with full
/// metadata-sections-container, no gate, no errors. The earlier "Madera doesn't expose
/// MH" phrasing was correct for CI but conflated with SF — only the CI-side is
/// categorically blocked. The 2 MH-INI scenarios above remain blocked; if MH-SUB
/// baseline samples ever exist in <c>docs/fileing files/Mental Health/</c>, they would
/// be Tier-B-reachable today via the existing MMH cases.
/// </para>
/// </summary>
public static class ScenarioFixtures
{
    /// <summary>
    /// Case categories that Madera exposes via its GetPolicy response and that we have
    /// baseline scenarios for. Derived from <c>docs/fileing files/madera_CASE_TYPE.xml</c>.
    /// </summary>
    private static readonly HashSet<CaseCategory> MaderaExposedCategories = new()
    {
        CaseCategory.Civil,
        CaseCategory.FamilyLaw,
        CaseCategory.Probate,
        // Small Claims: Madera exposes 711110 but we have no Small-Claims-category
        // baseline today (CIV-INI-013 is small-claims jurisdictional-limit filed
        // under Civil, so it falls in the Civil category).
    };

    /// <summary>
    /// 46 scenarios reachable on Madera (Civil 32 + FamilyLaw 10 + Probate 4).
    /// Use as the data source for Tier B <c>[Theory]</c> tests.
    /// </summary>
    public static IReadOnlyList<CanonicalScenario> MaderaReachable =>
        CanonicalScenarios.All
            .Where(s => MaderaExposedCategories.Contains(s.Category))
            .ToList()
            .AsReadOnly();

    /// <summary>
    /// 2 scenarios NOT reachable on Madera (both Mental Health Case Initiation).
    /// Use to document the Tier B coverage gap and to ensure they are excluded
    /// from the live-Madera theory data.
    /// </summary>
    public static IReadOnlyList<CanonicalScenario> MaderaBlocked =>
        CanonicalScenarios.All
            .Where(s => !MaderaExposedCategories.Contains(s.Category))
            .ToList()
            .AsReadOnly();

    /// <summary>
    /// xUnit <c>MemberData</c> source yielding <c>(scenarioId)</c> pairs for the 46
    /// Madera-reachable scenarios. Sorted by ID for stable theory naming.
    /// </summary>
    public static IEnumerable<object[]> MaderaReachableScenarioIds =>
        MaderaReachable
            .OrderBy(s => s.Id, StringComparer.Ordinal)
            .Select(s => new object[] { s.Id });

    /// <summary>
    /// Load the canonical baseline XML for the given scenario and parse it into a
    /// <see cref="FilingSubmission"/> via <see cref="ReviewFilingRequestParser"/>.
    ///
    /// <para>
    /// This is the SAME parse path exercised by the 48/48-green Tier A round-trip
    /// tests — if parsing fails here, the Tier A test for the same scenario would
    /// also fail. The smoke test in <c>ScenarioFixturesSmokeTests</c> asserts this
    /// invariant (all 46 Madera-reachable scenarios parse cleanly).
    /// </para>
    /// </summary>
    public static FilingSubmission LoadSubmission(string scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            throw new ArgumentException("Scenario ID must be non-empty.", nameof(scenarioId));

        var scenario = CanonicalScenarios.GetById(scenarioId);
        var xml = SampleLoader.LoadXmlText(scenario);
        return ReviewFilingRequestParser.FromXml(xml);
    }

    /// <summary>
    /// Load the canonical baseline XML for the given scenario and parse it into a
    /// <see cref="FilingSubmission"/>. Overload taking the <see cref="CanonicalScenario"/>
    /// directly to avoid the ID round-trip when the caller already has the scenario.
    /// </summary>
    public static FilingSubmission LoadSubmission(CanonicalScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var xml = SampleLoader.LoadXmlText(scenario);
        return ReviewFilingRequestParser.FromXml(xml);
    }
}
