using EFiling.Core.Caching;
using EFiling.Core.Models;
using EFiling.Providers.JTI;
using EFiling.Tests.SubsequentFilingRoundTrip;
using Xunit.Abstractions;

namespace EFiling.Tests.LiveMadera;

/// <summary>
/// Tier B — Live Madera submission scoreboard (catalog §2.6.2 Layer C).
///
/// <para>
/// <b>Design.</b> One <see cref="Xunit.TheoryAttribute"/> parameterized over the 46
/// Madera-reachable canonical scenarios. For each scenario the test pipeline is:
/// <list type="number">
///   <item>Parse the baseline XML into a <see cref="FilingSubmission"/> via
///         <see cref="ScenarioFixtures.LoadSubmission(string)"/>. (This is the
///         same parse already exercised green by Tier A round-trip tests.)</item>
///   <item>Apply common placeholder → Madera substitutions
///         (<see cref="MaderaLiveFixtures.ApplyCommonOverrides"/>).</item>
///   <item>Apply scenario-specific fixture overrides if curated
///         (<see cref="MaderaLiveFixtures.TryGetScenarioOverride"/>). If the
///         scenario is pending curation, log and return — we do not submit
///         half-baked payloads to Madera.</item>
///   <item>Guard with <see cref="TestConfiguration.RequireStaging"/> so a
///         misconfigured production endpoint throws before any wire call.</item>
///   <item>Submit via <see cref="JtiEFilingProvider.SubmitFilingAsync"/> and
///         assert <see cref="FilingResult.Success"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Opt-in execution.</b> Class-level <see cref="Xunit.TraitAttribute"/>
/// <c>Category = LiveMadera</c> excludes these tests from default CI runs. Explicit
/// invocation:
/// <code>
/// dotnet test --filter "Category=LiveMadera"
/// </code>
/// </para>
///
/// <para>
/// <b>Current state.</b> Scaffolding complete. Zero scenarios curated
/// (<see cref="MaderaLiveFixtures.CuratedScenarioCount"/> = 0). All 46 reachable
/// scenarios will report as "pending curation" when this theory runs — which is
/// honest: we don't have the per-scenario Madera fixture data yet. As scenarios
/// get curated (entries added to <see cref="MaderaLiveFixtures"/>'s override
/// registry), this scoreboard transitions from pending → green scenario-by-scenario.
/// See <c>docs/MADERA_FIXTURE_CURATION.md</c> for the curation checklist.
/// </para>
/// </summary>
[Trait("Category", "LiveMadera")]
public class TierB_LiveMaderaSubmitTests
{
    private readonly ITestOutputHelper _output;

    public TierB_LiveMaderaSubmitTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Parameterized per-scenario Tier B live submission. See class docstring for
    /// the full pipeline. Pending-curation scenarios log and return without
    /// touching the network — the assertion that they reach Madera only happens
    /// once a scenario override is registered in <see cref="MaderaLiveFixtures"/>.
    /// </summary>
    [LiveMaderaTheory]
    [MemberData(nameof(ScenarioFixtures.MaderaReachableScenarioIds),
                MemberType = typeof(ScenarioFixtures))]
    public async Task TierB_LiveMaderaSubmit(string scenarioId)
    {
        _output.WriteLine($"[TierB] Scenario: {scenarioId}");

        // 1. Parse baseline → FilingSubmission (same path as Tier A — trusted green).
        var submission = ScenarioFixtures.LoadSubmission(scenarioId);
        _output.WriteLine($"[TierB] Parsed submission: FilingType={submission.FilingType}, " +
                          $"CaseTypeCode={submission.CaseTypeCode}, " +
                          $"Parties={submission.Parties.Count}, " +
                          $"Docs={(submission.LeadDocument != null ? 1 : 0) + submission.ConnectedDocuments.Count}");

        // 2. Apply common placeholder → Madera substitutions (credentials, reference id).
        MaderaLiveFixtures.ApplyCommonOverrides(submission, scenarioId);

        // 3. Apply scenario-specific overrides if curated; otherwise report pending
        //    (or consumed) and skip the network call. This is the honest
        //    "scaffolding-complete, curation-pending/consumed" state documented in
        //    the class docstring.
        if (!MaderaLiveFixtures.TryGetScenarioOverride(scenarioId, out var scenarioOverride))
        {
            if (MaderaLiveFixtures.ConsumedScenarioIds.Contains(scenarioId))
            {
                _output.WriteLine(
                    $"[TierB] CONSUMED: scenario {scenarioId} was previously curated but its Madera " +
                    $"staging fixture has been consumed by a prior run (e.g. the attorney it would " +
                    $"substitute out is no longer on the case). Re-enabling requires provisioning a " +
                    $"fresh case in Madera staging and re-adding the override; see the CONSUMED block " +
                    $"in MaderaLiveFixtures.cs for the re-enable template. " +
                    $"Skipping live submission — test passes as a scaffold-health check.");
            }
            else
            {
                _output.WriteLine(
                    $"[TierB] PENDING CURATION: scenario {scenarioId} has no MaderaLiveFixtures entry yet. " +
                    $"See docs/MADERA_FIXTURE_CURATION.md for what's needed. " +
                    $"Skipping live submission — test passes as a scaffold-health check.");
            }
            return;
        }

        scenarioOverride!(submission);
        _output.WriteLine($"[TierB] Applied scenario-specific overrides for {scenarioId}.");

        // 4. Safety guard — refuse to run against anything other than explicitly-Staging.
        var config = MaderaLiveFixtures.MaderaStagingConfig;
        TestConfiguration.RequireStaging(config, $"TierB_LiveMaderaSubmit({scenarioId})");

        // 5. Submit to Madera staging.
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        FilingResult result;
        try
        {
            result = await provider.SubmitFilingAsync(config, submission);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"[TierB] EXCEPTION during SubmitFilingAsync: {ex.GetType().FullName}: {ex.Message}");
            if (ex is Providers.JTI.Soap.JtiSoapException soapEx)
            {
                _output.WriteLine($"[TierB] HTTP Status: {soapEx.HttpStatusCode}");
                _output.WriteLine($"[TierB] Response Body:\n{soapEx.ResponseBody}");
            }
            throw;
        }

        // 6. Log the raw result for diagnostic purposes (capturing goes to docs/live_captures/
        //    via the separate shadow-validation harness — this test records via xUnit output only).
        _output.WriteLine($"[TierB] Success={result.Success} EfmReferenceId={result.EfmReferenceId ?? "(null)"} " +
                          $"EfspReferenceId={result.EfspReferenceId ?? "(null)"} Status={result.Status}");
        if (!result.Success)
            _output.WriteLine($"[TierB] ErrorCode={result.ErrorCode} ErrorText={result.ErrorText ?? "(null)"}");

        Assert.True(result.Success,
            $"[TierB] Madera rejected scenario {scenarioId}. ErrorCode={result.ErrorCode}, " +
            $"ErrorText={result.ErrorText}. " +
            $"Investigation: (a) review request XML captured via provider logs, " +
            $"(b) compare against baseline sample for field-level divergence, " +
            $"(c) update the override in MaderaLiveFixtures if a codelist value or " +
            $"attorney/case reference needs refresh.");
    }

    /// <summary>
    /// Scaffold-health summary. Reports how many of the 46 reachable scenarios have
    /// been curated with Madera fixture data. Always runs (no trait gate), always
    /// passes — its job is to surface the "how far along is Tier B" signal in the
    /// test output.
    /// </summary>
    [Fact]
    [Trait("Category", "LiveMaderaStatus")]
    public void Status_ReportsTierBCurationProgress()
    {
        var total = ScenarioFixtures.MaderaReachable.Count;
        var curated = MaderaLiveFixtures.CuratedScenarioCount;
        var consumed = MaderaLiveFixtures.ConsumedScenarioCount;
        var pending = total - curated - consumed;
        var blocked = ScenarioFixtures.MaderaBlocked.Count;

        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine($"  Tier B — Live Madera Curation Progress");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine($"  Reachable on Madera:        {total} scenarios");
        _output.WriteLine($"  Curated (live-ready):       {curated} scenarios");
        _output.WriteLine($"  Pending curation:           {pending} scenarios");
        _output.WriteLine($"  Consumed (needs new case):  {consumed} scenarios");
        _output.WriteLine($"  Blocked (not in Madera):    {blocked} scenarios (Mental Health)");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        if (pending > 0)
        {
            _output.WriteLine("  Pending scenarios:");
            foreach (var id in MaderaLiveFixtures.PendingCurationScenarioIds)
                _output.WriteLine($"    - {id}");
            _output.WriteLine("");
            _output.WriteLine("  See docs/MADERA_FIXTURE_CURATION.md for what each needs.");
        }
        if (consumed > 0)
        {
            _output.WriteLine("  Consumed scenarios (staging fixture spent, re-enable needs a new case):");
            foreach (var id in MaderaLiveFixtures.ConsumedScenarioIds)
                _output.WriteLine($"    - {id}");
            _output.WriteLine("");
            _output.WriteLine("  See the CONSUMED blocks in MaderaLiveFixtures.cs for per-scenario forensic notes.");
        }

        // Always passes — this is a status reporter, not a gate.
        Assert.True(curated + pending + consumed == total);
    }
}
