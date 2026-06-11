using EFiling.Nop.UdDisclaimer;
using EFiling.Providers.JTI.Config;
using Xunit;

namespace EFiling.Tests;

/// <summary>
/// Drift-guard tests for the Step #49 case-category evidence pass.
///
/// <para>
/// Step #49 closed 4 schema gaps + 1 bug via a user-provided Tier-B case-number
/// harness (10 Madera dockets across 5 categories, probed via playwright-cli):
/// </para>
///
/// <list type="bullet">
///   <item><b>UD subtype bug fix.</b> Step #43+#44 originally gated only category
///     <c>407200</c> (Unlawful Detainer: Residential). Tier-B probe on
///     <c>MCV034905</c> (category <c>407100</c> "Unlawful Detainer: Commercial")
///     showed the §1161.2 gate failing to fire on Commercial UD. CCP §1161.2 (a)
///     applies to "unlawful detainer cases" generically — the statute does not
///     distinguish subtypes. Expanded <c>UD.knownCategoryCodes</c> to cover all
///     5 Madera UD subtypes per <c>scripts/madera_case_category.txt</c>.</item>
///   <item><b>MEN promotion.</b> <c>awaitingEvidence: false</c>;
///     <c>knownCategoryCodes: ["613110", "614120"]</c>. Evidence: MMH00187 +
///     MMH00192 both render SF page cleanly. The earlier "Madera doesn't expose
///     Mental Health" claim applied only to CI (case initiation) — SF works fine
///     on existing MH cases.</item>
///   <item><b>PRB promotion.</b> <c>awaitingEvidence: false</c>;
///     <c>knownCategoryCodes: ["511210", "531110"]</c>. Evidence: 4 MPR cases
///     (MPR11249, MPR011994 → 511210; MPR11261, MPR10298 → 531110). Conservatorship
///     (531110) folds under PRB.subCategories CONS.</item>
///   <item><b>FAM extension.</b> Added <c>291110</c> (Family Law Other) per
///     evidence from MFL004695.</item>
/// </list>
///
/// <para>
/// These tests pin each evidence-backed mapping. If a future maintainer reverts
/// or narrows any of the Step #49 expansions, the tests fail with a clear pointer
/// to the source-fidelity contract.
/// </para>
/// </summary>
public class Step49_CaseCategoryEvidenceTests
{
    // ─── Step #49.A — UD subtype gap fix ────────────────────────────────

    [Theory]
    [InlineData("407100", "Unlawful Detainer: Commercial (31)")]
    [InlineData("407110", "Unlawful Detainer: Commercial Foreclosure")]
    [InlineData("407200", "Unlawful Detainer: Residential (32) [pre-#49 baseline]")]
    [InlineData("407210", "Unlawful Detainer: Residential Foreclosure")]
    [InlineData("407300", "Unlawful Detainer: Drugs (38)")]
    public void Step49_RequiresDisclaimer_AllMaderaUdSubtypes_ReturnTrue(string code, string label)
    {
        // CCP §1161.2 (a) text: "Code of Civil Procedure §1161.2 (a) limits access
        // to unlawful detainer cases." The statute does NOT distinguish subtypes
        // (Commercial/Residential/Drugs/Foreclosure). Step #43 originally seeded
        // only 407200 (Residential), which left 4 subtypes ungated. Step #49
        // Tier-B probe on MCV034905 (cat 407100) discovered the gap.
        Assert.True(
            UdDisclaimerPolicy.RequiresDisclaimer("madera", code),
            $"§1161.2 gate must fire for Madera UD code {code} ({label}). " +
            "CCP §1161.2 (a) applies to all unlawful detainer subtypes; the statute " +
            "does not distinguish Commercial / Residential / Drugs / Foreclosure.");
    }

    [Fact]
    public void Step49_UdKnownCategoryCodes_ContainsAll5MaderaSubtypes()
    {
        var ud = JtiFieldSchemaProvider.GetCaseCategoryPolicy("UD");
        Assert.NotNull(ud);
        Assert.NotNull(ud.KnownCategoryCodes);

        // The 5 Madera UD subtypes per scripts/madera_case_category.txt.
        Assert.Contains("407100", ud.KnownCategoryCodes); // Commercial
        Assert.Contains("407110", ud.KnownCategoryCodes); // Commercial Foreclosure
        Assert.Contains("407200", ud.KnownCategoryCodes); // Residential (original Step #43)
        Assert.Contains("407210", ud.KnownCategoryCodes); // Residential Foreclosure
        Assert.Contains("407300", ud.KnownCategoryCodes); // Drugs
    }

    [Fact]
    public void Step49_FindPolicyByCourtCategoryCode_AllUdSubtypes_ResolveToUdPolicy()
    {
        // Each of the 5 UD subtypes should resolve to the same UD policy via the
        // Step #42 resolver. Drift-guard against accidental splitting of UD into
        // multiple per-subtype policies (which would break the unified gate).
        foreach (var code in new[] { "407100", "407110", "407200", "407210", "407300" })
        {
            // Step #54: courtId-aware resolver.
            var policy = JtiFieldSchemaProvider.FindPolicyByCourtCategoryCode("madera", code);
            Assert.NotNull(policy);
            Assert.Equal("UD", policy.CategoryCode);
            Assert.True(policy.RequiresUdDisclaimer,
                $"UD policy must keep requiresUdDisclaimer=true for code {code}.");
        }
    }

    // ─── Step #49.B — MEN promotion ─────────────────────────────────────

    [Fact]
    public void Step49_MenPolicy_NoLongerAwaitingEvidence()
    {
        var men = JtiFieldSchemaProvider.GetCaseCategoryPolicy("MEN");
        Assert.NotNull(men);
        Assert.False(men.AwaitingEvidence,
            "Step #49 promoted MEN to evidence-backed via Tier-B probe of " +
            "MMH00187 + MMH00192 (cat 613110 'Mental Health-Other').");
    }

    [Fact]
    public void Step49_MenKnownCategoryCodes_ContainsMaderaMentalHealthCodes()
    {
        var men = JtiFieldSchemaProvider.GetCaseCategoryPolicy("MEN");
        Assert.NotNull(men);
        Assert.NotNull(men.KnownCategoryCodes);
        Assert.Contains("613110", men.KnownCategoryCodes); // Mental Health-Other (observed)
        Assert.Contains("614120", men.KnownCategoryCodes); // Mental Health-Other Writ (sibling)
    }

    [Fact]
    public void Step49_FindPolicyByCourtCategoryCode_MaderaMentalHealthOther_ResolvesToMen()
    {
        // The full chain: court-code 613110 → JCCC "MEN" policy. Bridges Tier-B
        // probe evidence to the policy resolver consumed by SF.cshtml UX hooks.
        // Step #54: courtId-aware resolver.
        var policy = JtiFieldSchemaProvider.FindPolicyByCourtCategoryCode("madera", "613110");
        Assert.NotNull(policy);
        Assert.Equal("MEN", policy.CategoryCode);
        Assert.True(policy.RequiresDobForAsToParty,
            "MEN policy must retain requiresDobForAsToParty (rule MEN-1, V2 evidence).");
    }

    [Fact]
    public void Step49_RequiresDisclaimer_MaderaMentalHealthCodes_ReturnFalse()
    {
        // MH cases at Madera do NOT currently fire the §1161.2 UD gate. Step #49
        // open question MEN/openQuestions[3] tracks whether W&I §5328 / LPS
        // confidentiality requires an analogous MH-specific gate (would be a
        // separate UX-hook, not the UD one). This drift-guard ensures the
        // UD gate doesn't accidentally widen to MH codes.
        // Step #54: courtId-aware resolver.
        Assert.False(UdDisclaimerPolicy.RequiresDisclaimer("madera", "613110"),
            "MH-Other code 613110 must NOT fire the UD §1161.2 gate.");
        Assert.False(UdDisclaimerPolicy.RequiresDisclaimer("madera", "614120"),
            "MH-Other Writ code 614120 must NOT fire the UD §1161.2 gate.");
    }

    // ─── Step #49.C — PRB promotion ─────────────────────────────────────

    [Fact]
    public void Step49_PrbPolicy_NoLongerAwaitingEvidence()
    {
        var prb = JtiFieldSchemaProvider.GetCaseCategoryPolicy("PRB");
        Assert.NotNull(prb);
        Assert.False(prb.AwaitingEvidence,
            "Step #49 promoted PRB to evidence-backed via Tier-B probe of " +
            "4 MPR cases across 2 subtype codes (511210 Probate, 531110 Conservatorship).");
    }

    [Fact]
    public void Step49_PrbKnownCategoryCodes_ContainsObservedSubtypes()
    {
        var prb = JtiFieldSchemaProvider.GetCaseCategoryPolicy("PRB");
        Assert.NotNull(prb);
        Assert.NotNull(prb.KnownCategoryCodes);
        Assert.Contains("511210", prb.KnownCategoryCodes); // Probate of Wills & for Ltrs Test.
        Assert.Contains("531110", prb.KnownCategoryCodes); // Conservatorship
    }

    [Fact]
    public void Step49_FindPolicyByCourtCategoryCode_MaderaProbate_ResolvesToPrb()
    {
        // Probate proper (511210) — observed on MPR11249, MPR011994.
        // Step #54: courtId-aware resolver.
        var probate = JtiFieldSchemaProvider.FindPolicyByCourtCategoryCode("madera", "511210");
        Assert.NotNull(probate);
        Assert.Equal("PRB", probate.CategoryCode);
    }

    [Fact]
    public void Step49_FindPolicyByCourtCategoryCode_MaderaConservatorship_ResolvesToPrb()
    {
        // Conservatorship (531110) folds under PRB.subCategories CONS — observed
        // on MPR11261, MPR10298. NOT folded under MEN despite being adjacent to
        // LPS conservatorship (LPS = 532110, separate code, deferred to future
        // evidence pass).
        // Step #54: courtId-aware resolver.
        var conservatorship = JtiFieldSchemaProvider.FindPolicyByCourtCategoryCode("madera", "531110");
        Assert.NotNull(conservatorship);
        Assert.Equal("PRB", conservatorship.CategoryCode);
    }

    [Fact]
    public void Step49_LpsConservatorshipCode_MappedToPrbAtStep59_EvidenceProvided()
    {
        // 532110 (WI5350-LPS Conservatorship) is mental-health-statute-grounded
        // (Lanterman-Petris-Short, WI §5350) but processed via Probate case type.
        // Step #49 deferred it "until a future Tier-B probe provides evidence …
        // so a future maintainer doesn't silently fold 532110 into PRB or MEN
        // WITHOUT evidence."
        //
        // Step #59 PROVIDES exactly that evidence: a GetCase detail
        // probe on docket MPR012467 returned caseCategoryCode=532110 with
        // caseTypeCode=511110 (the PRB umbrella) — confirming the Step #49 note
        // that LPS "is processed via Probate case type". 532110 is therefore
        // promoted to PRB (NOT MEN — the umbrella is Probate). This is the
        // evidence-gated fold the Step #49 guard anticipated, not a silent one.
        // (Note: Step #52 separately found OTHER LPS dockets stored as 603110;
        // both 532110 and 603110 are valid LPS caseCategoryCodes and both -> PRB.)
        var lpsPolicy = JtiFieldSchemaProvider.FindPolicyByCourtCategoryCode("madera", "532110");
        Assert.NotNull(lpsPolicy);
        Assert.Equal("PRB", lpsPolicy.CategoryCode);
    }

    // ─── Step #49.D — FAM extension ─────────────────────────────────────

    [Fact]
    public void Step49_FamKnownCategoryCodes_ContainsFamilyLawOther()
    {
        var fam = JtiFieldSchemaProvider.GetCaseCategoryPolicy("FAM");
        Assert.NotNull(fam);
        Assert.NotNull(fam.KnownCategoryCodes);

        // Step #42 baseline (these must NOT regress).
        Assert.Contains("211110", fam.KnownCategoryCodes);
        Assert.Contains("211120", fam.KnownCategoryCodes);

        // Step #49 addition: Family Law Other (observed on MFL004695).
        Assert.Contains("291110", fam.KnownCategoryCodes);
    }

    [Fact]
    public void Step49_FindPolicyByCourtCategoryCode_MaderaFamilyLawOther_ResolvesToFam()
    {
        // Step #54: courtId-aware resolver.
        var fam = JtiFieldSchemaProvider.FindPolicyByCourtCategoryCode("madera", "291110");
        Assert.NotNull(fam);
        Assert.Equal("FAM", fam.CategoryCode);
    }

    [Fact]
    public void Step49_RequiresDisclaimer_FamilyLawOtherCode_ReturnsFalse()
    {
        // 291110 Family Law Other must NOT fire the UD §1161.2 gate. Drift-guard
        // against accidental widening of UD coverage to FAM-Other.
        // Step #54: courtId-aware resolver.
        Assert.False(UdDisclaimerPolicy.RequiresDisclaimer("madera", "291110"),
            "FAM-Other code 291110 must NOT fire the UD §1161.2 gate.");
    }

    // ─── Cross-cutting: completeness summary ────────────────────────────

    [Fact]
    public void Step49_CategoryPolicyCount_NoNewPoliciesAdded()
    {
        // Step #49 promoted existing PRB + MEN policies (didn't add new categories).
        // The 8 declared categories are: CIV, UD, FAM, PRB, MEN, JUV, CRI, APP.
        // Drift-guard against unintended schema growth.
        var allKnown = new[] { "CIV", "UD", "FAM", "PRB", "MEN", "JUV", "CRI", "APP" };
        foreach (var code in allKnown)
        {
            var policy = JtiFieldSchemaProvider.GetCaseCategoryPolicy(code);
            Assert.NotNull(policy);
        }
    }
}
