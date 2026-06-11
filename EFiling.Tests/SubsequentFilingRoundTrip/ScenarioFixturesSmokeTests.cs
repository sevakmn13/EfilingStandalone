namespace EFiling.Tests.SubsequentFilingRoundTrip;

/// <summary>
/// Smoke tests for <see cref="ScenarioFixtures"/> — the scenario → FilingSubmission
/// helper that gates all Tier B live-Madera tests. These run in the default test
/// suite (no special trait) because they don't touch the network; failure here
/// means the Tier B harness is broken irrespective of credentials or endpoints.
/// </summary>
public class ScenarioFixturesSmokeTests
{
    [Fact]
    public void MaderaReachable_Contains46Scenarios_AcrossCivilFamilyProbate()
    {
        var reachable = ScenarioFixtures.MaderaReachable;

        Assert.Equal(46, reachable.Count);
        Assert.Equal(32, reachable.Count(s => s.Category == CaseCategory.Civil));
        Assert.Equal(10, reachable.Count(s => s.Category == CaseCategory.FamilyLaw));
        Assert.Equal(4,  reachable.Count(s => s.Category == CaseCategory.Probate));
        Assert.DoesNotContain(reachable, s => s.Category == CaseCategory.MentalHealth);
    }

    [Fact]
    public void MaderaBlocked_Contains2MentalHealthScenarios()
    {
        var blocked = ScenarioFixtures.MaderaBlocked;

        Assert.Equal(2, blocked.Count);
        Assert.Contains(blocked, s => s.Id == "MH-INI-001");
        Assert.Contains(blocked, s => s.Id == "MH-INI-002");
        Assert.All(blocked, s => Assert.Equal(CaseCategory.MentalHealth, s.Category));
    }

    [Fact]
    public void MaderaReachableAndBlocked_AreDisjointAndCoverAll48()
    {
        var reachableIds = ScenarioFixtures.MaderaReachable.Select(s => s.Id).ToHashSet();
        var blockedIds = ScenarioFixtures.MaderaBlocked.Select(s => s.Id).ToHashSet();

        Assert.Empty(reachableIds.Intersect(blockedIds));
        Assert.Equal(48, reachableIds.Count + blockedIds.Count);
        Assert.Equal(CanonicalScenarios.All.Count, reachableIds.Count + blockedIds.Count);
    }

    /// <summary>
    /// The Tier B harness depends on <see cref="ScenarioFixtures.LoadSubmission(string)"/>
    /// parsing every reachable scenario into a non-null <see cref="EFiling.Core.Models.FilingSubmission"/>.
    /// This is a stronger version of the Tier A round-trip tests for the parse half only:
    /// Tier A asserts parse-then-rebuild equivalence; this asserts parse-succeeds for all
    /// 46 Madera-reachable scenarios. If this ever fails, the Tier B tests for the same
    /// scenario cannot run.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScenarioFixtures.MaderaReachableScenarioIds), MemberType = typeof(ScenarioFixtures))]
    public void LoadSubmission_ParsesEveryMaderaReachableScenario(string scenarioId)
    {
        var submission = ScenarioFixtures.LoadSubmission(scenarioId);

        Assert.NotNull(submission);
        Assert.False(string.IsNullOrEmpty(submission.EfspReferenceId),
            $"[{scenarioId}] Parser did not populate EfspReferenceId.");
        Assert.NotNull(submission.LeadDocument);
    }
}
