using EFiling.Providers.JTI.Config;

namespace EFiling.Tests;

/// <summary>
/// Step #47 — T-1.E per-court observations forcing functions.
///
/// <para>
/// Per the catalog §6 expansion (Step #47), we now document 8 California
/// Superior Courts: Madera (live primary), LASC, Alameda, Riverside,
/// Ventura, Placer, Nevada County, Sacramento. The JSON policy file
/// (<c>JtiCaseCategoryPolicy.json</c>) carries <c>appliesToCourts</c>
/// scope markers on per-court rules (UD-3 LASC, UD-4 Ventura). These
/// tests lock the relationship between the policy data and the catalog
/// documentation:
/// </para>
///
/// <list type="number">
///   <item><b>Court-scope drift detection</b> — every <c>appliesToCourts</c>
///     value referenced by any policy rule must be in the closed set of
///     8 catalog-§6-documented courts. If someone adds a rule with a new
///     court ID (e.g., <c>"sandiego"</c>) without updating catalog §6,
///     the test fires.</item>
///   <item><b>JSON binding sanity</b> — the <c>AppliesToCourts</c>
///     property was added in Step #47 to deserialize the JSON field
///     which had previously been silently unbound. This test asserts
///     that the two known rules (UD-3, UD-4) load with the expected
///     court scope.</item>
/// </list>
///
/// <para>
/// If a future court is onboarded with new per-court rules, the
/// engineer must:
/// </para>
/// <list type="number">
///   <item>Add a §6.X section to <c>JTI_SUBSEQUENT_FILING_CATALOG.md</c>
///     documenting that court's deviations.</item>
///   <item>Add the lowercase court ID to <see cref="DocumentedCourtsInCatalogSection6"/>
///     below.</item>
///   <item>Add the per-court rules to <c>JtiCaseCategoryPolicy.json</c>
///     with <c>appliesToCourts</c> markers.</item>
/// </list>
/// </summary>
public class Step47_PerCourtObservationsTests
{
    /// <summary>
    /// The 8 California Superior Courts catalogued in
    /// <c>docs/JTI_SUBSEQUENT_FILING_CATALOG.md</c> §6 as of Step #47
    ///. All values are lowercase to match
    /// <c>CourtConfiguration.CourtId</c> convention.
    /// </summary>
    private static readonly HashSet<string> DocumentedCourtsInCatalogSection6 = new(StringComparer.OrdinalIgnoreCase)
    {
        // §6.1 — live primary court
        "madera",
        // §6.2 — heaviest JTI deviation surface (8 SF + 4 CI documented sections)
        "lasc",
        // §6.3 — JTI HTML CI Phase 2 + SF (IS_WITH_HEARING, IS_STIP, REQ020 fields)
        "alameda",
        // §6.4 — sample-folder-only, no JTI HTML deviations
        "riverside",
        // §6.5 — UD-4 Civil-Limited-only disclaimer + Minor AS TO + Small Claims AS TO Address
        "ventura",
        // §6.6 — Consent to eService
        "placer",
        // §6.7 — Nevada COUNTY, CA (NOT the State of Nevada) — eService MANDATED Local Rule 1.06
        "nevada",
        // §6.8 — ParentLocationCode routing (caseType + caseCategory instead of zipCode-only)
        "sacramento"
    };

    /// <summary>
    /// Forcing function: any <c>appliesToCourts</c> value in the policy
    /// JSON must be in the documented set. If you add a new value without
    /// updating catalog §6, this test fires.
    /// </summary>
    [Fact]
    public void Step47_AllAppliesToCourtsValues_AreDocumentedInCatalogSection6()
    {
        var policy = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        var observedCourts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (categoryName, categoryDef) in policy.Policies)
        {
            if (categoryDef.Rules == null) continue;
            foreach (var rule in categoryDef.Rules)
            {
                if (rule.AppliesToCourts == null) continue;
                foreach (var court in rule.AppliesToCourts)
                {
                    if (!string.IsNullOrWhiteSpace(court))
                        observedCourts.Add(court);
                }
            }
        }

        var undocumented = observedCourts
            .Where(c => !DocumentedCourtsInCatalogSection6.Contains(c))
            .OrderBy(c => c)
            .ToList();

        Assert.True(undocumented.Count == 0,
            $"Found appliesToCourts values not documented in catalog §6: " +
            $"[{string.Join(", ", undocumented)}]. " +
            $"Either add a §6.X section to docs/JTI_SUBSEQUENT_FILING_CATALOG.md " +
            $"documenting the new court's deviations, then add its lowercase ID to " +
            $"DocumentedCourtsInCatalogSection6 above — OR remove the appliesToCourts " +
            $"entry from JtiCaseCategoryPolicy.json.");
    }

    /// <summary>
    /// Lock the known per-court scope rules. Both rules below have been
    /// captured in the catalog §6 narratives. If a future audit changes
    /// the scope (e.g., LASC UDCOV19 is repealed) or the rule ID, this
    /// test fires and forces a synchronized catalog update.
    /// </summary>
    [Fact]
    public void Step47_KnownPerCourtRules_HaveExpectedAppliesToCourtsScopes()
    {
        var udPolicy = JtiFieldSchemaProvider.GetCaseCategoryPolicy("UD");
        Assert.NotNull(udPolicy);
        Assert.NotNull(udPolicy.Rules);

        var ud3 = udPolicy.Rules.FirstOrDefault(r => r.Id == "UD-3");
        Assert.NotNull(ud3);
        Assert.NotNull(ud3.AppliesToCourts);
        Assert.Single(ud3.AppliesToCourts);
        Assert.Equal("lasc", ud3.AppliesToCourts[0], ignoreCase: true);

        var ud4 = udPolicy.Rules.FirstOrDefault(r => r.Id == "UD-4");
        Assert.NotNull(ud4);
        Assert.NotNull(ud4.AppliesToCourts);
        Assert.Single(ud4.AppliesToCourts);
        Assert.Equal("ventura", ud4.AppliesToCourts[0], ignoreCase: true);
    }

    /// <summary>
    /// Court-scope rules MUST always be lowercase to match
    /// <c>CourtConfiguration.CourtId</c> convention (verified in
    /// <c>SeedCourtConfigurationData.cs</c>: <c>madera</c> not
    /// <c>Madera</c> or <c>MADERA</c>). Uppercase or mixed-case values
    /// would silently bypass case-sensitive lookups in any future
    /// runtime court-scope check.
    /// </summary>
    [Fact]
    public void Step47_AllAppliesToCourtsValues_AreLowercase()
    {
        var policy = JtiFieldSchemaProvider.GetCaseCategoryPolicy();
        var nonLowercase = new List<string>();

        foreach (var categoryDef in policy.Policies.Values)
        {
            if (categoryDef.Rules == null) continue;
            foreach (var rule in categoryDef.Rules)
            {
                if (rule.AppliesToCourts == null) continue;
                foreach (var court in rule.AppliesToCourts)
                {
                    if (string.IsNullOrEmpty(court)) continue;
                    if (court != court.ToLowerInvariant())
                        nonLowercase.Add($"{rule.Id}:{court}");
                }
            }
        }

        Assert.True(nonLowercase.Count == 0,
            $"Found non-lowercase appliesToCourts values: " +
            $"[{string.Join(", ", nonLowercase)}]. " +
            $"Court IDs must be lowercase per CourtConfiguration.CourtId convention.");
    }
}
