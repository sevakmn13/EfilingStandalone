using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using EFiling.Providers.JTI.Config;
using Xunit;

namespace EFiling.Tests;

/// <summary>
/// Drift-guard tests for Step #56 Mental-Health caseCategoryCode evidence-expansion.
///
/// <para>
/// Step #56 applied Step #52's direct-legalhub-probe methodology to MEN
/// (previously 2 codes from Step #49) and expanded MEN.knownCategoryCodes
/// with 3 new evidence-backed Madera CMS codes via user-provided dockets:
/// </para>
///
/// <list type="bullet">
///   <item><b>604110 (WI3050/3051-Narcotics Addict):</b> probed via
///     MMH00135 → caseCategoryCode=604110, caseTypeCode=611110.</item>
///   <item><b>605110 (PC2966-Commitments):</b> probed via MMH00612 →
///     caseCategoryCode=605110, caseTypeCode=611110. <b>Key finding:</b>
///     PC-prefixed label does NOT imply CRI classification. The case files
///     under MEN umbrella (611110), rebutting the pre-probe Tier-2
///     hypothesis.</item>
///   <item><b>609110 (WI6600-Sexually Violent Predator):</b> probed via
///     MMH00201 → caseCategoryCode=609110, caseTypeCode=611110.</item>
/// </list>
///
/// <para>
/// 5 categories returned 0 InfoTrack search results at Madera and are now
/// documented as Madera-non-applicable (analogous to Step #53's Protect
/// Minor + Lodged Will pattern): 601110, 602110, 607110, 608110, 612110.
/// </para>
///
/// <para>
/// 1 probe (MMH01221, PC1368-Competency, codelist 606110) hit a dev-server
/// stale-build runtime view-compilation error and is parked as
/// 'needs-re-probe' in MEN.openQuestions. Not a code defect — source code
/// is correct; runtime needs assembly rebuild + server restart.
/// </para>
///
/// <para>
/// These tests pin the new MEN expansion state + audit block structure
/// against future drift. The 6th canonical test asserts the
/// PC-prefix-doesn't-imply-CRI finding explicitly so we don't accidentally
/// re-introduce that hypothesis at a later step.
/// </para>
/// </summary>
public sealed class Step56_MenEvidenceAuditTests
{
    private const string SchemaResourceName =
        "EFiling.Providers.JTI.Config.JtiCaseCategoryPolicy.json";

    private const string MappingsResourceName =
        "EFiling.Providers.JTI.Config.JtiCourtCategoryMappings.json";

    // ─── step56MenEvidenceAudit audit-block structural assertions ───────

    [Fact]
    public void Step56_AuditBlock_IsPresentInSchemaJson_WithRequiredTopLevelKeys()
    {
        using var doc = LoadSchemaJson();
        Assert.True(
            doc.RootElement.TryGetProperty("step56MenEvidenceAudit", out var audit),
            "Top-level step56MenEvidenceAudit block is missing.");

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
                $"step56MenEvidenceAudit missing required key '{key}'.");
        }
    }

    [Fact]
    public void Step56_AuditBlock_EvidenceTable_ContainsAll9ExpectedEntries()
    {
        using var doc = LoadSchemaJson();
        var audit = doc.RootElement.GetProperty("step56MenEvidenceAudit");
        var table = audit.GetProperty("evidenceTable");

        string[] expectedEntries =
        {
            // 3 evidence-backed
            "WI3050_3051_NarcoticsAddict",
            "PC2966_Commitments",
            "WI6600_SexuallyViolentPredator",
            // 1 needs-re-probe
            "PC1368_Competency",
            // 5 Madera-non-applicable
            "WI5250_5260_5270_Certification",
            "WI5300_PostCertificationTreatment",
            "PC1026_NGdueToInsanity",
            "WI6300_MentalDisorderSexOffen",
            "InReHop_DevelopmentallyDisabled",
        };
        foreach (var entry in expectedEntries)
        {
            Assert.True(
                table.TryGetProperty(entry, out _),
                $"step56MenEvidenceAudit.evidenceTable missing required entry '{entry}'.");
        }
    }

    [Fact]
    public void Step56_AuditBlock_EvidenceTable_WI3050NarcoticsAddict_Pins604110AndMMH00135AndTypeCode611110()
    {
        using var doc = LoadSchemaJson();
        var entry = doc.RootElement
            .GetProperty("step56MenEvidenceAudit")
            .GetProperty("evidenceTable")
            .GetProperty("WI3050_3051_NarcoticsAddict");

        Assert.Equal("MMH00135", entry.GetProperty("probedDocket").GetString());
        Assert.Equal("604110", entry.GetProperty("probedCaseCategoryCode").GetString());
        Assert.Equal("611110", entry.GetProperty("probedCaseTypeCode").GetString());
        Assert.Equal("604110", entry.GetProperty("codelistSubmissionCode").GetString());
        Assert.Contains("EVIDENCE-BACKED", entry.GetProperty("outcome").GetString());
    }

    [Fact]
    public void Step56_AuditBlock_EvidenceTable_PC2966Commitments_Pins605110AndMMH00612AndPcPrefixRebuttal()
    {
        using var doc = LoadSchemaJson();
        var entry = doc.RootElement
            .GetProperty("step56MenEvidenceAudit")
            .GetProperty("evidenceTable")
            .GetProperty("PC2966_Commitments");

        Assert.Equal("MMH00612", entry.GetProperty("probedDocket").GetString());
        Assert.Equal("605110", entry.GetProperty("probedCaseCategoryCode").GetString());
        Assert.Equal("611110", entry.GetProperty("probedCaseTypeCode").GetString());
        Assert.Contains("EVIDENCE-BACKED", entry.GetProperty("outcome").GetString());

        // F2 rebuttal: PC-prefix does NOT imply CRI classification. The
        // finding text must explicitly call this out so we don't
        // accidentally re-introduce the hypothesis.
        var finding = entry.GetProperty("finding").GetString() ?? string.Empty;
        Assert.Contains("NOT under CRI", finding);
        Assert.Contains("MEN umbrella", finding);
        Assert.Contains("611110", finding);
    }

    [Fact]
    public void Step56_AuditBlock_EvidenceTable_WI6600SVP_Pins609110AndMMH00201AndTypeCode611110()
    {
        using var doc = LoadSchemaJson();
        var entry = doc.RootElement
            .GetProperty("step56MenEvidenceAudit")
            .GetProperty("evidenceTable")
            .GetProperty("WI6600_SexuallyViolentPredator");

        Assert.Equal("MMH00201", entry.GetProperty("probedDocket").GetString());
        Assert.Equal("609110", entry.GetProperty("probedCaseCategoryCode").GetString());
        Assert.Equal("611110", entry.GetProperty("probedCaseTypeCode").GetString());
        Assert.Contains("EVIDENCE-BACKED", entry.GetProperty("outcome").GetString());
    }

    [Fact]
    public void Step56_AuditBlock_EvidenceTable_PC1368Competency_PinsMMH01221AsCaseNotResolvableAtCms()
    {
        // Step #56 had TWO probe passes for MMH01221:
        //   Pass 1: HTTP 500 from dev-server stale-build view-compilation
        //           error (PID 21280 was loading pre-Step-#54
        //           EFiling.Nop.dll from 5/21 with the 1-arg
        //           RequiresDisclaimer signature).
        //   Pass 2: After NopCommerce.sln rebuild + Nop.Web restart with
        //           explicit ASPNETCORE_URLS env, HTTP 200 with 1-hop
        //           redirect to /CourtFiling/SearchCase because CMS
        //           GetCase returns null for MMH01221.
        // Final outcome: CASE-NOT-RESOLVABLE-AT-CMS. Code 606110 stays
        // unobserved at Madera.
        using var doc = LoadSchemaJson();
        var entry = doc.RootElement
            .GetProperty("step56MenEvidenceAudit")
            .GetProperty("evidenceTable")
            .GetProperty("PC1368_Competency");

        Assert.Equal("MMH01221", entry.GetProperty("probedDocket").GetString());
        Assert.Equal("606110", entry.GetProperty("codelistSubmissionCode").GetString());

        var outcome = entry.GetProperty("outcome").GetString() ?? string.Empty;
        Assert.Contains("CASE-NOT-RESOLVABLE-AT-CMS", outcome);

        // The finding must capture BOTH the original stale-build issue
        // (now resolved) AND the post-restart re-probe outcome
        // (CMS GetCase returns null). This gives future operators the
        // full trail.
        var finding = entry.GetProperty("finding").GetString() ?? string.Empty;
        Assert.Contains("RequiresDisclaimer", finding);
        Assert.Contains("Step #54", finding);
        Assert.Contains("RESOLVED", finding);

        // The reProbeMethodologyResolved key documents the operational
        // mitigation (build + ASPNETCORE_URLS env) so future sessions
        // don't repeat the same gotcha.
        Assert.True(
            entry.TryGetProperty("reProbeMethodologyResolved", out var resolved),
            "PC1368_Competency missing 'reProbeMethodologyResolved' key (operational-fix audit trail).");
        var resolvedText = resolved.GetString() ?? string.Empty;
        Assert.Contains("ASPNETCORE_URLS", resolvedText);
        Assert.Contains("launchSettings.json", resolvedText);
    }

    [Theory]
    [InlineData("WI5250_5260_5270_Certification", "601110")]
    [InlineData("WI5300_PostCertificationTreatment", "602110")]
    [InlineData("PC1026_NGdueToInsanity", "607110")]
    [InlineData("WI6300_MentalDisorderSexOffen", "608110")]
    [InlineData("InReHop_DevelopmentallyDisabled", "612110")]
    public void Step56_AuditBlock_EvidenceTable_MaderaNonApplicableEntry_PinsCodelistCodeAndZeroResults(
        string entryKey, string expectedCodelistCode)
    {
        using var doc = LoadSchemaJson();
        var entry = doc.RootElement
            .GetProperty("step56MenEvidenceAudit")
            .GetProperty("evidenceTable")
            .GetProperty(entryKey);

        Assert.Equal(expectedCodelistCode, entry.GetProperty("codelistSubmissionCode").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("probedDocket").ValueKind);
        Assert.Contains("MADERA-NON-APPLICABLE", entry.GetProperty("outcome").GetString());
    }

    [Fact]
    public void Step56_AuditBlock_Findings_ContainsAll5RequiredKeys()
    {
        using var doc = LoadSchemaJson();
        var audit = doc.RootElement.GetProperty("step56MenEvidenceAudit");
        Assert.True(audit.TryGetProperty("findings", out var findings));

        string[] requiredFindings =
        {
            "step56_F1_ThreeNewMenCodes",
            "step56_F2_PcPrefixDoesNotImplyCri",
            "step56_F3_FiveMaderaNonApplicableCategories",
            "step56_F4_DevServerStaleBuildSurfacedAndResolved",
            "step56_F5_MenEvidenceBackedNowFive",
        };
        foreach (var key in requiredFindings)
        {
            Assert.True(
                findings.TryGetProperty(key, out _),
                $"step56MenEvidenceAudit.findings missing required finding '{key}'.");
        }
    }

    // ─── MEN.knownCategoryCodes expansion assertions ────────────────────

    [Fact]
    public void Step56_MenPolicy_KnownCategoryCodes_ContainsAtLeastStep56FiveExpectedCodes()
    {
        // F1 backward-compat enforcement: the 5 Step #56-era codes
        // (604110/605110/609110/613110/614120) must remain in
        // MEN.knownCategoryCodes regardless of subsequent expansions.
        // Step #57 supersession: this test was originally an
        // exact-equality check pinned at 5 codes; relaxed to AT-LEAST when
        // Step #57 promoted 606110 (PC1368-Competency, 985 Madera cases via
        // direct CMS list-query) and 612110 (Hop-Developmentally Disabled,
        // 1 case MMH01159 — InfoTrack false-negative at Step #56). The
        // exact-equality pin lives in `Step57_AllMaderaCategoriesAuditTests`.
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue("MEN", out var men));
        var codes = men!.KnownCategoryCodes ?? new List<string>();

        foreach (var step56Code in new[] { "604110", "605110", "609110", "613110", "614120" })
        {
            Assert.Contains(step56Code, codes);
        }
    }

    [Theory]
    [InlineData("604110")] // Step #56 — WI3050/3051-Narcotics Addict
    [InlineData("605110")] // Step #56 — PC2966-Commitments (PC-not-CRI)
    [InlineData("609110")] // Step #56 — WI6600-Sexually Violent Predator
    [InlineData("613110")] // Step #49 — Mental Health-Other
    [InlineData("614120")] // Step #49 — Mental Health-Other Writ
    public void Step56_MenPolicy_KnownCategoryCodes_ContainsExpectedCode(string code)
    {
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue("MEN", out var men));
        Assert.Contains(code, men!.KnownCategoryCodes ?? new List<string>());
    }

    [Theory]
    [InlineData("601110")] // WI5250/5260/5270-Certification — confirmed Madera-non-applicable (Step #56 InfoTrack 0 + Step #57 direct CMS 0)
    [InlineData("602110")] // WI5300-PostCertification Treatment — confirmed Madera-non-applicable
    [InlineData("607110")] // PC1026-NG due to Insanity — confirmed Madera-non-applicable
    [InlineData("608110")] // WI6300-Mental Disorder Sex Offen — confirmed Madera-non-applicable
    // Step #57 supersession: 606110 + 612110 InlineData entries were
    // REMOVED here. Both codes are now in MEN.knownCategoryCodes after
    // Step #57's direct CMS list-query proved them evidence-backed (606110
    // = 985 cases including MMH00133; 612110 = 1 case MMH01159).
    public void Step56_MenPolicy_KnownCategoryCodes_DoesNotContain_ConfirmedMaderaNonApplicableCode(string code)
    {
        // Step #56 + Step #57 cross-validated: these codes return 0 from
        // both InfoTrack search AND direct CMS list-query. They are
        // genuinely absent at Madera and must not be added without
        // non-Madera evidence.
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue("MEN", out var men));
        Assert.DoesNotContain(code, men!.KnownCategoryCodes ?? new List<string>());
    }

    // ─── JtiCourtCategoryMappings.json madera section assertions ────────

    [Theory]
    [InlineData("604110")]
    [InlineData("605110")]
    [InlineData("609110")]
    public void Step56_MaderaCategoryMappings_ContainsStep56AdditionMappedToMEN(string code)
    {
        using var doc = LoadMappingsJson();
        var mappings = doc.RootElement
            .GetProperty("courts")
            .GetProperty("madera")
            .GetProperty("categoryCodeToJccc");

        Assert.True(
            mappings.TryGetProperty(code, out var jccc),
            $"madera.categoryCodeToJccc missing Step #56 addition '{code}'.");
        Assert.Equal("MEN", jccc.GetString());
    }

    // ─── MEN.openQuestions Step #56 reframing assertions ────────────────

    [Fact]
    public void Step56_MenPolicy_PC1368_MovedToResolvedOpenQuestionsAtStep57()
    {
        // Step #57 supersession: at Step #56 the PC1368-Competency entry
        // lived in `MEN.openQuestions` as 'CASE-NOT-RESOLVABLE-AT-CMS'
        // because the only known docket (MMH01221) was unreachable from
        // CMS. At Step #57 the direct CMS list-query found 985 OTHER
        // cases under code 606110, separating the per-docket finding
        // (MMH01221 stays unreachable, recorded in step56MenEvidenceAudit
        // as historical evidence) from the per-code finding (606110 IS
        // evidence-backed at Madera). The PC1368 entry MOVED from
        // openQuestions to resolvedOpenQuestions with newKnownCategoryCode='606110'.
        using var doc = LoadSchemaJson();
        var men = doc.RootElement.GetProperty("policies").GetProperty("MEN");

        // (a) Must NOT be in openQuestions any more.
        Assert.True(men.TryGetProperty("openQuestions", out var open));
        var stillInOpenQuestions = open.EnumerateArray().Any(e =>
        {
            var text = e.GetString() ?? string.Empty;
            return text.Contains("PC1368-Competency") && text.Contains("606110");
        });
        Assert.False(
            stillInOpenQuestions,
            "PC1368-Competency (606110) should have been moved out of MEN.openQuestions at Step #57 " +
            "(direct CMS list-query revealed 985 cases under 606110 — promoted to knownCategoryCodes).");

        // (b) MUST now be in resolvedOpenQuestions with the Step #57
        //     resolution note + newKnownCategoryCode='606110'.
        Assert.True(men.TryGetProperty("resolvedOpenQuestions", out var resolved));
        var inResolved = resolved.EnumerateArray().Any(e =>
        {
            if (!e.TryGetProperty("resolvedAt", out var resolvedAt)) return false;
            if (!e.TryGetProperty("resolution", out var resolution)) return false;
            var resolvedAtText = resolvedAt.GetString() ?? string.Empty;
            var resolutionText = resolution.GetString() ?? string.Empty;
            return resolvedAtText.Contains("Step #57")
                && resolutionText.Contains("606110")
                && resolutionText.Contains("MMH00133")
                && resolutionText.Contains("985");
        });
        Assert.True(
            inResolved,
            "MEN.resolvedOpenQuestions must contain a Step #57 entry resolving the PC1368-Competency question " +
            "(must reference 606110, MMH00133, and the 985-case count).");
    }

    [Theory]
    [InlineData("WI5250/5260/5270-Certification", "601110")]
    [InlineData("WI5300-PostCertification Treatment", "602110")]
    [InlineData("PC1026-NG due to Insanity", "607110")]
    [InlineData("WI6300-Mental Disorder Sex Offen", "608110")]
    // Step #57 supersession: removed the 612110 / In Re Hop-Developmentally
    // Disabled InlineData. Direct CMS list-query at Step #57 returned 1
    // case (MMH01159), proving Step #56's InfoTrack-based Madera-non-
    // applicable framing was a false-negative for that code. 612110 is
    // now in MEN.knownCategoryCodes + resolvedOpenQuestions.
    public void Step56_MenPolicy_OpenQuestions_ContainsMaderaNonApplicableEntryForCategory(
        string categoryLabel, string codelistCode)
    {
        using var doc = LoadSchemaJson();
        var men = doc.RootElement.GetProperty("policies").GetProperty("MEN");
        Assert.True(men.TryGetProperty("openQuestions", out var open));

        var hasEntry = open.EnumerateArray().Any(e =>
        {
            var text = e.GetString() ?? string.Empty;
            return text.Contains(categoryLabel)
                && text.Contains(codelistCode)
                && text.Contains("Step #56")
                && (text.Contains("Madera-non-applicable") || text.Contains("0 InfoTrack results"));
        });
        Assert.True(
            hasEntry,
            $"MEN.openQuestions missing the Step #56-added Madera-non-applicable entry for " +
            $"'{categoryLabel}' (codelist {codelistCode}).");
    }

    // ─── completenessNote Step #56 reference ────────────────────────────

    [Fact]
    public void Step56_CompletenessNote_ReferencesStep56AndMenEvidenceBlock()
    {
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        var note = schema.CompletenessNote ?? string.Empty;

        Assert.Contains("Step #56", note);
        Assert.Contains("step56MenEvidenceAudit", note);
        // Earlier MEN-affecting steps must still be referenced in the
        // narrative chain.
        Assert.Contains("Step #49", note);
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
