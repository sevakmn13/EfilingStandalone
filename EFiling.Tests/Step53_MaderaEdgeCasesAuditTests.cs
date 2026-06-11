using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using EFiling.Providers.JTI.Config;
using Xunit;

namespace EFiling.Tests;

/// <summary>
/// Drift-guard tests for Step #53 Madera-edge-cases closure.
///
/// <para>
/// Step #53 closed items #15 (Ancillary disambiguation) + #16 (Protect
/// Proceeding Involving Minor + Lodged Will probes) from the post-#52
/// next-step menu by documenting that all 3 are Madera-scoped exhaustions:
/// </para>
///
/// <list type="bullet">
///   <item><b>Ancillary Proceedings (codelist 561310):</b> Madera InfoTrack
///     returns exactly 1 docket (MPR015007). Step #52 probe returned
///     <c>caseCategoryCode=614130</c>, which is also codelist's Habeas Corpus.
///     Single-docket evidence cannot disambiguate; needs non-Madera-court
///     evidence.</item>
///   <item><b>Protect Proceeding Involving Minor (codelist 551710):</b> 0
///     InfoTrack results at Madera. Re-confirmed at Step #53.
///     Madera-non-applicable.</item>
///   <item><b>Lodged Will (codelist 110120):</b> Same status as Protect
///     Minor — 0 results, re-confirmed at Step #53.</item>
/// </list>
///
/// <para>
/// Step #53 made NO additions or removals to PRB.knownCategoryCodes — the
/// Step #52 20-code set was the Madera evidence floor + ceiling AT THAT TIME.
/// </para>
///
/// <para>
/// <b>Step #59 supersession:</b> the exhaustive direct-CMS
/// list-query + GetCase DETAIL-verification probe (all 85 evidence codes
/// detail-MATCHED their list-level CaseCategoryText, 0 diverge) provided the
/// previously-missing CMS-storage evidence for <c>561310</c> (Ancillary —
/// observed directly on docket MPR015004, detail caseCategoryCode=561310,
/// caseTypeCode=511110 PRB umbrella). 561310 is therefore RESTORED to PRB at
/// Step #59 (alongside 511110 + 532110), lifting PRB from 20 -> 23 codes.
/// The Step #53 finding was INCOMPLETE, not wrong: 614130 (its probed value
/// for MPR015007) remains valid too — both Ancillary codes coexist. The
/// <c>110120</c> (Lodged Will) and <c>551710</c> (Protect Minor) guards below
/// still hold (110120 stays ADMIN-not-PRB; 551710 confirmed 0 at Step #59).
/// </para>
/// </summary>
public sealed class Step53_MaderaEdgeCasesAuditTests
{
    private const string SchemaResourceName =
        "EFiling.Providers.JTI.Config.JtiCaseCategoryPolicy.json";

    // ─── step53MaderaEdgeCases audit-block structural assertions ─────────

    [Fact]
    public void Step53_AuditBlock_IsPresentInSchemaJson_WithRequiredTopLevelKeys()
    {
        using var doc = LoadSchemaJson();
        Assert.True(
            doc.RootElement.TryGetProperty("step53MaderaEdgeCases", out var audit),
            "Top-level step53MaderaEdgeCases block is missing.");

        string[] requiredKeys =
        {
            "title",
            "purpose",
            "method",
            "evidenceTable",
            "findings",
            "filesModified",
            "driftGuardTests",
        };
        foreach (var key in requiredKeys)
        {
            Assert.True(
                audit.TryGetProperty(key, out _),
                $"step53MaderaEdgeCases missing required key '{key}'.");
        }
    }

    [Fact]
    public void Step53_AuditBlock_EvidenceTable_AncillaryProceedings_Pins1ResultAndMPR015007AndCode614130()
    {
        using var doc = LoadSchemaJson();
        var audit = doc.RootElement.GetProperty("step53MaderaEdgeCases");
        var ancillary = audit.GetProperty("evidenceTable").GetProperty("AncillaryProceedings");

        Assert.Equal(1, ancillary.GetProperty("infoTrackResultsAtMadera").GetInt32());
        Assert.Equal("MPR015007", ancillary.GetProperty("onlyDocket").GetString());
        Assert.Equal("614130", ancillary.GetProperty("probedCaseCategoryCode").GetString());
    }

    [Fact]
    public void Step53_AuditBlock_EvidenceTable_ProtectMinor_Pins0ResultsAndCodelist551710()
    {
        using var doc = LoadSchemaJson();
        var audit = doc.RootElement.GetProperty("step53MaderaEdgeCases");
        var protectMinor = audit.GetProperty("evidenceTable").GetProperty("ProtectProceedingInvolvingMinor");

        Assert.Equal(0, protectMinor.GetProperty("infoTrackResultsAtMadera").GetInt32());
        Assert.Equal("551710", protectMinor.GetProperty("codelistSubmissionCode").GetString());
    }

    [Fact]
    public void Step53_AuditBlock_EvidenceTable_LodgedWill_Pins0ResultsAndCodelist110120()
    {
        using var doc = LoadSchemaJson();
        var audit = doc.RootElement.GetProperty("step53MaderaEdgeCases");
        var lodgedWill = audit.GetProperty("evidenceTable").GetProperty("LodgedWill");

        Assert.Equal(0, lodgedWill.GetProperty("infoTrackResultsAtMadera").GetInt32());
        Assert.Equal("110120", lodgedWill.GetProperty("codelistSubmissionCode").GetString());
    }

    [Fact]
    public void Step53_AuditBlock_Findings_ContainsAll5RequiredKeys()
    {
        using var doc = LoadSchemaJson();
        var audit = doc.RootElement.GetProperty("step53MaderaEdgeCases");
        Assert.True(audit.TryGetProperty("findings", out var findings));

        string[] requiredFindings =
        {
            "step53_F1_AncillaryScarcity_MaderaCannotDisambiguate",
            "step53_F2_ProtectMinorZeroEvidence",
            "step53_F3_LodgedWillZeroEvidence",
            "step53_F4_PRBCodesUnchanged",
            "step53_F5_PostStep52MenuAdvancement",
        };
        foreach (var key in requiredFindings)
        {
            Assert.True(
                findings.TryGetProperty(key, out _),
                $"step53MaderaEdgeCases.findings missing required finding '{key}'.");
        }
    }

    // ─── PRB.knownCategoryCodes ceiling preservation (F4 enforcement) ────

    [Fact]
    public void Step53_PrbPolicy_KnownCategoryCodes_Is23_AfterStep59Restoration()
    {
        // Step #53 itself made NO additions (the Step #52 20-code set held).
        // Step #59 restored 3 codes with DETAIL-level CMS evidence
        // (511110 generic Decedent's Estate, 532110 LPS, 561310 Ancillary — all
        // observed via GetCase with detail caseTypeCode=511110 PRB umbrella),
        // taking PRB to 23. The exact 23-code set is pinned in
        // Step59_UnmappedCategoryPromotionAuditTests; this guards the count.
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue("PRB", out var prb));
        var codes = prb!.KnownCategoryCodes ?? new List<string>();
        Assert.Equal(23, codes.Count);
    }

    [Fact]
    public void Step53_PrbPolicy_KnownCategoryCodes_DoesNotContain_551710_ProtectMinor_NoMaderaEvidence()
    {
        // F2 enforcement: Madera has 0 InfoTrack results for Protect Minor.
        // The submission codelist code 551710 must NOT appear in
        // PRB.knownCategoryCodes without CMS-side evidence.
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue("PRB", out var prb));
        var codes = prb!.KnownCategoryCodes ?? new List<string>();
        Assert.DoesNotContain("551710", codes);
    }

    [Fact]
    public void Step53_PrbPolicy_KnownCategoryCodes_DoesNotContain_110120_LodgedWill_NoMaderaEvidence()
    {
        // Step #59 correction: the Step #53 "0 InfoTrack results"
        // basis was a FALSE NEGATIVE — the direct-CMS probe found 327 Lodged Will
        // cases (first docket MLD003510). HOWEVER 110120 is SELF-TYPED
        // (GetCase detail caseTypeCode=110120, an administrative code), NOT a
        // PRB-umbrella (511110) case — so it correctly stays OUT of PRB. It is
        // documented as a searchable-but-unmapped ADMIN code in the step59 audit
        // block. The assertion (110120 not in PRB) is unchanged; only its
        // rationale is corrected from "no cases" to "administrative self-type".
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue("PRB", out var prb));
        var codes = prb!.KnownCategoryCodes ?? new List<string>();
        Assert.DoesNotContain("110120", codes);
    }

    [Fact]
    public void Step53_PrbPolicy_KnownCategoryCodes_Contains_561310_RestoredAtStep59_DetailVerified()
    {
        // SUPERSEDES the Step #53 F1 "never probed as CMS storage" finding.
        // Step #53 saw only MPR015007 -> 614130 and concluded 561310 had never
        // been observed as CMS-stored. Step #59 probed a DIFFERENT
        // Ancillary docket, MPR015004, via GetCase and observed detail
        // caseCategoryCode=561310 directly (caseTypeCode=511110 PRB umbrella).
        // So 561310 IS a real CMS-stored caseCategoryCode -> restored to PRB.
        // 614130 also stays (see _StillContains_614130 below): both coexist.
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue("PRB", out var prb));
        var codes = prb!.KnownCategoryCodes ?? new List<string>();
        Assert.Contains("561310", codes);
    }

    [Fact]
    public void Step53_PrbPolicy_KnownCategoryCodes_StillContains_614130_AncillaryAmbiguousProbed()
    {
        // F1 corollary: 614130 IS the CMS-probed value for MPR015007 and
        // therefore STAYS in PRB.knownCategoryCodes per Step #52 evidence.
        // The ambiguity (same code = codelist's Habeas) is tracked in
        // step52ProbateFamilyAudit.findings.ambiguity_614130_Ancillary_or_Habeas,
        // not by removing the code.
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue("PRB", out var prb));
        var codes = prb!.KnownCategoryCodes ?? new List<string>();
        Assert.Contains("614130", codes);
    }

    // ─── PRB.openQuestions Step #53 reframing assertions ────────────────

    [Fact]
    public void Step53_PrbPolicy_OpenQuestions_ProtectMinorEntry_ReframedWithStep53Confirmation()
    {
        using var doc = LoadSchemaJson();
        var prb = doc.RootElement.GetProperty("policies").GetProperty("PRB");
        Assert.True(prb.TryGetProperty("openQuestions", out var open));

        var hasReframedProtectMinor = open.EnumerateArray().Any(e =>
        {
            var text = e.GetString() ?? string.Empty;
            return text.Contains("Protect Proceeding Involving Minor")
                && text.Contains("551710")
                && text.Contains("Step #53")
                && text.Contains("step53MaderaEdgeCases");
        });
        Assert.True(
            hasReframedProtectMinor,
            "PRB.openQuestions missing the Step #53-reframed Protect Minor entry " +
            "(must mention codelist 551710, Step #53, and step53MaderaEdgeCases block).");
    }

    [Fact]
    public void Step53_PrbPolicy_OpenQuestions_LodgedWillEntry_ReframedWithStep53Confirmation()
    {
        using var doc = LoadSchemaJson();
        var prb = doc.RootElement.GetProperty("policies").GetProperty("PRB");
        Assert.True(prb.TryGetProperty("openQuestions", out var open));

        var hasReframedLodgedWill = open.EnumerateArray().Any(e =>
        {
            var text = e.GetString() ?? string.Empty;
            return text.Contains("Lodged Will")
                && text.Contains("110120")
                && text.Contains("Step #53")
                && text.Contains("step53MaderaEdgeCases");
        });
        Assert.True(
            hasReframedLodgedWill,
            "PRB.openQuestions missing the Step #53-reframed Lodged Will entry " +
            "(must mention codelist 110120, Step #53, and step53MaderaEdgeCases block).");
    }

    [Fact]
    public void Step53_PrbPolicy_OpenQuestions_AncillaryEntry_ReframedWithStep53OnlyDocketConfirmation()
    {
        using var doc = LoadSchemaJson();
        var prb = doc.RootElement.GetProperty("policies").GetProperty("PRB");
        Assert.True(prb.TryGetProperty("openQuestions", out var open));

        var hasReframedAncillary = open.EnumerateArray().Any(e =>
        {
            var text = e.GetString() ?? string.Empty;
            return text.Contains("Ancillary Proceedings")
                && text.Contains("MPR015007")
                && text.Contains("ONLY")
                && text.Contains("Step #53")
                && text.Contains("step53MaderaEdgeCases");
        });
        Assert.True(
            hasReframedAncillary,
            "PRB.openQuestions missing the Step #53-reframed Ancillary entry " +
            "(must mention MPR015007 as the ONLY docket + Step #53 + step53MaderaEdgeCases block).");
    }

    // ─── completenessNote Step #53 reference ────────────────────────────

    [Fact]
    public void Step53_CompletenessNote_ReferencesStep53AndEdgeCasesBlock()
    {
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        var note = schema.CompletenessNote ?? string.Empty;

        Assert.Contains("Step #53", note);
        Assert.Contains("step53MaderaEdgeCases", note);
        // Step #52 ceiling must still be acknowledged in the closure language.
        Assert.Contains("Step #52", note);
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
