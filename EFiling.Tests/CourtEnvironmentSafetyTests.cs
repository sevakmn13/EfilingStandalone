using EFiling.Core.Enums;
using EFiling.Core.Models;
using EFiling.Core.Validation;

namespace EFiling.Tests;

/// <summary>
/// Unit tests for the staging-vs-production safety guards:
///   - <see cref="CourtEnvironmentParser"/> (label parsing)
///   - <see cref="JtiUrlConvention"/>    (URL hostname heuristics)
///   - <see cref="CourtConfigurationValidator"/> (aggregate validation)
///   - <see cref="CourtConfiguration"/>  (runtime helper properties / guards)
///   - <see cref="TestConfiguration.RequireStaging"/> (test-harness guard)
///
/// These are pure unit tests — no network, no DB, no filesystem.
/// </summary>
public class CourtEnvironmentSafetyTests
{
    // ─── CourtEnvironmentParser ───────────────────────────────────────

    [Theory]
    [InlineData("Staging", CourtEnvironment.Staging)]
    [InlineData("staging", CourtEnvironment.Staging)]
    [InlineData("STAGING", CourtEnvironment.Staging)]
    [InlineData("  Staging  ", CourtEnvironment.Staging)]
    [InlineData("Stage", CourtEnvironment.Staging)]
    [InlineData("UAT", CourtEnvironment.Staging)]
    [InlineData("uat", CourtEnvironment.Staging)]
    [InlineData("Test", CourtEnvironment.Staging)]
    [InlineData("Production", CourtEnvironment.Production)]
    [InlineData("production", CourtEnvironment.Production)]
    [InlineData("PRODUCTION", CourtEnvironment.Production)]
    [InlineData("Prod", CourtEnvironment.Production)]
    [InlineData("Live", CourtEnvironment.Production)]
    [InlineData("", CourtEnvironment.Unknown)]
    [InlineData("   ", CourtEnvironment.Unknown)]
    [InlineData(null, CourtEnvironment.Unknown)]
    [InlineData("Dev", CourtEnvironment.Unknown)]
    [InlineData("Sandbox", CourtEnvironment.Unknown)]
    [InlineData("Prodution", CourtEnvironment.Unknown)] // typo — must NOT map to Production
    [InlineData("Stag", CourtEnvironment.Unknown)]       // partial — must NOT map to Staging
    public void Parser_MapsLabelsCorrectly(string? input, CourtEnvironment expected)
    {
        Assert.Equal(expected, CourtEnvironmentParser.Parse(input));
    }

    [Fact]
    public void Parser_CanonicalString_RoundTrips()
    {
        Assert.Equal(CourtEnvironment.Staging,
            CourtEnvironmentParser.Parse(CourtEnvironmentParser.ToCanonicalString(CourtEnvironment.Staging)));
        Assert.Equal(CourtEnvironment.Production,
            CourtEnvironmentParser.Parse(CourtEnvironmentParser.ToCanonicalString(CourtEnvironment.Production)));
    }

    // ─── JtiUrlConvention ─────────────────────────────────────────────

    [Theory]
    [InlineData("https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/FilingReview/", CourtEnvironment.Staging)]
    [InlineData("https://aux-pub-efm-madera-ca.ecourt.com/ws/rest/ecourt",               CourtEnvironment.Staging)]
    [InlineData("https://efm-madera-court-prod-pub.ecourt.com/sustain/ws/soap/niem/FilingReview", CourtEnvironment.Production)]
    [InlineData("https://efm-lasc-court-prod-pub.ecourt.com/ws/soap/niem/FilingReview", CourtEnvironment.Production)]
    [InlineData("https://example.com/unknown", CourtEnvironment.Unknown)]
    [InlineData("not-a-url",                   CourtEnvironment.Unknown)]
    [InlineData("",                            CourtEnvironment.Unknown)]
    [InlineData(null,                          CourtEnvironment.Unknown)]
    public void UrlConvention_InfersEnvironment(string? url, CourtEnvironment expected)
    {
        Assert.Equal(expected, JtiUrlConvention.InferFromUrl(url));
    }

    [Fact]
    public void UrlConvention_IsMismatch_DetectsCrossEnvironmentUrls()
    {
        Assert.True(JtiUrlConvention.IsMismatch(CourtEnvironment.Production,
            "https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/FilingReview/"));
        Assert.True(JtiUrlConvention.IsMismatch(CourtEnvironment.Staging,
            "https://efm-madera-court-prod-pub.ecourt.com/ws/soap/niem/FilingReview/"));
    }

    [Fact]
    public void UrlConvention_IsMismatch_ReturnsFalseWhenUnknownEitherSide()
    {
        Assert.False(JtiUrlConvention.IsMismatch(CourtEnvironment.Unknown,
            "https://aux-pub-efm-madera-ca.ecourt.com/"));
        Assert.False(JtiUrlConvention.IsMismatch(CourtEnvironment.Production,
            "https://example.com/some/path"));
    }

    // ─── CourtConfiguration helper properties ──────────────────────────

    [Fact]
    public void CourtConfiguration_StagingProperties_WorkAsExpected()
    {
        var c = new CourtConfiguration { Environment = "Staging" };
        Assert.True(c.IsStaging);
        Assert.False(c.IsProduction);
        Assert.False(c.IsUnknownEnvironment);
        Assert.True(c.AllowsDestructiveTests);
    }

    [Fact]
    public void CourtConfiguration_ProductionProperties_WorkAsExpected()
    {
        var c = new CourtConfiguration { Environment = "Production" };
        Assert.False(c.IsStaging);
        Assert.True(c.IsProduction);
        Assert.False(c.IsUnknownEnvironment);
        Assert.False(c.AllowsDestructiveTests);
    }

    [Fact]
    public void CourtConfiguration_EmptyEnvironment_IsUnknownAndUnsafe()
    {
        var c = new CourtConfiguration { Environment = "" };
        Assert.True(c.IsUnknownEnvironment);
        Assert.False(c.AllowsDestructiveTests);
    }

    [Fact]
    public void CourtConfiguration_RequireStaging_ThrowsForProduction()
    {
        var c = new CourtConfiguration { CourtId = "madera", Environment = "Production" };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            c.RequireStagingEnvironment("SubmitFiling test"));
        Assert.Contains("madera", ex.Message);
        Assert.Contains("SubmitFiling test", ex.Message);
        Assert.Contains("Staging", ex.Message);
    }

    [Fact]
    public void CourtConfiguration_RequireStaging_ThrowsForUnknown()
    {
        var c = new CourtConfiguration { CourtId = "madera", Environment = "Sandbox" };
        Assert.Throws<InvalidOperationException>(() => c.RequireStagingEnvironment());
    }

    [Fact]
    public void CourtConfiguration_RequireStaging_AllowsStaging()
    {
        var c = new CourtConfiguration { CourtId = "madera", Environment = "Staging" };
        c.RequireStagingEnvironment(); // no throw
    }

    [Fact]
    public void CourtConfiguration_RequireTestModeAllowed_ThrowsWhenProductionHasTestMode()
    {
        var c = new CourtConfiguration
        {
            CourtId = "madera",
            Environment = "Production",
            TestFilingMode = TestFilingMode.AutoAccept,
        };
        var ex = Assert.Throws<InvalidOperationException>(() => c.RequireTestModeAllowedForEnvironment());
        Assert.Contains("Production", ex.Message);
        Assert.Contains("AutoAccept", ex.Message);
    }

    [Fact]
    public void CourtConfiguration_RequireTestModeAllowed_OkWhenProductionIsNoneMode()
    {
        var c = new CourtConfiguration { Environment = "Production", TestFilingMode = TestFilingMode.None };
        c.RequireTestModeAllowedForEnvironment(); // no throw
    }

    [Fact]
    public void CourtConfiguration_RequireTestModeAllowed_OkWhenStagingHasAutoAccept()
    {
        var c = new CourtConfiguration { Environment = "Staging", TestFilingMode = TestFilingMode.AutoAccept };
        c.RequireTestModeAllowedForEnvironment(); // no throw
    }

    // ─── CourtConfigurationValidator ───────────────────────────────────

    [Fact]
    public void Validator_CleanStagingConfig_HasNoIssues()
    {
        var c = new CourtConfiguration
        {
            CourtId = "madera",
            Environment = "Staging",
            SoapEndpoint = "https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/FilingReview/",
            RestBaseUrl = "https://aux-pub-efm-madera-ca.ecourt.com/ws/rest/ecourt",
            TestFilingMode = TestFilingMode.AutoAccept,
        };
        var issues = CourtConfigurationValidator.Validate(c);
        Assert.Empty(issues);
        Assert.True(CourtConfigurationValidator.IsSafeToSave(c));
    }

    [Fact]
    public void Validator_CleanProductionConfig_HasNoIssues()
    {
        var c = new CourtConfiguration
        {
            CourtId = "madera",
            Environment = "Production",
            SoapEndpoint = "https://efm-madera-court-prod-pub.ecourt.com/sustain/ws/soap/niem/FilingReview",
            RestBaseUrl = "https://efm-madera-court-prod-pub.ecourt.com/sustain/ws/rest/ecourt",
            TestFilingMode = TestFilingMode.None,
        };
        var issues = CourtConfigurationValidator.Validate(c);
        Assert.Empty(issues);
        Assert.True(CourtConfigurationValidator.IsSafeToSave(c));
    }

    [Fact]
    public void Validator_ProductionWithTestMode_ReportsError()
    {
        var c = new CourtConfiguration
        {
            CourtId = "madera",
            Environment = "Production",
            SoapEndpoint = "https://efm-madera-court-prod-pub.ecourt.com/ws/soap/niem/FilingReview",
            TestFilingMode = TestFilingMode.AutoAccept,
        };
        var issues = CourtConfigurationValidator.Validate(c);
        Assert.Contains(issues, i =>
            i.Code == "PROD_TEST_MODE" && i.Severity == CourtConfigValidationSeverity.Error);
        Assert.False(CourtConfigurationValidator.IsSafeToSave(c));
    }

    [Fact]
    public void Validator_StagingLabelWithProductionUrl_ReportsMismatchWarning()
    {
        var c = new CourtConfiguration
        {
            CourtId = "madera",
            Environment = "Staging",
            SoapEndpoint = "https://efm-madera-court-prod-pub.ecourt.com/ws/soap/niem/FilingReview",
        };
        var issues = CourtConfigurationValidator.Validate(c);
        Assert.Contains(issues, i =>
            i.Code == "ENV_URL_MISMATCH" &&
            i.Severity == CourtConfigValidationSeverity.Warning &&
            i.Field == nameof(CourtConfiguration.SoapEndpoint));
        Assert.True(CourtConfigurationValidator.IsSafeToSave(c)); // warnings don't block save
    }

    [Fact]
    public void Validator_ProductionLabelWithStagingUrl_ReportsMismatchWarning()
    {
        var c = new CourtConfiguration
        {
            CourtId = "madera",
            Environment = "Production",
            SoapEndpoint = "https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/FilingReview/",
        };
        var issues = CourtConfigurationValidator.Validate(c);
        Assert.Contains(issues, i => i.Code == "ENV_URL_MISMATCH");
    }

    [Fact]
    public void Validator_EmptyEnvironment_ReportsUnknownWarning()
    {
        var c = new CourtConfiguration
        {
            CourtId = "madera",
            Environment = "",
        };
        var issues = CourtConfigurationValidator.Validate(c);
        Assert.Contains(issues, i =>
            i.Code == "ENV_UNKNOWN" && i.Severity == CourtConfigValidationSeverity.Warning);
    }

    [Fact]
    public void Validator_UnknownEnvironmentWithTestMode_ReportsBothWarnings()
    {
        var c = new CourtConfiguration
        {
            CourtId = "madera",
            Environment = "",
            TestFilingMode = TestFilingMode.AutoAccept,
        };
        var issues = CourtConfigurationValidator.Validate(c);
        Assert.Contains(issues, i => i.Code == "ENV_UNKNOWN");
        Assert.Contains(issues, i => i.Code == "UNKNOWN_ENV_TEST_MODE");
    }

    // ─── TestConfiguration guard wrapper ───────────────────────────────

    [Fact]
    public void TestConfiguration_RequireStaging_ThrowsForProduction()
    {
        var c = new CourtConfiguration { CourtId = "madera", Environment = "Production" };
        Assert.Throws<InvalidOperationException>(() =>
            TestConfiguration.RequireStaging(c, "SubmitFiling"));
    }

    [Fact]
    public void TestConfiguration_RequireStaging_AllowsStaging()
    {
        var c = new CourtConfiguration { CourtId = "madera", Environment = "Staging" };
        TestConfiguration.RequireStaging(c); // no throw
    }

    [Fact]
    public void TestConfiguration_GetSkipReasonIfNotStaging_ReturnsNullForStaging()
    {
        var c = new CourtConfiguration { CourtId = "madera", Environment = "Staging" };
        Assert.Null(TestConfiguration.GetSkipReasonIfNotStaging(c));
    }

    [Fact]
    public void TestConfiguration_GetSkipReasonIfNotStaging_ReturnsReasonForProduction()
    {
        var c = new CourtConfiguration { CourtId = "madera", Environment = "Production" };
        var reason = TestConfiguration.GetSkipReasonIfNotStaging(c);
        Assert.NotNull(reason);
        Assert.Contains("madera", reason);
        Assert.Contains("Production", reason);
    }
}
