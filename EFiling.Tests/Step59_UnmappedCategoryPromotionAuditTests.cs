using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using EFiling.Providers.JTI.Config;
using Xunit;

namespace EFiling.Tests;

/// <summary>
/// Drift-guard tests for the Step #59 exhaustive UNMAPPED-code promotion +
/// GetCase detail-verification + CRI/ADMIN deferral (closed 2026-05-29).
///
/// <para>
/// Step #59 probed EVERY Madera codelist code not already in
/// <c>categoryCodeToJccc</c> (139 codes) via direct CMS list-query, then
/// DETAIL-verified all 85 codes that returned cases by issuing a GetCase on
/// the first sample docket and comparing the detail-level
/// <c>caseCategoryCode</c> (what the resolver receives at SF time) against the
/// list-level <c>CaseCategoryText</c>. Result: <b>85/85 MATCH, 0 diverge</b>
/// (see <c>.playwright-cli/step59-detail-verification.json</c>).
/// </para>
///
/// <para><b>Promotions (42 -> 105):</b> +41 CIV + 1 CIV/Small-Claims (711110),
/// +17 FAM, +3 PRB (511110/532110/561310 — restored on GetCase detail evidence
/// that SUPERSEDES the Step #52/#53 incomplete-evidence retractions), +1 MEN
/// (610110).</para>
///
/// <para><b>Deliberately NOT promoted (per user Option 2):</b> 19 searchable
/// CRI criminal codes (workflow/policy unvalidated; CRI stays
/// <c>awaitingEvidence</c>) + 3 self-typed ADMIN codes (110120 Lodged Will,
/// 110210 Sanctions, 120110 Juror OSC — no JCCC umbrella fits).</para>
///
/// <para>
/// This test file OWNS the exact post-#59 mapping state. Earlier-step tests
/// (Step #49/#51/#52/#53/#57 + KD-001) were relaxed to non-regression subset
/// checks or updated with Step #59 supersession notes — see those files.
/// </para>
/// </summary>
public sealed class Step59_UnmappedCategoryPromotionAuditTests
{
    private const string SchemaResourceName =
        "EFiling.Providers.JTI.Config.JtiCaseCategoryPolicy.json";

    private const string MappingsResourceName =
        "EFiling.Providers.JTI.Config.JtiCourtCategoryMappings.json";

    // ─── Exact post-#59 per-bucket Madera code sets (Ordinal-sorted) ─────

    private static readonly string[] ExpectedCiv =
    {
        "401100", "401110", "402100", "402200", "402300", "402400", "403100",
        "403200", "403300", "403400", "403500", "403600", "403610", "403700",
        "404100", "404110", "404200", "405100", "405300", "405400", "405500",
        "406100", "406200", "406300", "408100", "408200", "408300", "408400",
        "409200", "409400", "409500", "409600", "411100", "411900", "411910",
        "411920", "411930", "412100", "412900", "412910", "412920", "412930",
        "412940", "412950", "415110", "711110"
    };

    private static readonly string[] ExpectedFam =
    {
        "211110", "211120", "211121", "212110", "212120", "213110", "213120",
        "221110", "231110", "231120", "241210", "242110", "291110", "292110",
        "293110", "294110", "294210", "294220", "295110", "296110", "297110",
        "298110", "299110"
    };

    private static readonly string[] ExpectedPrb =
    {
        "511110", "511210", "511310", "511410", "511510", "511610", "521110",
        "531110", "532110", "541110", "551110", "551210", "551211", "551212",
        "551310", "551410", "551510", "551610", "561110", "561210", "561310",
        "603110", "614130"
    };

    private static readonly string[] ExpectedMen =
    {
        "604110", "605110", "606110", "609110", "610110", "612110", "613110", "614120"
    };

    private static readonly string[] ExpectedUd =
    {
        "407100", "407110", "407200", "407210", "407300"
    };

    // The 19 CRI codes that ARE searchable at Madera but were deliberately NOT
    // promoted (Option 2). They must NOT appear in categoryCodeToJccc.
    private static readonly string[] SearchableCriNotMapped =
    {
        "911110", "911120", "911130", "911140", "911150", "911160", "911170",
        "911180", "911190", "911200", "911201", "911310", "911410", "921140",
        "921170", "921180", "922120", "991110", "991150"
    };

    // The 3 self-typed ADMIN codes — searchable but not a JCCC bucket member.
    private static readonly string[] SearchableAdminNotMapped =
    {
        "110120", "110210", "120110"
    };

    // ─── Exact mapping counts ────────────────────────────────────────────

    [Fact]
    public void Step59_MaderaCategoryMappings_TotalCountIs105()
    {
        var map = LoadMaderaMappings();
        Assert.Equal(105, map.Count);
    }

    [Theory]
    [InlineData("CIV", 46)]
    [InlineData("UD", 5)]
    [InlineData("FAM", 23)]
    [InlineData("PRB", 23)]
    [InlineData("MEN", 8)]
    public void Step59_MaderaCategoryMappings_PerBucketCounts(string jccc, int expectedCount)
    {
        var map = LoadMaderaMappings();
        var count = map.Count(kv => kv.Value == jccc);
        Assert.Equal(expectedCount, count);
    }

    // ─── Exact per-bucket code sets (court-scoped projection) ────────────

    [Fact]
    public void Step59_CivCodes_EqualExactly46ExpectedCodes()
        => AssertCourtBucketEquals("CIV", ExpectedCiv);

    [Fact]
    public void Step59_FamCodes_EqualExactly23ExpectedCodes()
        => AssertCourtBucketEquals("FAM", ExpectedFam);

    [Fact]
    public void Step59_PrbCodes_EqualExactly23ExpectedCodes()
        => AssertCourtBucketEquals("PRB", ExpectedPrb);

    [Fact]
    public void Step59_MenCodes_EqualExactly8ExpectedCodes()
        => AssertCourtBucketEquals("MEN", ExpectedMen);

    [Fact]
    public void Step59_UdCodes_Unchanged_EqualExactly5ExpectedCodes()
        => AssertCourtBucketEquals("UD", ExpectedUd);

    // ─── Resolver-level confirmation of the Step #59 promotions ──────────

    [Theory]
    [InlineData("511110")] // generic Decedent's Estate (restored — MPR11267)
    [InlineData("532110")] // LPS Conservatorship (restored — MPR012467)
    [InlineData("561310")] // Ancillary Proceedings (restored — MPR015004)
    public void Step59_RestoredPrbCodes_ResolveToPrb(string code)
    {
        var policy = JtiFieldSchemaProvider.FindPolicyByCourtCategoryCode("madera", code);
        Assert.NotNull(policy);
        Assert.Equal("PRB", policy!.CategoryCode);
    }

    [Fact]
    public void Step59_SmallClaimsCode_711110_ResolvesToCiv()
    {
        // Small Claims (711110, caseTypeCode 711110) folds under the CIV policy
        // whose label is "Civil & Small Claims".
        var policy = JtiFieldSchemaProvider.FindPolicyByCourtCategoryCode("madera", "711110");
        Assert.NotNull(policy);
        Assert.Equal("CIV", policy!.CategoryCode);
    }

    [Theory]
    [InlineData("401100", "CIV")] // Auto Tort (2265 cases, S3830)
    [InlineData("405100", "CIV")] // Contract: Breach (2454 cases, MCV034162)
    [InlineData("412930", "CIV")] // Civil Pet: Name Change (1139 cases)
    [InlineData("231110", "FAM")] // DV Prevention w/Minor Child (2658, MCV01698)
    [InlineData("221110", "FAM")] // Establish Parental Relationship (1777, M62416)
    [InlineData("294210", "FAM")] // Register Foreign Support Orders (3137)
    [InlineData("610110", "MEN")] // WI1800-Juvenile (MMH umbrella 611110)
    public void Step59_SampleNewlyMappedCodes_ResolveToExpectedJccc(string code, string expectedJccc)
    {
        var policy = JtiFieldSchemaProvider.FindPolicyByCourtCategoryCode("madera", code);
        Assert.NotNull(policy);
        Assert.Equal(expectedJccc, policy!.CategoryCode);
    }

    // ─── CRI / ADMIN deliberately-NOT-mapped guards (Option 2) ───────────

    [Theory]
    [MemberData(nameof(CriCodes))]
    public void Step59_SearchableCriCodes_AreNotMapped(string code)
    {
        // Option 2: CRI codes are searchable at Madera but workflow/policy is
        // unvalidated, so they must NOT be in categoryCodeToJccc.
        var map = LoadMaderaMappings();
        Assert.False(map.ContainsKey(code),
            $"CRI code {code} must NOT be mapped (Step #59 Option 2 — searchable but deferred).");
        Assert.Null(JtiFieldSchemaProvider.FindPolicyByCourtCategoryCode("madera", code));
    }

    [Theory]
    [MemberData(nameof(AdminCodes))]
    public void Step59_SearchableAdminCodes_AreNotMapped(string code)
    {
        // Self-typed administrative codes don't fit any JCCC umbrella.
        var map = LoadMaderaMappings();
        Assert.False(map.ContainsKey(code),
            $"ADMIN code {code} must NOT be mapped (self-typed, no JCCC bucket).");
        Assert.Null(JtiFieldSchemaProvider.FindPolicyByCourtCategoryCode("madera", code));
    }

    public static IEnumerable<object[]> CriCodes() => SearchableCriNotMapped.Select(c => new object[] { c });
    public static IEnumerable<object[]> AdminCodes() => SearchableAdminNotMapped.Select(c => new object[] { c });

    [Theory]
    [InlineData("CRI")]
    [InlineData("JUV")]
    [InlineData("APP")]
    public void Step59_BlockedCategories_RemainAwaitingEvidence_WithEmptyKnownCategoryCodes(string jccc)
    {
        // Step #59 did NOT promote CRI/JUV/APP. They must stay awaitingEvidence
        // with no Madera-namespaced codes (CRI searchable-but-deferred; JUV
        // structurally blocked 14/14 zero; APP separate appellate system).
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        Assert.True(schema.Policies.TryGetValue(jccc, out var policy));
        Assert.True(policy!.AwaitingEvidence,
            $"{jccc}.awaitingEvidence must remain true after Step #59.");
        var codes = policy.KnownCategoryCodes;
        Assert.True(codes is null || codes.Count == 0,
            $"{jccc}.knownCategoryCodes must stay empty after Step #59 (count={codes?.Count ?? 0}).");
    }

    // ─── step59 audit block structural + content assertions ──────────────

    [Fact]
    public void Step59_AuditBlock_IsPresentInSchemaJson_WithRequiredKeys()
    {
        using var doc = LoadSchemaJson();
        Assert.True(
            doc.RootElement.TryGetProperty("step59UnmappedCategoryPromotionAudit", out var audit),
            "Top-level step59UnmappedCategoryPromotionAudit block is missing.");

        string[] requiredKeys =
        {
            "title", "purpose", "method", "detailVerification", "promotionSummary",
            "prbRestorations", "criSearchableNotMapped", "adminSearchableNotMapped",
            "emptyCodesConfirmed", "findings", "filesModified", "driftGuardTests",
        };
        foreach (var key in requiredKeys)
            Assert.True(audit.TryGetProperty(key, out _),
                $"step59UnmappedCategoryPromotionAudit missing required key '{key}'.");
    }

    [Fact]
    public void Step59_AuditBlock_DetailVerification_Pins85Of85Match()
    {
        using var doc = LoadSchemaJson();
        var dv = doc.RootElement
            .GetProperty("step59UnmappedCategoryPromotionAudit")
            .GetProperty("detailVerification");

        Assert.Equal(85, dv.GetProperty("evidenceCodesProbed").GetInt32());
        Assert.Equal(85, dv.GetProperty("detailMatch").GetInt32());
        Assert.Equal(0, dv.GetProperty("detailDiverge").GetInt32());
        Assert.Equal(0, dv.GetProperty("detailError").GetInt32());
    }

    [Fact]
    public void Step59_AuditBlock_PromotionSummary_Pins42To105()
    {
        using var doc = LoadSchemaJson();
        var ps = doc.RootElement
            .GetProperty("step59UnmappedCategoryPromotionAudit")
            .GetProperty("promotionSummary");

        Assert.Equal(42, ps.GetProperty("mappingCountBefore").GetInt32());
        Assert.Equal(105, ps.GetProperty("mappingCountAfter").GetInt32());
        Assert.Equal(63, ps.GetProperty("promoted").GetInt32());
    }

    [Fact]
    public void Step59_AuditBlock_CriSearchableNotMapped_Pins19CodesAndOption2()
    {
        using var doc = LoadSchemaJson();
        var cri = doc.RootElement
            .GetProperty("step59UnmappedCategoryPromotionAudit")
            .GetProperty("criSearchableNotMapped");

        Assert.Equal(19, cri.GetProperty("codeCount").GetInt32());
        Assert.Contains("Option 2", cri.GetProperty("decision").GetString() ?? string.Empty);

        var codes = cri.GetProperty("codes").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(19, codes.Count);
        foreach (var c in SearchableCriNotMapped)
            Assert.Contains(c, codes);
    }

    [Fact]
    public void Step59_AuditBlock_PrbRestorations_PinThreeDetailVerifiedCodes()
    {
        using var doc = LoadSchemaJson();
        var codes = doc.RootElement
            .GetProperty("step59UnmappedCategoryPromotionAudit")
            .GetProperty("prbRestorations")
            .GetProperty("codes");

        var byCode = codes.EnumerateArray()
            .ToDictionary(e => e.GetProperty("code").GetString()!, e => e);

        foreach (var (code, docket) in new[] { ("511110", "MPR11267"), ("532110", "MPR012467"), ("561310", "MPR015004") })
        {
            Assert.True(byCode.ContainsKey(code), $"prbRestorations missing code {code}.");
            var entry = byCode[code];
            Assert.Equal(docket, entry.GetProperty("docket").GetString());
            Assert.Equal(code, entry.GetProperty("detailCaseCategoryCode").GetString());
            Assert.Equal("511110", entry.GetProperty("detailCaseTypeCode").GetString());
        }
    }

    // ─── completenessNote Step #59 reference ─────────────────────────────

    [Fact]
    public void Step59_CompletenessNote_ReferencesStep59AndKeyFigures()
    {
        var schema = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        var note = schema.CompletenessNote ?? string.Empty;

        Assert.Contains("Step #59", note);
        Assert.Contains("step59UnmappedCategoryPromotionAudit", note);
        Assert.Contains("42→105", note);
        Assert.Contains("85/85", note);
        // Earlier-step references must survive.
        Assert.Contains("Step #57", note);
        Assert.Contains("RETRACTED", note);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static void AssertCourtBucketEquals(string jccc, string[] expected)
    {
        var actual = JtiFieldSchemaProvider.GetKnownCategoryCodesForCourt("madera", jccc)
            .OrderBy(c => c, System.StringComparer.Ordinal)
            .ToArray();
        var expectedSorted = expected.OrderBy(c => c, System.StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedSorted, actual);
    }

    private static Dictionary<string, string> LoadMaderaMappings()
    {
        using var doc = LoadMappingsJson();
        var map = doc.RootElement
            .GetProperty("courts")
            .GetProperty("madera")
            .GetProperty("categoryCodeToJccc");
        var result = new Dictionary<string, string>();
        foreach (var prop in map.EnumerateObject())
            result[prop.Name] = prop.Value.GetString() ?? string.Empty;
        return result;
    }

    private static JsonDocument LoadSchemaJson() => LoadEmbeddedJson(SchemaResourceName);
    private static JsonDocument LoadMappingsJson() => LoadEmbeddedJson(MappingsResourceName);

    private static JsonDocument LoadEmbeddedJson(string resourceName)
    {
        var assembly = typeof(JtiFieldSchemaProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException(
                $"Embedded resource '{resourceName}' not found in assembly '{assembly.FullName}'.");
        return JsonDocument.Parse(stream);
    }
}
