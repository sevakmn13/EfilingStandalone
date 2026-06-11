using System.Reflection;

namespace EFiling.Tests.SubsequentFilingRoundTrip;

/// <summary>
/// Tier A — Round-trip coverage drift-guard for canonical SF + CI scenarios.
///
/// <para>
/// <b>Historical role (T-2 Pass 1, pre-2026-05-29):</b> this class was a 48-row
/// <c>[Theory(Skip = ...)]</c> placeholder representing the unimplemented round-trip
/// harness — the original "T-2 Pass 2" work item that required
/// <c>ReviewFilingRequestParser</c> + <c>XmlStructuralDiff</c> helpers and per-scenario
/// activation.
/// </para>
///
/// <para>
/// <b>Current role (T-2 Pass 2 closed, 2026-05-29 SF closeout):</b> both helpers exist
/// (<see cref="EFiling.Providers.JTI.Parsers.ReviewFilingRequestParser"/> + the
/// test-folder <c>XmlStructuralDiff</c>) and all 48 canonical scenarios are pinned by
/// per-scenario <c>[Fact]</c> methods in:
/// </para>
/// <list type="bullet">
///   <item><see cref="TierA_CaseInitiationRoundTripTests"/> — 22 CI scenarios
///         (CIV-INI-001..013, FAM-INI-001..004, MH-INI-001..002, PRO-INI-001..003).</item>
///   <item><see cref="TierA_SubsequentFilingRoundTripTests"/> — 26 SF scenarios
///         (CIV-SUB-001..019, FAM-SUB-001..006, PRO-SUB-001).</item>
/// </list>
///
/// <para>
/// <b>What this class does now</b> — a single forcing-function test that uses reflection
/// to assert every <see cref="CanonicalScenarios.All"/> ID has a corresponding
/// <c>[Fact]</c>-decorated method named <c>{ScenarioId-with-underscores}_RoundTrip*</c>
/// in one of the two real test classes. When a future scenario is added to
/// <see cref="CanonicalScenarios.All"/>, this guard fails until a round-trip test is
/// authored — preserving the original T-2 Pass 1 forcing-function intent without keeping
/// 48 dead skipped rows in the scoreboard.
/// </para>
/// </summary>
public class TierA_RoundTripTests
{
    /// <summary>
    /// Forcing function: every <see cref="CanonicalScenarios.All"/> ID must have a
    /// round-trip <c>[Fact]</c> in <see cref="TierA_CaseInitiationRoundTripTests"/> or
    /// <see cref="TierA_SubsequentFilingRoundTripTests"/>. Methods are conventionally
    /// named <c>{ID-with-dashes-as-underscores}_RoundTrip_ParseBuildAndCompareStructurally</c>
    /// (e.g., <c>CIV_SUB_001_RoundTrip_ParseBuildAndCompareStructurally</c>).
    /// </summary>
    [Fact]
    public void EveryCanonicalScenario_HasRoundTripCoverage()
    {
        var ciFacts = GetFactMethodNames(typeof(TierA_CaseInitiationRoundTripTests));
        var sfFacts = GetFactMethodNames(typeof(TierA_SubsequentFilingRoundTripTests));
        var allFacts = ciFacts.Union(sfFacts, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        foreach (var scenario in CanonicalScenarios.All)
        {
            // Convention: scenario ID "CIV-SUB-001" → method prefix "CIV_SUB_001_".
            var expectedPrefix = scenario.Id.Replace('-', '_') + "_";
            if (!allFacts.Any(name => name.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)))
                missing.Add(scenario.Id);
        }

        Assert.True(
            missing.Count == 0,
            $"Round-trip coverage gap: {missing.Count} of {CanonicalScenarios.All.Count} canonical "
            + $"scenarios have no [Fact] method matching '<ID-with-underscores>_*' in either "
            + $"TierA_CaseInitiationRoundTripTests or TierA_SubsequentFilingRoundTripTests. "
            + $"Missing: {string.Join(", ", missing)}. "
            + $"Add a [Fact] per missing scenario calling AssertRoundTrip(\"<ID>\").");
    }

    /// <summary>
    /// Negative-case sanity: this guard is non-trivial. If we mis-write the scenario→method
    /// convention check, this test should still pass for the current 48 scenarios. So we
    /// also assert the count of matched scenarios equals <see cref="CanonicalScenarios.All"/>
    /// — catches a regression where the matcher silently matches everything (or nothing).
    /// </summary>
    [Fact]
    public void EveryCanonicalScenario_HasRoundTripCoverage_PositiveCountSanity()
    {
        var ciFacts = GetFactMethodNames(typeof(TierA_CaseInitiationRoundTripTests));
        var sfFacts = GetFactMethodNames(typeof(TierA_SubsequentFilingRoundTripTests));
        var allFacts = ciFacts.Union(sfFacts, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matched = CanonicalScenarios.All.Count(scenario =>
        {
            var prefix = scenario.Id.Replace('-', '_') + "_";
            return allFacts.Any(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        });

        // Hard-coded 48 — if CanonicalScenarios.All grows, this test must be updated in
        // lockstep with the matched-count expectation, which forces a deliberate review of
        // whether the new scenarios were also added to the round-trip test classes.
        Assert.Equal(48, CanonicalScenarios.All.Count);
        Assert.Equal(48, matched);
    }

    private static IEnumerable<string> GetFactMethodNames(Type testClass) =>
        testClass
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttributes<FactAttribute>(inherit: false).Any())
            .Select(m => m.Name);
}
