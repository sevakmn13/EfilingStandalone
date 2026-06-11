using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EFiling.Providers.JTI.Config;
using Xunit;

namespace EFiling.Tests;

/// <summary>
/// Step #54 — KD-001 CLOSURE drift-guard tests.
///
/// <para>
/// <b>Historical context (pre-Step-#54):</b> KD-001 ("Per-court CASE_CATEGORY
/// codelist namespace is currently flat (Madera-only assumption)") was logged
/// at Step #49.X under <c>knownDebt_KD_001_FlatCategoryCodeNamespace</c> in
/// JtiCaseCategoryPolicy.json. The pre-#54 <c>knownCategoryCodes</c> arrays on
/// each policy were flat string lists with no court-id scoping. The pre-#54
/// resolver <c>FindPolicyByCourtCategoryCode(string)</c> matched on code-only,
/// silently assuming all CA courts share a single CASE_CATEGORY codelist
/// namespace — a direct conflict with JTI's per-court-policy mandate.
/// </para>
///
/// <para>
/// <b>Closure at Step #54 (Option B):</b> Codes moved out of
/// JtiCaseCategoryPolicy.json into the new <c>JtiCourtCategoryMappings.json</c>
/// slice file. Resolver signature changed to
/// <see cref="JtiFieldSchemaProvider.FindPolicyByCourtCategoryCode(string, string)"/>
/// — courtId is now a required parameter. The legacy
/// <c>CaseCategoryPolicy.KnownCategoryCodes</c> field is preserved as a
/// PROJECTION from the mapping file (union of all courts' codes that map to
/// that JCCC) for back-compat with the existing Step #49/#52/#53 drift-guards.
/// </para>
///
/// <para>
/// <b>Post-closure invariants pinned by this file:</b>
/// </para>
/// <list type="bullet">
///   <item>Resolver signature MUST take (courtId, code) — the inverted
///     signature assertion is the regression guard.</item>
///   <item>Madera codes in the projected <c>KnownCategoryCodes</c> still
///     match the Madera namespace shape (^\d{6}$) and (mostly) appear in
///     scripts/madera_case_category.txt — the 2 pre-existing codelist
///     sanity tests stay valid.</item>
///   <item>The new <c>JtiCourtCategoryMappings.json</c> mapping file exists
///     and has a <c>madera</c> entry whose codes round-trip with the
///     projection.</item>
/// </list>
/// </summary>
public sealed class KnownDebt_KD001_FlatCategoryCodeNamespaceTests
{
    /// <summary>
    /// Madera per-court CASE_CATEGORY codelist values are 6-digit numeric.
    /// LASC, by contrast, uses alpha codes like "LC" / "UD" per the JTI doc
    /// (jtiSourceQuote). This regex enforces the Madera-namespace shape.
    /// </summary>
    private static readonly Regex MaderaCodePattern = new(@"^\d{6}$", RegexOptions.Compiled);

    /// <summary>
    /// Codes that are Madera-namespace-shape (^\d{6}$) but not in the Madera
    /// captured codelist file `scripts/madera_case_category.txt`. These fall
    /// into one of two categories:
    /// <list type="bullet">
    ///   <item>JCCC codes Madera does not currently expose at the submission
    ///     dropdown level (legitimately codelist-absent).</item>
    ///   <item><b>CMS-stored caseCategoryCode values</b> that diverge from the
    ///     JTI EFM SUBMISSION codelist. Per the Step #51 + Step #52 audit
    ///     findings, `scripts/madera_case_category.txt` is the
    ///     <i>submission</i> codelist (filing-time dropdown), NOT the CMS
    ///     storage codelist. Some categories diverge — notably LPS
    ///     Conservatorship submits as 532110 but stores as 603110.</item>
    /// </list>
    /// Both classes are tolerated as long as the namespace shape is
    /// unambiguous, but listed explicitly so reviewers can spot drift.
    /// </summary>
    private static readonly HashSet<string> MaderaCodelistAllowedNonObserved = new()
    {
        // Step #52 — LPS Conservatorship: submission codelist
        // says 532110 (WI5350-LPS Conservatorship), but Madera CMS stores
        // it as 603110 (MEN-range). 3 dockets confirmed via direct legalhub
        // probe (MPR10622, MPR011875, MPR7458) — see
        // step52ProbateFamilyAudit.evidenceTable.Step51_reVerification.LPS_Conservatorship_dockets
        // in JtiCaseCategoryPolicy.json. 603110 is NOT present in
        // `scripts/madera_case_category.txt` because that file is the
        // SUBMISSION codelist; 603110 is a CMS-only storage code.
        "603110",
    };

    [Fact]
    public void KD001_AllKnownCategoryCodes_MatchMaderaNamespacePattern()
    {
        var policies = JtiFieldSchemaProvider.GetCaseCategoryPolicy().Policies;
        var offenders = new List<(string PolicyCode, string Code)>();

        foreach (var (policyCode, policy) in policies)
        {
            if (policy.KnownCategoryCodes is not { Count: > 0 } codes) continue;
            foreach (var code in codes)
            {
                if (!MaderaCodePattern.IsMatch(code))
                    offenders.Add((policyCode, code));
            }
        }

        Assert.True(
            offenders.Count == 0,
            BuildKd001FailureMessage(
                "Code(s) failed the Madera 6-digit-numeric namespace pattern (^\\d{6}$).",
                offenders));
    }

    [Fact]
    public void KD001_AllKnownCategoryCodes_AreInMaderaCodelistFile()
    {
        var maderaCodes = LoadMaderaCodelist();
        var policies = JtiFieldSchemaProvider.GetCaseCategoryPolicy().Policies;
        var offenders = new List<(string PolicyCode, string Code)>();

        foreach (var (policyCode, policy) in policies)
        {
            if (policy.KnownCategoryCodes is not { Count: > 0 } codes) continue;
            foreach (var code in codes)
            {
                // Only require Madera-codelist membership for codes that pass
                // the namespace shape — the shape test above will fail-fast
                // for non-numeric outliers, and we don't want a double-failure.
                if (!MaderaCodePattern.IsMatch(code)) continue;
                if (maderaCodes.Contains(code)) continue;
                if (MaderaCodelistAllowedNonObserved.Contains(code)) continue;
                offenders.Add((policyCode, code));
            }
        }

        Assert.True(
            offenders.Count == 0,
            BuildKd001FailureMessage(
                "Code(s) match the Madera namespace shape but are NOT present in " +
                "scripts/madera_case_category.txt. Either Madera has added a new " +
                "category (refresh the file) or the code belongs to a different " +
                "court's namespace.",
                offenders));
    }

    [Fact]
    public void KD001Closure_KnownCategoryCodesShape_StillListString_AsProjectedFromMappingFile()
    {
        // Step #54: Although the JSON source of truth moved from
        // policy.knownCategoryCodes (per-policy flat lists) to
        // JtiCourtCategoryMappings.json (per-court explicit mappings), the
        // legacy <c>CaseCategoryPolicy.KnownCategoryCodes</c> field is preserved
        // as a PROJECTION (union of all courts' codes for that JCCC) for
        // back-compat with existing drift-guards. If a future refactor removes
        // the property entirely, this test fires and reminds the maintainer to
        // migrate Step #49/#52/#53 tests to GetKnownCategoryCodesForCourt.
        var prop = typeof(CaseCategoryPolicy).GetProperty(nameof(CaseCategoryPolicy.KnownCategoryCodes));
        Assert.NotNull(prop);

        var propType = prop!.PropertyType;
        Assert.True(
            propType == typeof(List<string>) ||
            (propType.IsGenericType
             && propType.GetGenericTypeDefinition() == typeof(List<>)
             && propType.GetGenericArguments()[0] == typeof(string)),
            "CaseCategoryPolicy.KnownCategoryCodes shape changed. If the property " +
            "was removed in favor of court-explicit lookups via " +
            "GetKnownCategoryCodesForCourt(courtId, jccc), migrate the affected " +
            "Step #49/#52/#53 drift-guard tests to use that helper directly and " +
            "update this assertion to match the new shape.");
    }

    [Fact]
    public void KD001Closure_ResolverSignature_NowTakesCourtIdAndCode()
    {
        // Step #54 — KD-001 CLOSURE regression guard.
        // The flat-namespace bug was closed by adding a courtId parameter to
        // FindPolicyByCourtCategoryCode. If a future refactor accidentally
        // reverts to the single-param shape, this test fires and the maintainer
        // is forced to either restore the courtId scope or document the regression
        // (re-opening KD-001 in JtiCaseCategoryPolicy.json with an audit trail).
        var method = typeof(JtiFieldSchemaProvider).GetMethod(
            nameof(JtiFieldSchemaProvider.FindPolicyByCourtCategoryCode));
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal("courtId", parameters[0].Name);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal("courtCategoryCode", parameters[1].Name);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
    }

    // ─── Step #54 closure positive shape ─────────────────────

    [Fact]
    public void KD001Closure_CourtCategoryMappingsFile_HasMaderaEntry()
    {
        // Step #54 — the new mapping file MUST contain a 'madera'
        // entry as the bootstrap (single live court).
        var mappings = JtiFieldSchemaProvider.GetCourtCategoryMappings();
        Assert.NotNull(mappings);
        Assert.True(
            mappings.Courts.ContainsKey("madera"),
            "JtiCourtCategoryMappings.json missing required 'madera' court entry. " +
            "Madera is the Step-#54 bootstrap and the sole live court; its mapping " +
            "is the source of truth for all KnownCategoryCodes projections.");

        var madera = mappings.Courts["madera"];
        Assert.NotNull(madera.CategoryCodeToJccc);
        Assert.True(
            madera.CategoryCodeToJccc.Count >= 30,
            $"Madera mapping has only {madera.CategoryCodeToJccc.Count} entries — " +
            "expected >=30 (Step #52 PRB has 20 codes alone + UD 5 + FAM 5 + CIV 3 + MEN 2).");
    }

    [Fact]
    public void KD001Closure_MaderaMappings_RoundTripWithProjectedKnownCategoryCodes()
    {
        // Step #54 — verify the projection is consistent: every
        // code in the Madera mapping appears in some policy.KnownCategoryCodes,
        // and every code in policy.KnownCategoryCodes (under Madera-only world)
        // appears in the Madera mapping. This catches projection drift if the
        // ProjectKnownCategoryCodesFromMappings logic ever skips a court.
        var mappings = JtiFieldSchemaProvider.GetCourtCategoryMappings();
        var madera = mappings.Courts["madera"];
        var policies = JtiFieldSchemaProvider.GetCaseCategoryPolicy().Policies;

        // Direction 1: every Madera mapping code is in its target policy's KnownCategoryCodes.
        foreach (var (code, jcccKey) in madera.CategoryCodeToJccc)
        {
            Assert.True(policies.TryGetValue(jcccKey, out var policy),
                $"Madera mapping references unknown JCCC policy '{jcccKey}' for code '{code}'.");
            Assert.NotNull(policy.KnownCategoryCodes);
            Assert.Contains(code, policy.KnownCategoryCodes!);
        }

        // Direction 2: every projected KnownCategoryCodes entry has a corresponding
        // mapping entry in Madera (Madera-only court for now).
        foreach (var (jcccKey, policy) in policies)
        {
            if (policy.KnownCategoryCodes is not { Count: > 0 } codes) continue;
            foreach (var code in codes)
            {
                Assert.True(
                    madera.CategoryCodeToJccc.TryGetValue(code, out var mappedJccc),
                    $"Policy '{jcccKey}' has projected KnownCategoryCode '{code}' " +
                    "with no corresponding Madera mapping entry.");
                Assert.Equal(jcccKey, mappedJccc);
            }
        }
    }

    [Fact]
    public void KD001Closure_GetKnownCategoryCodesForCourt_MaderaPrb_Returns23Codes()
    {
        // Step #54 helper API check. Madera PRB was 20 codes per
        // Step #52 evidence; Step #59 restored 3 detail-verified
        // codes (511110, 532110, 561310 — all GetCase detail caseTypeCode=511110
        // PRB umbrella) -> 23.
        var codes = JtiFieldSchemaProvider.GetKnownCategoryCodesForCourt("madera", "PRB");
        Assert.Equal(23, codes.Count);
    }

    [Fact]
    public void KD001Closure_GetKnownCategoryCodesForCourt_UnknownCourt_ReturnsEmpty()
    {
        var codes = JtiFieldSchemaProvider.GetKnownCategoryCodesForCourt("unknown", "UD");
        Assert.Empty(codes);
    }

    [Fact]
    public void KD001Closure_GetKnownCategoryCodesForCourt_MaderaUnknownJccc_ReturnsEmpty()
    {
        var codes = JtiFieldSchemaProvider.GetKnownCategoryCodesForCourt("madera", "ZZZ");
        Assert.Empty(codes);
    }

    // ───────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────

    private static HashSet<string> LoadMaderaCodelist()
    {
        var path = LocateMaderaCodelistFile();
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length < 6) continue;
            // Format: "<6-digit-code><whitespace><label>" — skip section
            // headers / separators which won't lead with 6 digits.
            var prefix = trimmed[..6];
            if (MaderaCodePattern.IsMatch(prefix))
                codes.Add(prefix);
        }
        Assert.True(
            codes.Count > 50,
            $"Madera codelist file at '{path}' yielded only {codes.Count} codes — " +
            "expected >50. File may have been moved, truncated, or reformatted.");
        return codes;
    }

    private static string LocateMaderaCodelistFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "madera_case_category.txt");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not locate scripts/madera_case_category.txt by walking up from " +
            $"{AppContext.BaseDirectory}. KD-001 drift-guard tests require this " +
            "file to verify the Madera codelist namespace assumption.");
    }

    private static string BuildKd001FailureMessage(
        string headline,
        IReadOnlyCollection<(string PolicyCode, string Code)> offenders)
    {
        var lines = new List<string>
        {
            "[KNOWN-DEBT KD-001] Flat CASE_CATEGORY codelist namespace assumption violated.",
            "",
            headline,
            "",
            "Offending entries:",
        };
        foreach (var (policy, code) in offenders.OrderBy(o => o.PolicyCode).ThenBy(o => o.Code))
            lines.Add($"  • {policy}.knownCategoryCodes contains '{code}'");

        lines.AddRange(new[]
        {
            "",
            "Remediation:",
            "  1. If Madera onboarded a new category, refresh scripts/madera_case_category.txt",
            "     from the live codelist endpoint and re-run.",
            "  2. If a non-Madera court was added: register its entry under",
            "     `courts.<courtId>.categoryCodeToJccc` in",
            "     src/EFiling/EFiling.Providers.JTI/Config/JtiCourtCategoryMappings.json and",
            "     update this test if the new court's codes legitimately fall outside",
            "     the Madera 6-digit-numeric pattern (e.g., LASC alpha 'UD' codes).",
            "  3. The pre-Step-#54 flat-namespace bug is CLOSED at Step #54 — the",
            "     resolver now takes (courtId, code) and projections are court-scoped.",
            "     If this test is failing post-#54, it's a NEW DATA issue (above), NOT a",
            "     KD-001 regression.",
        });

        return string.Join(Environment.NewLine, lines);
    }
}
