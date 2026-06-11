using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using EFiling.Providers.JTI.Config;
using Xunit;

namespace EFiling.Tests;

/// <summary>
/// Drift-guard tests for Step #57 exhaustive Madera category-search probe + UI
/// surface promotion + 4 new evidence-backed codes.
///
/// <para>
/// Step #57 closed the post-#54 menu item 25b (CIV + FAM evidence-expansion)
/// AND superseded specific Step #56 findings (606110 needs-re-probe, 612110
/// Madera-non-applicable) by introducing direct CMS list-query as the
/// canonical evidence methodology — replacing InfoTrack-mediated probing.
/// </para>
///
/// <para><b>What changed at Step #57:</b></para>
/// <list type="bullet">
///   <item><b>Backend exposed via UI:</b> <c>CaseSearchModel.CaseCategoryCode</c>
///     field + <c>CourtFilingController.SearchCasesAsync</c> 'category' switch
///     case + <c>SearchCasesAjax</c> caseCategoryCode query param + new
///     category dropdown in <c>SearchCase.cshtml</c>. The SOAP backend
///     (<c>BuildGetCaseListRequest</c> with <c>&lt;ns1:CaseCategoryText&gt;</c>)
///     was already wired but no UI/controller layer exposed it.</item>
///   <item><b>4 new evidence-backed codes:</b> 411100 → CIV (8 cases,
///     MCV039455), 213110 → FAM (25 cases, MFL005686), 606110 → MEN (985
///     cases, MMH00133 — supersedes Step #56 needs-re-probe), 612110 → MEN
///     (1 case, MMH01159 — supersedes Step #56 Madera-non-applicable).</item>
///   <item><b>Methodology shift:</b> InfoTrack-mediated probing (Steps #52,
///     #56) is no longer authoritative. The 612110 InfoTrack-false-negative
///     proves it. Direct CMS list-query is now canonical.</item>
///   <item><b>Madera mapping count:</b> 35 → 39 in
///     <c>JtiCourtCategoryMappings.json</c>.</item>
///   <item><b>MEN policy count:</b> knownCategoryCodes 5 → 7;
///     openQuestions 8 → 6; resolvedOpenQuestions 1 → 3.</item>
/// </list>
///
/// <para>
/// These tests pin the new state against future drift. Some Step #56 tests
/// were relaxed/replaced when their assertions became outdated by Step #57's
/// promotions — see <c>Step56_MenEvidenceAuditTests</c> for the supersession
/// notes.
/// </para>
/// </summary>
public sealed class Step57_AllMaderaCategoriesAuditTests
{
    private const string SchemaResourceName =
        "EFiling.Providers.JTI.Config.JtiCaseCategoryPolicy.json";

    private const string MappingsResourceName =
        "EFiling.Providers.JTI.Config.JtiCourtCategoryMappings.json";

    // ─── step57AllMaderaCategoriesAudit audit-block structural assertions ─

    [Fact]
    public void Step57_AuditBlock_IsPresentInSchemaJson_WithRequiredTopLevelKeys()
    {
        using var doc = LoadSchemaJson();
        Assert.True(
            doc.RootElement.TryGetProperty("step57AllMaderaCategoriesAudit", out var audit),
            "Top-level step57AllMaderaCategoriesAudit block is missing.");

        string[] requiredKeys =
        {
            "title",
            "purpose",
            "method",
            "probeResultsTable",
            "findings",
            "filesModified",
            "driftGuardTests",
        };
        foreach (var key in requiredKeys)
        {
            Assert.True(
                audit.TryGetProperty(key, out _),
                $"step57AllMaderaCategoriesAudit missing required key '{key}'.");
        }
    }

    [Fact]
    public void Step57_AuditBlock_ProbeResultsTable_HasFiveRequiredSubKeys()
    {
        using var doc = LoadSchemaJson();
        var table = doc.RootElement
            .GetProperty("step57AllMaderaCategoriesAudit")
            .GetProperty("probeResultsTable");

        string[] subKeys =
        {
            "mapped_evidence_backed",
            "newly_promoted_at_step57",
            "confirmed_madera_non_applicable",
            "civ_hypothesized_unmapped_returning_zero",
            "fam_hypothesized_unmapped_returning_zero",
        };
        foreach (var key in subKeys)
        {
            Assert.True(
                table.TryGetProperty(key, out _),
                $"step57AllMaderaCategoriesAudit.probeResultsTable missing sub-key '{key}'.");
        }
    }

    [Fact]
    public void Step57_AuditBlock_MappedEvidenceBacked_HasExpectedCount()
    {
        // 38 Madera mappings present in JtiCourtCategoryMappings.json BEFORE
        // Step #57 expansion (3 CIV + 5 UD + 5 FAM + 20 PRB + 5 MEN — the
        // post-Step-#56 baseline) — every one of them has its evidence
        // captured in the audit block. (407110 stays in the table with
        // count=0 as 'mapped-but-no-Madera-evidence' per CCP statute
        // coverage rationale.)
        using var doc = LoadSchemaJson();
        var arr = doc.RootElement
            .GetProperty("step57AllMaderaCategoriesAudit")
            .GetProperty("probeResultsTable")
            .GetProperty("mapped_evidence_backed");

        Assert.Equal(JsonValueKind.Array, arr.ValueKind);
        Assert.Equal(38, arr.GetArrayLength());
    }

    [Theory]
    [InlineData("411100", "CIV", 8, "MCV039455", "411110")]
    [InlineData("213110", "FAM", 25, "MFL005686", "211110")]
    [InlineData("606110", "MEN", 985, "MMH00133", "611110")]
    [InlineData("612110", "MEN", 1, "MMH01159", "611110")]
    public void Step57_AuditBlock_NewlyPromoted_PinsCodeJcccCountSampleAndCaseTypeCode(
        string code, string expectedJccc, int expectedCount, string expectedSampleDocket, string expectedCaseTypeCode)
    {
        using var doc = LoadSchemaJson();
        var arr = doc.RootElement
            .GetProperty("step57AllMaderaCategoriesAudit")
            .GetProperty("probeResultsTable")
            .GetProperty("newly_promoted_at_step57");

        var entry = arr.EnumerateArray().FirstOrDefault(e =>
            e.TryGetProperty("code", out var c) && c.GetString() == code);
        Assert.False(
            entry.ValueKind == JsonValueKind.Undefined,
            $"newly_promoted_at_step57 missing entry for code '{code}'.");

        Assert.Equal(expectedJccc, entry.GetProperty("jccc").GetString());
        Assert.Equal(expectedCount, entry.GetProperty("count").GetInt32());
        Assert.Equal(expectedSampleDocket, entry.GetProperty("sampleDocket").GetString());
        Assert.Equal(expectedCaseTypeCode, entry.GetProperty("sampleCaseTypeCode").GetString());
    }

    [Fact]
    public void Step57_AuditBlock_NewlyPromoted_HasExactlyFourEntries()
    {
        using var doc = LoadSchemaJson();
        var arr = doc.RootElement
            .GetProperty("step57AllMaderaCategoriesAudit")
            .GetProperty("probeResultsTable")
            .GetProperty("newly_promoted_at_step57");

        Assert.Equal(JsonValueKind.Array, arr.ValueKind);
        Assert.Equal(4, arr.GetArrayLength());
    }

    [Theory]
    [InlineData("601110")]
    [InlineData("602110")]
    [InlineData("607110")]
    [InlineData("608110")]
    [InlineData("611110")] // MEN umbrella case-type, not a valid caseCategoryCode
    public void Step57_AuditBlock_ConfirmedMaderaNonApplicable_ContainsCodeWithZeroCounts(string code)
    {
        using var doc = LoadSchemaJson();
        var arr = doc.RootElement
            .GetProperty("step57AllMaderaCategoriesAudit")
            .GetProperty("probeResultsTable")
            .GetProperty("confirmed_madera_non_applicable");

        var entry = arr.EnumerateArray().FirstOrDefault(e =>
            e.TryGetProperty("code", out var c) && c.GetString() == code);
        Assert.False(
            entry.ValueKind == JsonValueKind.Undefined,
            $"confirmed_madera_non_applicable missing entry for code '{code}'.");

        // directCmsCount must be 0 — that's what makes it "confirmed".
        Assert.Equal(0, entry.GetProperty("directCmsCount").GetInt32());
        Assert.True(entry.GetProperty("doubleConfirmedAtStep57").GetBoolean());
    }

    [Fact]
    public void Step57_AuditBlock_ConfirmedMaderaNonApplicable_HasExactlyFiveEntries()
    {
        using var doc = LoadSchemaJson();
        var arr = doc.RootElement
            .GetProperty("step57AllMaderaCategoriesAudit")
            .GetProperty("probeResultsTable")
            .GetProperty("confirmed_madera_non_applicable");

        Assert.Equal(JsonValueKind.Array, arr.ValueKind);
        Assert.Equal(5, arr.GetArrayLength());
    }

    [Fact]
    public void Step57_AuditBlock_Findings_ContainsAll5RequiredKeys()
    {
        using var doc = LoadSchemaJson();
        var findings = doc.RootElement
            .GetProperty("step57AllMaderaCategoriesAudit")
            .GetProperty("findings");

        string[] requiredFindings =
        {
            "step57_F1_BackendAlreadySupportedCategorySearch",
            "step57_F2_DirectCmsListQueryIsAuthoritative",
            "step57_F3_McH01221_PerDocketUnreachabilityConfirmed",
            "step57_F4_NumericCodesRequiredNotLabels",
            "step57_F5_FortyOneOfFiftyNineCodesEvidenceBacked",
        };
        foreach (var key in requiredFindings)
        {
            Assert.True(
                findings.TryGetProperty(key, out _),
                $"step57AllMaderaCategoriesAudit.findings missing required finding '{key}'.");
        }
    }

    [Fact]
    public void Step57_AuditBlock_Finding_F2_ExplicitlyDocumentsInfoTrackFalseNegativeFor612110()
    {
        // F2 is the methodology-shift finding — it must explicitly call out
        // 612110 as the InfoTrack false-negative example. This pin prevents
        // a future doc-cleanup pass from generic-izing the wording and
        // losing the concrete evidence.
        using var doc = LoadSchemaJson();
        var f2 = doc.RootElement
            .GetProperty("step57AllMaderaCategoriesAudit")
            .GetProperty("findings")
            .GetProperty("step57_F2_DirectCmsListQueryIsAuthoritative");

        var summary = f2.GetProperty("summary").GetString() ?? string.Empty;
        Assert.Contains("612110", summary);
        Assert.Contains("MMH01159", summary);
        Assert.Contains("InfoTrack", summary);

        var supersedes = f2.GetProperty("supersedes").GetString() ?? string.Empty;
        Assert.Contains("612110", supersedes);
        Assert.Contains("606110", supersedes);
    }

    [Fact]
    public void Step57_AuditBlock_Finding_F3_ConfirmsThreeIndependentVerificationsForMmh01221()
    {
        // F3 is the per-docket-vs-per-code separation finding — the audit
        // must capture that 3 independent CMS-access methods all fail to
        // retrieve MMH01221, while 985 OTHER cases under the same code
        // ARE retrievable. This is what justifies the per-docket finding
        // staying in step56MenEvidenceAudit + the per-code promotion to
        // MEN.knownCategoryCodes.
        using var doc = LoadSchemaJson();
        var f3 = doc.RootElement
            .GetProperty("step57AllMaderaCategoriesAudit")
            .GetProperty("findings")
            .GetProperty("step57_F3_McH01221_PerDocketUnreachabilityConfirmed");

        var summary = f3.GetProperty("summary").GetString() ?? string.Empty;
        Assert.Contains("985", summary);
        Assert.Contains("MMH01221", summary);
        Assert.Contains("NOT in", summary);
    }

    // ─── CIV/FAM/MEN.knownCategoryCodes Step #57 expansion assertions ────

    [Fact]
    public void Step57_CivPolicy_KnownCategoryCodes_StillContainsStep57EraCodes_AfterStep59Expansion()
    {
        // Step #57-era CIV codes were [411100, 411900, 412910, 415110]. Step #59
        // expanded CIV to 46 evidence-backed codes via the exhaustive
        // direct-CMS list-query + GetCase DETAIL-verification probe (all 85 evidence
        // codes detail-MATCHED their list-level CaseCategoryText, 0 diverge — see
        // .playwright-cli/step59-detail-verification.json). The EXACT 46-code pin now
        // lives in Step59_UnmappedCategoryPromotionAuditTests; this test is relaxed to
        // a non-regression SUBSET check guarding the Step #57 codes against accidental
        // removal during the Step #59 expansion.
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue("CIV", out var civ));
        var codes = civ!.KnownCategoryCodes ?? new List<string>();

        foreach (var step57Code in new[] { "411100", "411900", "412910", "415110" })
            Assert.Contains(step57Code, codes);
    }

    [Fact]
    public void Step57_FamPolicy_KnownCategoryCodes_StillContainsStep57EraCodes_AfterStep59Expansion()
    {
        // Step #57-era FAM codes were [211110, 211120, 212110, 212120, 213110,
        // 291110]. Step #59 expanded FAM to 23 evidence-backed codes
        // (detail-verified). The EXACT 23-code pin lives in
        // Step59_UnmappedCategoryPromotionAuditTests; this is a non-regression subset
        // check on the Step #57 codes.
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue("FAM", out var fam));
        var codes = fam!.KnownCategoryCodes ?? new List<string>();

        foreach (var step57Code in new[] { "211110", "211120", "212110", "212120", "213110", "291110" })
            Assert.Contains(step57Code, codes);
    }

    [Fact]
    public void Step57_MenPolicy_KnownCategoryCodes_StillContainsStep57EraCodes_AfterStep59Expansion()
    {
        // Step #57-era MEN codes were [604110, 605110, 606110, 609110, 612110,
        // 613110, 614120]. Step #59 added 610110 (WI1800-Juvenile,
        // detail caseTypeCode=611110 MEN umbrella, docket MMH01161) -> MEN now 8
        // codes. The EXACT 8-code pin lives in
        // Step59_UnmappedCategoryPromotionAuditTests; this is a non-regression subset
        // check on the Step #57 codes.
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue("MEN", out var men));
        var codes = men!.KnownCategoryCodes ?? new List<string>();

        foreach (var step57Code in new[] { "604110", "605110", "606110", "609110", "612110", "613110", "614120" })
            Assert.Contains(step57Code, codes);
    }

    [Theory]
    [InlineData("411100", "CIV")]
    [InlineData("213110", "FAM")]
    [InlineData("606110", "MEN")]
    [InlineData("612110", "MEN")]
    public void Step57_MaderaCategoryMappings_ContainsStep57AdditionMappedToExpectedJccc(
        string code, string expectedJccc)
    {
        using var doc = LoadMappingsJson();
        var mappings = doc.RootElement
            .GetProperty("courts")
            .GetProperty("madera")
            .GetProperty("categoryCodeToJccc");

        Assert.True(
            mappings.TryGetProperty(code, out var jccc),
            $"madera.categoryCodeToJccc missing Step #57 addition '{code}'.");
        Assert.Equal(expectedJccc, jccc.GetString());
    }

    [Fact]
    public void Step57_MaderaCategoryMappings_TotalCountIs105_AfterStep59Expansion()
    {
        // Step #57 brought the count to 42 (3 CIV + 5 UD + 5 FAM + 20 PRB + 5 MEN +
        // the 4 Step #57 additions). Step #59 promoted 63 newly
        // evidence-backed codes (+41 CIV, +1 CIV/Small-Claims 711110, +17 FAM,
        // +3 PRB, +1 MEN) -> 105 total. Every promotion was DETAIL-verified via
        // GetCase (85/85 list==detail, 0 diverge). The per-bucket breakdown
        // (CIV 46, UD 5, FAM 23, PRB 23, MEN 8) is pinned in
        // Step59_UnmappedCategoryPromotionAuditTests. Drift on this count means a
        // mapping was added/removed without an audit-block update.
        using var doc = LoadMappingsJson();
        var mappings = doc.RootElement
            .GetProperty("courts")
            .GetProperty("madera")
            .GetProperty("categoryCodeToJccc");

        Assert.Equal(JsonValueKind.Object, mappings.ValueKind);
        Assert.Equal(105, mappings.EnumerateObject().Count());
    }

    // ─── MEN.openQuestions / resolvedOpenQuestions Step #57 reframing ────

    [Fact]
    public void Step57_MenPolicy_OpenQuestions_Has6Entries()
    {
        // Step #56 left openQuestions at 8 entries (2 original + 5 Madera-
        // non-applicable + 1 needs-re-probe). Step #57 removed the 606110
        // and 612110 entries (both now in resolvedOpenQuestions). Final
        // count: 2 original + 4 Madera-non-applicable = 6.
        using var doc = LoadSchemaJson();
        var open = doc.RootElement
            .GetProperty("policies")
            .GetProperty("MEN")
            .GetProperty("openQuestions");

        Assert.Equal(JsonValueKind.Array, open.ValueKind);
        Assert.Equal(6, open.GetArrayLength());
    }

    [Fact]
    public void Step57_MenPolicy_ResolvedOpenQuestions_HasAtLeast3Entries_With2FromStep57()
    {
        // Step #56 left resolvedOpenQuestions at 1 entry (Step #50
        // confidentiality-gate negative finding). Step #57 added 2 new
        // entries — one resolving 606110 / PC1368-Competency, one
        // resolving 612110 / In Re Hop-Developmentally Disabled. Both
        // must reference 'Step #57' in resolvedAt.
        using var doc = LoadSchemaJson();
        var resolved = doc.RootElement
            .GetProperty("policies")
            .GetProperty("MEN")
            .GetProperty("resolvedOpenQuestions");

        Assert.Equal(JsonValueKind.Array, resolved.ValueKind);
        Assert.True(
            resolved.GetArrayLength() >= 3,
            $"MEN.resolvedOpenQuestions expected >= 3 entries, got {resolved.GetArrayLength()}.");

        var step57Entries = resolved.EnumerateArray()
            .Where(e => (e.TryGetProperty("resolvedAt", out var r) ? r.GetString() ?? "" : "")
                .Contains("Step #57"))
            .ToList();
        Assert.Equal(2, step57Entries.Count);

        // Each Step #57 entry must have newKnownCategoryCode pointing at
        // the promoted code.
        var promotedCodes = step57Entries
            .Where(e => e.TryGetProperty("newKnownCategoryCode", out _))
            .Select(e => e.GetProperty("newKnownCategoryCode").GetString() ?? "")
            .OrderBy(c => c)
            .ToList();
        Assert.Equal(new[] { "606110", "612110" }, promotedCodes);
    }

    // ─── completenessNote Step #57 reference ────────────────────────────

    [Fact]
    public void Step57_CompletenessNote_ReferencesStep57AndAllMaderaAuditBlock()
    {
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        var note = schema.CompletenessNote ?? string.Empty;

        Assert.Contains("Step #57", note);
        Assert.Contains("step57AllMaderaCategoriesAudit", note);
        Assert.Contains("411100", note);
        Assert.Contains("213110", note);
        Assert.Contains("606110", note);
        Assert.Contains("612110", note);
        // Earlier MEN-affecting steps must still be referenced.
        Assert.Contains("Step #56", note);
        Assert.Contains("Step #54", note);
    }

    // ─── filesModified pin (for traceability) ───────────────────────────

    [Fact]
    public void Step57_AuditBlock_FilesModified_IncludesAllStep57TouchedFiles()
    {
        using var doc = LoadSchemaJson();
        var arr = doc.RootElement
            .GetProperty("step57AllMaderaCategoriesAudit")
            .GetProperty("filesModified");

        Assert.Equal(JsonValueKind.Array, arr.ValueKind);
        var entries = arr.EnumerateArray()
            .Select(e => e.GetString() ?? "")
            .ToList();

        // Specific files we expect every Step #57 to have touched.
        string[] expectedFileSubstrings =
        {
            "JtiCaseCategoryPolicy.json",
            "JtiCourtCategoryMappings.json",
            "CaseSearchModel.cs",
            "CourtFilingController.cs",
            "EFilingMvcController.cs",
            "SearchCase.cshtml",
            "probe-step57",
            "Step57_AllMaderaCategoriesAuditTests.cs",
        };
        foreach (var sub in expectedFileSubstrings)
        {
            Assert.True(
                entries.Any(e => e.Contains(sub)),
                $"step57AllMaderaCategoriesAudit.filesModified missing entry containing '{sub}'.");
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static JsonDocument LoadSchemaJson() =>
        LoadEmbeddedJson(SchemaResourceName);

    private static JsonDocument LoadMappingsJson() =>
        LoadEmbeddedJson(MappingsResourceName);

    private static JsonDocument LoadEmbeddedJson(string resourceName)
    {
        var assembly = typeof(JtiFieldSchemaProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException(
                $"Embedded resource '{resourceName}' not found in assembly " +
                $"'{assembly.FullName}'.");
        return JsonDocument.Parse(stream);
    }
}
