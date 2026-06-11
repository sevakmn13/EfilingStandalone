using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using EFiling.Providers.JTI.Config;
using Xunit;

namespace EFiling.Tests;

/// <summary>
/// Drift-guard tests for the Step #51 per-bucket eFile-reachability audit
/// at Madera (closed 2026-05-27).
///
/// <para>
/// Step #51 closed the Step #49 deferred LPS-folding question with positive
/// evidence (PRB extended via umbrella code 511110), and reframed the 3
/// previously "awaiting evidence" categories (JUV / CRI / APP) as
/// structurally-blocked-at-Madera based on InfoTrack category-search evidence:
/// </para>
///
/// <list type="bullet">
///   <item><b>JUV:</b> W&amp;I Code 300 + W&amp;I Code 602 both return zero
///     results via Madera eFile (likely W&amp;I §827 sealed-record routing).</item>
///   <item><b>CRI:</b> True criminal codes (911180/911190) absent from the
///     Madera eFile category dropdown entirely.</item>
///   <item><b>APP:</b> Only Appeal-flavored eFile category is Labor Commissioner
///     Appeal which is CIV/Employment-coded; true appellate filings go through
///     a separate state-level appellate eFiling system.</item>
///   <item><b>PRB:</b> 511110 added as umbrella code covering LPS Conservatorship
///     + Probate-routed Writ of Habeas Corpus (25+ InfoTrack rows confirming
///     `511110 - [label]` display pattern).</item>
///   <item><b>Codelist-vs-CMS distinction:</b> `scripts/madera_case_category.txt`
///     is the JTI submission codelist (filing-time dropdown), NOT the CMS-stored
///     categoryCode list. knownCategoryCodes must contain CMS-stored codes only.</item>
/// </list>
/// </summary>
public sealed class Step51_EFileReachabilityAuditTests
{
    private const string SchemaResourceName =
        "EFiling.Providers.JTI.Config.JtiCaseCategoryPolicy.json";

    // ─── PRB extension — originally Step #51, CORRECTED at Step #52 ────
    //
    // Step #51 added '511110' based on InfoTrack `Case type:` display.
    // Step #52 discovered that display is the `caseTypeCode` (umbrella),
    // NOT the `caseCategoryCode` (per-subtype CMS storage). Step #52
    // RETRACTED '511110' and replaced with the dedicated per-subtype
    // codes (full Step #52 assertions live in Step52_ProbateFamilyAuditTests).

    [Fact]
    public void Step51_PrbPolicy_RetainsStep49BaselineCodes_And511110RestoredAtStep59()
    {
        // History of 511110 in PRB (three chapters):
        //  • Step #51 ADDED it on BAD evidence — the InfoTrack `Case type:`
        //    display-field, which shows the umbrella caseTypeCode, not a
        //    caseCategoryCode.
        //  • Step #52 correctly RETRACTED it (that display read was not CMS
        //    evidence). The retraction was the right call on the evidence then.
        //  • Step #59 RE-ADDED it on VALID evidence — a GetCase
        //    DETAIL probe on docket MPR11267 returned caseCategoryCode=511110
        //    directly (a generic, un-subtyped "Decedent's Estate" case; the
        //    detail caseTypeCode is also 511110). So 511110 is BOTH the PRB
        //    umbrella caseTypeCode AND a real catch-all caseCategoryCode.
        // This is NOT a reversion to the Step #51 error: the evidence basis is
        // now the authoritative GetCase detail value the resolver actually
        // receives at SF time, not an InfoTrack display artifact.
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue("PRB", out var prb));
        var codes = prb!.KnownCategoryCodes ?? new List<string>();

        Assert.Contains("511210", codes);  // ← Step #49: Probate of Wills & for Ltrs Test.
        Assert.Contains("531110", codes);  // ← Step #49: Conservatorship
        Assert.Contains("511110", codes);  // ← Step #59: restored on direct GetCase detail evidence (MPR11267)
    }

    [Fact]
    public void Step51_PrbPolicy_ResolvedOpenQuestions_RecordsLpsFoldingClosure()
    {
        using var doc = LoadSchemaJson();
        var policies = doc.RootElement.GetProperty("policies");
        var prb = policies.GetProperty("PRB");

        Assert.True(
            prb.TryGetProperty("resolvedOpenQuestions", out var resolved),
            "PRB.resolvedOpenQuestions missing — Step #51 LPS-folding closure must be preserved.");
        Assert.Equal(JsonValueKind.Array, resolved.ValueKind);

        // Step #52 correction: the LPS closure still lives in resolvedOpenQuestions,
        // but `newKnownCategoryCode` was corrected from '511110' (display-field error)
        // to '603110' (the actual caseCategoryCode per Step #52 direct legalhub probe).
        var hasLpsClosure = resolved.EnumerateArray().Any(entry =>
        {
            var resolvedAt = entry.TryGetProperty("resolvedAt", out var ra) ? ra.GetString() ?? string.Empty : string.Empty;
            var newCode = entry.TryGetProperty("newKnownCategoryCode", out var nc) ? nc.GetString() ?? string.Empty : string.Empty;
            return resolvedAt.Contains("Step #51") && newCode == "603110";
        });
        Assert.True(
            hasLpsClosure,
            "PRB.resolvedOpenQuestions missing the LPS-folding closure with Step #52-corrected newKnownCategoryCode=603110 " +
            "(originally Step #51 added '511110' as a display-field artifact; Step #52 corrected to '603110').");
    }

    // ─── Structural-block findings (negative evidence) ─────────────────

    [Theory]
    [InlineData("JUV")]
    [InlineData("CRI")]
    [InlineData("APP")]
    public void Step51_StructurallyBlockedCategories_KeepAwaitingEvidenceTrue(string categoryKey)
    {
        // After Step #51 the semantics of awaitingEvidence shifts for these 3:
        // it now means "awaiting non-Madera-court evidence" rather than
        // "awaiting Madera Tier-B probe". The flag stays true; only its
        // documented meaning changed (per the step51 audit block).
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue(categoryKey, out var policy));
        Assert.True(
            policy!.AwaitingEvidence == true,
            $"{categoryKey}.awaitingEvidence must remain true after Step #51 since Madera evidence " +
            "cannot close these categories (structural block). Flipping to false requires " +
            "evidence from a non-Madera court — see step51EFileReachabilityAudit findings.");
    }

    [Theory]
    [InlineData("JUV")]
    [InlineData("CRI")]
    [InlineData("APP")]
    public void Step51_StructurallyBlockedCategories_HaveEmptyOrNullKnownCategoryCodes(string categoryKey)
    {
        // Structurally-blocked categories should not have any Madera-namespaced
        // codes — adding one would be the kind of speculative promotion the
        // Step #51 audit explicitly warns against.
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue(categoryKey, out var policy));
        var codes = policy!.KnownCategoryCodes;
        Assert.True(
            codes is null || codes.Count == 0,
            $"{categoryKey}.knownCategoryCodes is non-empty (count={codes?.Count ?? 0}). " +
            "Step #51 found this category structurally blocked at Madera eFile; promotion " +
            "requires non-Madera-court evidence + an audit-block update.");
    }

    // ─── Step #52 retraction guard (pins the methodology correction) ────

    [Fact]
    public void Step51_AuditBlock_PRB_Extended_Via_511110_Finding_IsMarkedRetractedAtStep52()
    {
        using var doc = LoadSchemaJson();
        var audit = doc.RootElement.GetProperty("step51EFileReachabilityAudit");
        var findings = audit.GetProperty("findings");
        Assert.True(findings.TryGetProperty("PRB_extended_via_511110_umbrellaCode", out var prbFinding),
            "step51EFileReachabilityAudit.findings.PRB_extended_via_511110_umbrellaCode must remain present " +
            "(as a RETRACTED finding) for historical audit traceability.");

        Assert.True(prbFinding.TryGetProperty("status", out var status),
            "PRB_extended_via_511110_umbrellaCode finding must carry a 'status' field after Step #52 retraction.");
        var statusText = status.GetString() ?? string.Empty;
        Assert.Contains("RETRACTED", statusText);
        Assert.Contains("Step #52", statusText);

        Assert.True(prbFinding.TryGetProperty("step52Correction", out _),
            "PRB_extended_via_511110_umbrellaCode finding must carry a 'step52Correction' field documenting the fix.");
    }

    // ─── Audit block structural assertions ──────────────────────────────

    [Fact]
    public void Step51_AuditBlock_IsPresentInSchemaJson_WithAllExpectedFindings()
    {
        using var doc = LoadSchemaJson();
        Assert.True(
            doc.RootElement.TryGetProperty("step51EFileReachabilityAudit", out var audit),
            "Top-level step51EFileReachabilityAudit block is missing.");

        Assert.True(audit.TryGetProperty("findings", out var findings));
        string[] requiredFindingKeys =
        {
            "JUV_structurallyBlocked_atMaderaEFile",
            "CRI_structurallyBlocked_atMaderaEFile",
            "APP_structurallyBlocked_atMaderaEFile",
            "PRB_extended_via_511110_umbrellaCode",
            "codelistVsCMS_architecturalDistinction",
        };
        foreach (var key in requiredFindingKeys)
        {
            Assert.True(
                findings.TryGetProperty(key, out _),
                $"step51EFileReachabilityAudit.findings missing required key '{key}'.");
        }
    }

    [Fact]
    public void Step51_AuditBlock_Conclusion_FramesMaderaCeilingAndCmsVsSubmissionDistinction()
    {
        using var doc = LoadSchemaJson();
        var audit = doc.RootElement.GetProperty("step51EFileReachabilityAudit");
        Assert.True(audit.TryGetProperty("conclusion", out var conclusion));
        var text = conclusion.GetString() ?? string.Empty;

        // Madera-ceiling framing
        Assert.Contains("5 of 8", text);
        Assert.Contains("STRUCTURALLY BLOCKED", text);
        // Codelist-vs-CMS distinction
        Assert.Contains("CMS-stored", text);
        Assert.Contains("submission", text, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Step51_AuditBlock_EvidenceDockets_CitesUserProvidedMprDockets()
    {
        using var doc = LoadSchemaJson();
        var audit = doc.RootElement.GetProperty("step51EFileReachabilityAudit");
        Assert.True(audit.TryGetProperty("evidenceDockets", out var dockets));

        var lpsEvidence = dockets.GetProperty("WI5350_LPS_Conservatorship").GetString() ?? string.Empty;
        // Pin the 5 user-provided LPS dockets
        Assert.Contains("MPR10622", lpsEvidence);
        Assert.Contains("MPR011875", lpsEvidence);
        Assert.Contains("MPR7458", lpsEvidence);
        Assert.Contains("MPR012098", lpsEvidence);
        Assert.Contains("MPR7413", lpsEvidence);

        var habeasEvidence = dockets.GetProperty("WritOfHabeasCorpus").GetString() ?? string.Empty;
        // Pin the 5 user-provided habeas dockets
        Assert.Contains("MPR015914", habeasEvidence);
        Assert.Contains("MPR016127", habeasEvidence);
        Assert.Contains("MPR011826A", habeasEvidence);
        Assert.Contains("MPR016177", habeasEvidence);
        Assert.Contains("MPR016176", habeasEvidence);
    }

    // ─── Completeness note reframing ────────────────────────────────────

    [Fact]
    public void Step51_CompletenessNote_FramesJuvCriAppAsStructurallyBlockedAtMadera()
    {
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        var note = schema.CompletenessNote ?? string.Empty;
        Assert.Contains("STRUCTURALLY BLOCKED", note);
        Assert.Contains("Step #51", note);
        Assert.Contains("Madera", note);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static JsonDocument LoadSchemaJson()
    {
        var assembly = typeof(JtiFieldSchemaProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(SchemaResourceName)
            ?? throw new FileNotFoundException(
                $"Embedded resource '{SchemaResourceName}' not found in assembly " +
                $"'{assembly.FullName}'.");
        return JsonDocument.Parse(stream);
    }
}
