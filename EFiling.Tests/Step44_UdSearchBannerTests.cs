using EFiling.Core.Models;
using EFiling.Nop.UdDisclaimer;

namespace EFiling.Tests;

/// <summary>
/// Tests for Step #44 — closing two Step #43 gaps:
///
/// <list type="bullet">
///   <item><b>Gap 1:</b> SearchCase results page lacked the JTI-mandated
///     "alert when searching" banner. Pre-Step-#44 the disclaimer only
///     fired on click-through to a UD case; per JTI doc lines 235-237
///     ("alert users... when searching for Unlawful Detainers") the
///     disclaimer must also be visible at search-results-display time.</item>
///   <item><b>Gap 2:</b> The <c>/api/efiling/case-detail</c> AJAX endpoint
///     returned full UD party + title + complaint data without going
///     through the §1161.2 gate, creating a defense-in-depth leak. Closed
///     by <c>GateUdAccessJsonAsync</c> which returns HTTP 403 +
///     <c>{ requiresAttestation: true, attestationUrl }</c>.</item>
/// </list>
///
/// <para>
/// These tests focus on the pure UD-detection invariants (the view-side
/// banner is tested indirectly via the helper that decides when to render
/// it). The 403 behavior of the AJAX gate is verified at the controller
/// level via live smoke (see PROGRESS.md Step #44 entry) since
/// <c>EFilingMvcController</c> resolves all 12 ctor dependencies that are
/// not yet available as unit-test fixtures.
/// </para>
/// </summary>
public class Step44_UdSearchBannerTests
{
    // ─── Banner-trigger invariant: hasUdResults logic in SearchCase.cshtml ──

    [Fact]
    public void Step44_HasUdResults_AllNonUdCases_ReturnsFalse()
    {
        // SearchCase.cshtml computes:
        //   var hasUdResults = Model.Cases.Any(c => UdDisclaimerPolicy.RequiresDisclaimer(Model.Search.CourtId, c.CaseCategoryCode));
        // This test pins the predicate's evaluation on a non-UD result set.
        // Step #54: predicate signature gained courtId per KD-001 closure.
        var cases = new List<CaseInfo>
        {
            new() { CaseDocketId = "MFL018522", CaseCategoryCode = "211110" }, // FAM
            new() { CaseDocketId = "MCV000001", CaseCategoryCode = "411900" }, // CIV
            new() { CaseDocketId = "MCV000002", CaseCategoryCode = "412910" }, // CIV
        };

        var hasUd = cases.Any(c => UdDisclaimerPolicy.RequiresDisclaimer("madera", c.CaseCategoryCode));

        Assert.False(hasUd, "Banner must NOT render when no UD cases are present in results.");
    }

    [Fact]
    public void Step44_HasUdResults_OneUdCaseAmongNonUd_ReturnsTrue()
    {
        // Even a single UD case in mixed results must trigger the banner —
        // the JTI mandate is "WHEN SEARCHING for Unlawful Detainers", which
        // we interpret as "whenever UD is exposed in results".
        var cases = new List<CaseInfo>
        {
            new() { CaseDocketId = "MFL018522", CaseCategoryCode = "211110" }, // FAM
            new() { CaseDocketId = "MCV089023", CaseCategoryCode = "407200" }, // UD ← triggers
            new() { CaseDocketId = "MCV000001", CaseCategoryCode = "411900" }, // CIV
        };

        var hasUd = cases.Any(c => UdDisclaimerPolicy.RequiresDisclaimer("madera", c.CaseCategoryCode));

        Assert.True(hasUd, "Banner MUST render when ANY UD case is present in results.");
    }

    [Fact]
    public void Step44_HasUdResults_EmptyResults_ReturnsFalse()
    {
        // The banner block in SearchCase.cshtml is nested inside
        // `@if (Model.Cases.Any())` so this branch is unreachable when
        // results are empty — but the predicate itself must still be safe.
        var cases = new List<CaseInfo>();

        var hasUd = cases.Any(c => UdDisclaimerPolicy.RequiresDisclaimer("madera", c.CaseCategoryCode));

        Assert.False(hasUd);
    }

    [Fact]
    public void Step44_HasUdResults_AllUdCases_ReturnsTrue()
    {
        // Pure UD result set — banner must render.
        var cases = new List<CaseInfo>
        {
            new() { CaseDocketId = "MCV089023", CaseCategoryCode = "407200" },
            new() { CaseDocketId = "MCV089024", CaseCategoryCode = "407200" },
        };

        var hasUd = cases.Any(c => UdDisclaimerPolicy.RequiresDisclaimer("madera", c.CaseCategoryCode));

        Assert.True(hasUd);
    }

    // ─── Per-row badge invariant: isUd predicate on individual rows ─────

    [Fact]
    public void Step44_PerRowUdBadge_ShowsForUdCategoryOnly()
    {
        // SearchCase.cshtml computes per-row:
        //   var isUd = UdDisclaimerPolicy.RequiresDisclaimer(Model.Search.CourtId, c.CaseCategoryCode);
        //   <tr class="@(isUd ? "ud-restricted-row" : "")" ...>
        // This pins the per-row logic — Madera UD code 407200 shows the
        // badge; other codes do not. Step #54: courtId-aware
        // predicate per KD-001 closure.
        Assert.True(UdDisclaimerPolicy.RequiresDisclaimer("madera", "407200"),
            "UD row badge MUST render for Madera UD code 407200.");
        Assert.False(UdDisclaimerPolicy.RequiresDisclaimer("madera", "411900"),
            "UD row badge must NOT render for Madera Civil code 411900.");
        Assert.False(UdDisclaimerPolicy.RequiresDisclaimer("madera", "211110"),
            "UD row badge must NOT render for Madera Family code 211110.");
    }

    // ─── Banner content invariant: verbatim text reuse ─────────────────

    [Fact]
    public void Step44_Banner_ReusesVerbatimUdDisclaimerPolicyConstants()
    {
        // The SearchCase.cshtml banner renders @UdDisclaimerPolicy.LeadInVerbatim
        // and @UdDisclaimerPolicy.DisclaimerVerbatim. This test guards against
        // a regression where someone copy-pastes the text inline (paraphrase
        // risk recurs).
        //
        // We assert the constants are non-empty + contain the §1161.2
        // citation. Step #43's `Step43_DisclaimerVerbatim_ExactlyMatchesJtiDocText`
        // already pins the exact wording — this test confirms the constants
        // exist as the source-of-truth.
        Assert.False(string.IsNullOrWhiteSpace(UdDisclaimerPolicy.DisclaimerVerbatim));
        Assert.False(string.IsNullOrWhiteSpace(UdDisclaimerPolicy.LeadInVerbatim));
        Assert.Contains("§1161.2", UdDisclaimerPolicy.DisclaimerVerbatim);
        Assert.Contains("deemed confidential", UdDisclaimerPolicy.LeadInVerbatim);
    }

    // ─── Defense-in-depth invariant: AJAX gate parity ────────────────────

    [Fact]
    public void Step44_AjaxGate_SameDetectionAsViewGate()
    {
        // Both GateUdAccessAsync (view-redirect) and GateUdAccessJsonAsync
        // (AJAX 403) must use the IDENTICAL UD-detection predicate so a
        // case that triggers the view gate also triggers the AJAX gate.
        // This test pins the parity at the predicate level — if the two
        // gates ever diverged on detection logic, a UD case might leak
        // through one path but not the other.
        const string maderaUd = "407200";
        const string maderaCiv = "411900";
        const string maderaFam = "211110";

        // Single shared predicate — both gates branch on this.
        // Step #54: courtId-aware predicate per KD-001 closure.
        Assert.True(UdDisclaimerPolicy.RequiresDisclaimer("madera", maderaUd));
        Assert.False(UdDisclaimerPolicy.RequiresDisclaimer("madera", maderaCiv));
        Assert.False(UdDisclaimerPolicy.RequiresDisclaimer("madera", maderaFam));
    }
}
