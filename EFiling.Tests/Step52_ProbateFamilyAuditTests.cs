using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using EFiling.Providers.JTI.Config;
using Xunit;

namespace EFiling.Tests;

/// <summary>
/// Drift-guard tests for the Step #52 Probate-family caseCategoryCode
/// discovery + Step #51 methodology correction (closed 2026-05-27).
///
/// <para>
/// Step #52 closed the Step #51 deferred "do remaining subtypes collapse or
/// have dedicated codes" question with positive evidence (17 dedicated codes
/// discovered via direct legalhub probe extracting <c>var caseCategoryCode</c>
/// from rendered SF HTML), and CORRECTED Step #51's methodology error
/// (conflating InfoTrack <c>Case type:</c> display field — which shows
/// <c>caseTypeCode</c> umbrella — with <c>caseCategoryCode</c>).
/// </para>
///
/// <list type="bullet">
///   <item><b>17 dedicated caseCategoryCodes discovered</b> for Probate-family
///     subtypes (Trust → 521110, Guardianship → 541110, Spousal Property →
///     551110, Minor's Claim → 551310, etc.).</item>
///   <item><b>LPS Conservatorship correction:</b> Step #51 incorrectly added
///     511110; Step #52 direct probe on 3 LPS dockets returned 603110
///     (MEN-range cross-classified).</item>
///   <item><b>Habeas Corpus correction:</b> Step #51's claim that Habeas
///     collapses to 511110 was display-only and is UNSUBSTANTIATED — Step #52
///     SF probes on 3 Habeas dockets all redirected to SearchCase (cases not
///     retrievable via live JTI EFM).</item>
///   <item><b>New architectural distinction:</b> <c>caseTypeCode</c> (umbrella)
///     vs <c>caseCategoryCode</c> (per-subtype CMS storage). Our resolver
///     <c>FindPolicyByCourtCategoryCode</c> matches on the latter only.</item>
/// </list>
/// </summary>
public sealed class Step52_ProbateFamilyAuditTests
{
    private const string SchemaResourceName =
        "EFiling.Providers.JTI.Config.JtiCaseCategoryPolicy.json";

    /// <summary>
    /// The full 20-code Step #52 expected set for PRB.knownCategoryCodes.
    /// Step #49 contributed 2 (511210, 531110). Step #52 contributed 18:
    /// 17 dedicated per-subtype caseCategoryCodes from direct legalhub
    /// probes + 1 LPS Conservatorship code (603110) from the re-verification
    /// probe that corrected Step #51's 511110 error.
    /// </summary>
    private static readonly string[] Step52ExpectedPrbCodes =
    {
        // ── Step #49 baseline (2) ──
        "511210", // Probate of Wills & for Ltrs Test. (MPR11249, MPR011994)
        "531110", // Conservatorship (MPR11261, MPR10298)

        // ── Step #52 dedicated codes (17) ──
        "511310", // Probate of Wills & Ltrs of Admin (MPR011989)
        "511410", // Letters of Administration (MPR011983)
        "511510", // Letters of Special Administration (MPR011521)
        "511610", // Authorization to do Administrative (MPR012152)
        "521110", // Trust (MPR012026)
        "541110", // Guardianship (MPR011984)
        "551110", // Spousal Property (MPR011981)
        "551210", // Establish Fact of Birth (MPR012364)
        "551211", // Establish Fact of Death (MPR012205)
        "551212", // Establish Fact of Marriage (MPR012419)
        "551310", // Minor's Claim (MPR012027)
        "551410", // Determine Succession Real Property (MPR011987)
        "551510", // Mgmt/Dispo Prop-Spouse No Capacity (MPR011677)
        "551610", // Auth Med Trtmnt Adult w/o Conserv (MPR012321)
        "561110", // Affidavit Property Small Value (MPR012003)
        "561210", // Summary Petition (MPR011300)
        "614130", // Ancillary Proceedings — ambiguous, see audit block (MPR015007)

        // ── Step #52 LPS correction (1) ──
        "603110"  // LPS Conservatorship — MEN-range cross-classified (MPR10622, MPR011875, MPR7458)
    };

    // ─── 20-code PRB.knownCategoryCodes pin ──────────────────────────────

    [Fact]
    public void Step52_PrbPolicy_KnownCategoryCodes_ContainsAll20Step52Codes_AfterStep59Expansion()
    {
        // All 20 Step #52-era PRB codes must survive (non-regression). Step #59
        // added 3 detail-verified codes (511110, 532110, 561310 —
        // each observed via GetCase with detail caseTypeCode=511110 PRB umbrella)
        // -> 23 total. The exact 23-code set is pinned in
        // Step59_UnmappedCategoryPromotionAuditTests.
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue("PRB", out var prb));
        var codes = prb!.KnownCategoryCodes ?? new List<string>();

        foreach (var expected in Step52ExpectedPrbCodes)
        {
            Assert.Contains(expected, codes);
        }
        Assert.Equal(23, codes.Count);
    }

    [Fact]
    public void Step52_PrbPolicy_KnownCategoryCodes_Contains_511110_RestoredAtStep59()
    {
        // SUPERSEDES the Step #52 retraction of 511110. Step #52 was correct that
        // 511110 is the umbrella caseTypeCode AND that the Step #51 InfoTrack
        // `Case type:` display-read was invalid evidence. But Step #52 only ever
        // probed probate SUBTYPE dockets (Trust/Guardianship/etc.) — it never
        // probed a GENERIC, un-subtyped estate case. Step #59 did:
        // GetCase on MPR11267 returned caseCategoryCode=511110 directly. So
        // 511110 is BOTH the umbrella caseTypeCode AND a valid catch-all
        // caseCategoryCode for generic Decedent's Estate cases -> restored to PRB
        // on authoritative GetCase detail evidence (not the Step #51 display read).
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue("PRB", out var prb));
        var codes = prb!.KnownCategoryCodes ?? new List<string>();
        Assert.Contains("511110", codes);
    }

    [Fact]
    public void Step52_PrbPolicy_KnownCategoryCodes_Contains_603110_LpsCorrection()
    {
        // Step #52 LPS correction. Direct legalhub probe on MPR10622 + MPR011875
        // + MPR7458 returned caseCategoryCode=603110 (NOT 511110, NOT codelist's
        // 532110). Cross-classified: MPR-prefixed (Probate-administered),
        // caseTypeCode=511110 (PRB umbrella), but caseCategoryCode in MEN range.
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue("PRB", out var prb));
        var codes = prb!.KnownCategoryCodes ?? new List<string>();
        Assert.Contains("603110", codes);
    }

    // ─── PRB.resolvedOpenQuestions assertions ───────────────────────────

    [Fact]
    public void Step52_PrbPolicy_ResolvedOpenQuestions_LpsClosure_HasStep52CorrectionMarker()
    {
        using var doc = LoadSchemaJson();
        var prb = doc.RootElement.GetProperty("policies").GetProperty("PRB");
        Assert.True(prb.TryGetProperty("resolvedOpenQuestions", out var resolved));

        var hasStep52CorrectedLpsClosure = resolved.EnumerateArray().Any(entry =>
        {
            var resolvedAt = entry.TryGetProperty("resolvedAt", out var ra) ? ra.GetString() ?? string.Empty : string.Empty;
            var newCode = entry.TryGetProperty("newKnownCategoryCode", out var nc) ? nc.GetString() ?? string.Empty : string.Empty;
            var hasCorrection = entry.TryGetProperty("step52Correction", out _);
            return resolvedAt.Contains("Step #52") && newCode == "603110" && hasCorrection;
        });
        Assert.True(
            hasStep52CorrectedLpsClosure,
            "PRB.resolvedOpenQuestions missing the Step #52-corrected LPS closure entry " +
            "(must have resolvedAt containing 'Step #52', newKnownCategoryCode='603110', " +
            "and a step52Correction field).");
    }

    [Fact]
    public void Step52_PrbPolicy_ResolvedOpenQuestions_RecordsDedicatedCodesClosure()
    {
        // Step #52 closed the "do subtypes collapse or have dedicated codes"
        // question with the DEDICATED-CODES answer (17 dedicated codes observed,
        // no umbrella collapse). This must be recorded in resolvedOpenQuestions.
        using var doc = LoadSchemaJson();
        var prb = doc.RootElement.GetProperty("policies").GetProperty("PRB");
        Assert.True(prb.TryGetProperty("resolvedOpenQuestions", out var resolved));

        var hasDedicatedCodesClosure = resolved.EnumerateArray().Any(entry =>
        {
            var question = entry.TryGetProperty("question", out var q) ? q.GetString() ?? string.Empty : string.Empty;
            var resolvedAt = entry.TryGetProperty("resolvedAt", out var ra) ? ra.GetString() ?? string.Empty : string.Empty;
            var hasCodesList = entry.TryGetProperty("newKnownCategoryCodes", out var codes)
                               && codes.ValueKind == JsonValueKind.Array
                               && codes.GetArrayLength() >= 18;
            return resolvedAt.Contains("Step #52")
                && question.Contains("collapse", System.StringComparison.OrdinalIgnoreCase)
                && hasCodesList;
        });
        Assert.True(
            hasDedicatedCodesClosure,
            "PRB.resolvedOpenQuestions missing the Step #52 dedicated-codes closure entry " +
            "(must reference the 'collapse vs dedicated' question, be resolvedAt 'Step #52', " +
            "and carry a newKnownCategoryCodes array of >=18 codes).");
    }

    // ─── step52ProbateFamilyAudit audit-block structural assertions ─────

    [Fact]
    public void Step52_AuditBlock_IsPresentInSchemaJson_WithAllExpectedFindings()
    {
        using var doc = LoadSchemaJson();
        Assert.True(
            doc.RootElement.TryGetProperty("step52ProbateFamilyAudit", out var audit),
            "Top-level step52ProbateFamilyAudit block is missing.");

        Assert.True(audit.TryGetProperty("findings", out var findings));
        string[] requiredFindingKeys =
        {
            "methodologyErrorCorrection",
            "caseTypeCodeVsCaseCategoryCode",
            "crossClassification_LPS_603110",
            "ambiguity_614130_Ancillary_or_Habeas",
        };
        foreach (var key in requiredFindingKeys)
        {
            Assert.True(
                findings.TryGetProperty(key, out _),
                $"step52ProbateFamilyAudit.findings missing required key '{key}'.");
        }
    }

    [Fact]
    public void Step52_AuditBlock_EvidenceTable_DocumentsAllProbeCohorts()
    {
        using var doc = LoadSchemaJson();
        var audit = doc.RootElement.GetProperty("step52ProbateFamilyAudit");
        Assert.True(audit.TryGetProperty("evidenceTable", out var table));

        string[] requiredCohortKeys =
        {
            "Step52_dedicatedCodesDiscovered",
            "Step51_reVerification",
            "Step49_baselineSanity",
        };
        foreach (var key in requiredCohortKeys)
        {
            Assert.True(
                table.TryGetProperty(key, out _),
                $"step52ProbateFamilyAudit.evidenceTable missing required cohort '{key}'.");
        }
    }

    [Fact]
    public void Step52_AuditBlock_EvidenceTable_PinsAllProbedDocketsAndCodes()
    {
        using var doc = LoadSchemaJson();
        var audit = doc.RootElement.GetProperty("step52ProbateFamilyAudit");
        var dedicated = audit.GetProperty("evidenceTable").GetProperty("Step52_dedicatedCodesDiscovered");

        // 17 dedicated-code entries — pin docket + caseCategoryCode for each.
        var pins = new[]
        {
            ("Trust", "MPR012026", "521110"),
            ("Guardianship", "MPR011984", "541110"),
            ("SpousalProperty", "MPR011981", "551110"),
            ("MinorsClaim", "MPR012027", "551310"),
            ("EstablishFactOfBirth", "MPR012364", "551210"),
            ("LettersOfAdministration", "MPR011983", "511410"),
            ("LettersOfSpecialAdministration", "MPR011521", "511510"),
            ("EstablishFactOfDeath", "MPR012205", "551211"),
            ("EstablishFactOfMarriage", "MPR012419", "551212"),
            ("DetermineSuccessionRealProperty", "MPR011987", "551410"),
            ("MgmtDispoPropSpouseNoCapacity", "MPR011677", "551510"),
            ("AuthMedTrtmntAdult", "MPR012321", "551610"),
            ("AffidavitPropertySmallValue", "MPR012003", "561110"),
            ("SummaryPetition", "MPR011300", "561210"),
            ("AncillaryProceedings", "MPR015007", "614130"),
            ("ProbateOfWillsAndLtrsOfAdmin", "MPR011989", "511310"),
            ("AuthorizationToDoAdministrative", "MPR012152", "511610"),
        };

        foreach (var (key, expectedDocket, expectedCode) in pins)
        {
            Assert.True(dedicated.TryGetProperty(key, out var entry),
                $"Step52_dedicatedCodesDiscovered missing key '{key}'.");
            Assert.Equal(expectedDocket, entry.GetProperty("docket").GetString());
            Assert.Equal(expectedCode, entry.GetProperty("caseCategoryCode").GetString());
            // Every PRB-bucket case must share the umbrella caseTypeCode 511110.
            Assert.Equal("511110", entry.GetProperty("caseTypeCode").GetString());
        }
    }

    [Fact]
    public void Step52_AuditBlock_Step51ReVerification_PinsLpsAndHabeasFindings()
    {
        using var doc = LoadSchemaJson();
        var audit = doc.RootElement.GetProperty("step52ProbateFamilyAudit");
        var reVerify = audit.GetProperty("evidenceTable").GetProperty("Step51_reVerification");

        // LPS Conservatorship: probed value MUST be 603110 (the Step #52 correction).
        var lps = reVerify.GetProperty("LPS_Conservatorship_dockets");
        Assert.Equal("603110", lps.GetProperty("caseCategoryCode").GetString());
        Assert.Contains("MPR10622", lps.GetProperty("dockets").EnumerateArray().Select(e => e.GetString()));

        // Habeas Corpus: probed value MUST be UNKNOWN (Step #52 SF-redirect finding).
        var habeas = reVerify.GetProperty("Habeas_dockets");
        Assert.Equal("UNKNOWN", habeas.GetProperty("caseCategoryCode").GetString());
        Assert.Contains("redirect", habeas.GetProperty("verdict").GetString() ?? string.Empty);
    }

    [Fact]
    public void Step52_AuditBlock_Conclusion_FramesPRBExpansionAnd511110Retraction()
    {
        using var doc = LoadSchemaJson();
        var audit = doc.RootElement.GetProperty("step52ProbateFamilyAudit");
        Assert.True(audit.TryGetProperty("conclusion", out var conclusion));
        var text = conclusion.GetString() ?? string.Empty;

        Assert.Contains("20 codes", text);
        Assert.Contains("methodology error", text);
        Assert.Contains("caseTypeCode", text);
        Assert.Contains("caseCategoryCode", text);
    }

    // ─── completenessNote Step #52 reframing ────────────────────────────

    [Fact]
    public void Step52_CompletenessNote_DocumentsPrbExpansionAnd511110Retraction()
    {
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        var note = schema.CompletenessNote ?? string.Empty;

        Assert.Contains("Step #52", note);
        Assert.Contains("20 evidence-backed", note);
        Assert.Contains("RETRACTED", note);
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
