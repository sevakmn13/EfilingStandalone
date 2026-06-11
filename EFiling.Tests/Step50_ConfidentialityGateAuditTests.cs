using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using EFiling.Providers.JTI.Config;
using Xunit;

namespace EFiling.Tests;

/// <summary>
/// Drift-guard tests for the Step #50 per-category EFSP-side confidentiality-gate
/// audit (closed 2026-05-23).
///
/// <para>
/// Step #50 is the T-7 follow-on that resolved the MEN open question raised at
/// Step #49: "does the JTI EFM doc mandate an MH-specific disclaimer analogous
/// to UD CCP §1161.2 (e.g., W&amp;I §5328 / LPS confidentiality)?"
/// </para>
///
/// <para>
/// Method: full-doc grep across <c>docs/fileing files/</c> for the union of
/// patterns ('Mental Health', 'W&amp;I', '§5328', 'LPS', 'confidential',
/// 'disclaimer', 'access.*limit', 'gate', 'attestation') with per-match
/// contextual inspection. The full methodology + scan scope + per-category
/// findings are documented in <c>JtiCaseCategoryPolicy.json</c> under the
/// <c>step50ConfidentialityGateAudit</c> top-level block.
/// </para>
///
/// <para>
/// Conclusion: UD is the ONLY category with a universal EFSP-side
/// confidentiality gate (CCP §1161.2). Two narrower court-scoped disclaimers
/// exist (LASC Small Claims COVID-19 + Ventura UD-Civil-Limited narrowing) and
/// are handled via the <c>appliesToCourts</c> rule-scoping mechanism. No other
/// category — MEN included — carries a JTI-mandated gate.
/// </para>
///
/// <para>
/// These tests pin the negative finding so it doesn't regress silently:
/// </para>
///
/// <list type="number">
///   <item>UD is the only policy with <c>requiresUdDisclaimer = true</c>.</item>
///   <item>MEN policy no longer carries the §5328/LPS confidentiality open
///     question (moved to <c>resolvedOpenQuestions</c> with negative-finding
///     rationale).</item>
///   <item>The <c>step50ConfidentialityGateAudit</c> block is present in the
///     loaded schema JSON with all expected category findings recorded.</item>
/// </list>
/// </summary>
public sealed class Step50_ConfidentialityGateAuditTests
{
    private const string SchemaResourceName =
        "EFiling.Providers.JTI.Config.JtiCaseCategoryPolicy.json";

    [Fact]
    public void Step50_OnlyUdPolicy_HasRequiresUdDisclaimerTrue()
    {
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        var policiesRequiringDisclaimer = schema.Policies
            .Where(p => p.Value.RequiresUdDisclaimer == true)
            .Select(p => p.Key)
            .OrderBy(k => k)
            .ToList();

        Assert.Single(policiesRequiringDisclaimer);
        Assert.Equal("UD", policiesRequiringDisclaimer[0]);
        // ↑ If a non-UD policy ever sets RequiresUdDisclaimer = true, this
        //   test fails — forcing review against the Step #50 audit findings.
        //   Confidentiality gates beyond UD §1161.2 must be either (a) backed
        //   by a JTI source quote in policy rules + audit block update or
        //   (b) court-scoped via appliesToCourts (LASC, Ventura precedent).
    }

    [Fact]
    public void Step50_MenPolicy_NoLongerHasConfidentialityGateOpenQuestion()
    {
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue("MEN", out var menPolicy));

        var openQuestions = menPolicy!.OpenQuestions ?? new List<string>();

        // Step #50 closure: the SPECIFIC question about whether MH cases
        // need an EFSP-side disclaimer/attestation gate (parallel to
        // UD §1161.2) was moved to `resolvedOpenQuestions` as a negative
        // finding. This test guards against THAT question reappearing in
        // the open list — NOT against any incidental §5328 reference.
        //
        // The original question text was: "Confidentiality / sealed-record
        // gate at MH SF surface? Investigate whether W&I Code §5328 or LPS
        // confidentiality requires an EFSP-side disclaimer parallel to
        // UD §1161.2." We pin two unique substrings from it that don't
        // appear in unrelated entries (e.g., Step #56's PC1368-Competency
        // entry mentions §5328 as one possible cause of CMS-unresolvability
        // but is not about the gate question).
        var confidentialityGateQuestions = openQuestions
            .Where(q =>
                (q.Contains("gate at MH SF surface", System.StringComparison.OrdinalIgnoreCase) ||
                 q.Contains("EFSP-side disclaimer parallel", System.StringComparison.OrdinalIgnoreCase) ||
                 q.Contains("disclaimer parallel to UD", System.StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(confidentialityGateQuestions);
        // ↑ The §5328 / LPS / sealed-record-GATE question was moved to
        //   `resolvedOpenQuestions` at Step #50. If it reappears in
        //   openQuestions, either the resolution was reverted (in which case
        //   re-add it via JTI source) or new evidence emerged (in which case
        //   the step50ConfidentialityGateAudit findings need updating).
        //   Other open questions are allowed to incidentally mention §5328
        //   as a code reference (e.g., as a possible cause of
        //   case-unresolvability) without falsely tripping this guard.
    }

    [Fact]
    public void Step50_MenPolicy_ResolvedOpenQuestions_RecordsTheNegativeFinding()
    {
        // The `resolvedOpenQuestions` array is not strongly typed on the
        // schema model (it's documentation-only metadata), so we parse the
        // raw JSON resource and assert structural presence.
        using var doc = LoadSchemaJson();
        var policies = doc.RootElement.GetProperty("policies");
        var men = policies.GetProperty("MEN");
        Assert.True(
            men.TryGetProperty("resolvedOpenQuestions", out var resolved),
            "MEN policy missing 'resolvedOpenQuestions' — Step #50 closure note must be preserved.");
        Assert.Equal(JsonValueKind.Array, resolved.ValueKind);
        Assert.True(
            resolved.GetArrayLength() >= 1,
            "MEN policy 'resolvedOpenQuestions' is empty — at least the Step #50 §5328 closure entry must be present.");

        // At least one entry must reference Step #50 + the §5328 / LPS topic.
        var hasStep50Closure = resolved.EnumerateArray().Any(entry =>
        {
            var resolvedAt = entry.TryGetProperty("resolvedAt", out var ra) ? ra.GetString() ?? string.Empty : string.Empty;
            var question = entry.TryGetProperty("question", out var q) ? q.GetString() ?? string.Empty : string.Empty;
            return resolvedAt.Contains("Step #50") &&
                   (question.Contains("§5328") || question.Contains("LPS") || question.Contains("Confidentiality"));
        });
        Assert.True(
            hasStep50Closure,
            "MEN.resolvedOpenQuestions missing the Step #50 §5328/LPS confidentiality closure entry.");
    }

    [Fact]
    public void Step50_AuditBlock_IsPresentInSchemaJson_WithAllExpectedCategoryFindings()
    {
        using var doc = LoadSchemaJson();
        Assert.True(
            doc.RootElement.TryGetProperty("step50ConfidentialityGateAudit", out var audit),
            "Top-level step50ConfidentialityGateAudit block is missing — Step #50 audit documentation must be preserved.");

        // The findings must enumerate every category outcome from the doc scan.
        // Each key is the negative-or-positive finding name; absence is a regression.
        Assert.True(audit.TryGetProperty("findings", out var findings));
        string[] requiredFindingKeys =
        {
            "UD_categoryWide_universalGate",
            "LASC_smallClaims_covid19_courtScopedGate",
            "Ventura_UDCivilLimited_courtScopedNarrowing",
            "JUV_serverSideRedaction_notEfspGate",
            "CRI_noRedactionNoGate",
            "MEN_mentalHealth_noGate",
            "PRB_FAM_APP_noGate",
        };
        foreach (var key in requiredFindingKeys)
        {
            Assert.True(
                findings.TryGetProperty(key, out _),
                $"step50ConfidentialityGateAudit.findings missing required key '{key}'. " +
                "If you removed a finding, update the test inventory + add a justification " +
                "to the audit block's `implications` array.");
        }

        // Conclusion must explicitly call out UD as the only universal gate.
        Assert.True(audit.TryGetProperty("conclusion", out var conclusion));
        var conclusionText = conclusion.GetString() ?? string.Empty;
        Assert.Contains("UD is the only category", conclusionText, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("§1161.2", conclusionText);
    }

    [Fact]
    public void Step50_AllNonUdPolicies_DoNotRequireUdDisclaimer()
    {
        // Positive sweep that complements the singleton assertion above:
        // each non-UD policy is explicitly checked to ensure RequiresUdDisclaimer
        // is either null or false. Provides per-policy failure granularity if
        // a regression lands.
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        foreach (var (key, policy) in schema.Policies)
        {
            if (string.Equals(key, "UD", System.StringComparison.OrdinalIgnoreCase)) continue;
            Assert.True(
                policy.RequiresUdDisclaimer != true,
                $"Policy '{key}' has RequiresUdDisclaimer = true. The §1161.2 disclaimer is " +
                "UD-specific per CCP statute. If a non-UD category needs a confidentiality " +
                "gate, add a new policy-level flag (don't piggyback on RequiresUdDisclaimer) " +
                "and update the step50ConfidentialityGateAudit findings.");
        }
    }

    // ───────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────

    private static JsonDocument LoadSchemaJson()
    {
        var assembly = typeof(JtiFieldSchemaProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(SchemaResourceName)
            ?? throw new FileNotFoundException(
                $"Embedded resource '{SchemaResourceName}' not found in assembly " +
                $"'{assembly.FullName}'. The Step #50 audit-block tests load the raw " +
                "policy JSON to assert on documentation-only fields not exposed by the " +
                "strongly-typed CaseCategoryPolicySchemaV2 model.");
        return JsonDocument.Parse(stream);
    }
}
