using EFiling.Core.Caching;
using EFiling.Providers.JTI;

namespace EFiling.Tests;

/// <summary>
/// Integration tests that hit the live Madera staging endpoint.
/// These require network access and valid credentials in testsettings.json.
/// Mark with [Trait] so they can be filtered out in CI.
/// </summary>
[Trait("Category", "Integration")]
public class GetPolicyIntegrationTests
{

    [Fact]
    public async Task GetPolicyAsync_LiveMadera_ReturnsPolicyVersionId()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;

        var policy = await provider.GetPolicyAsync(config);

        Assert.True(policy.PolicyVersionId > 0, $"Expected PolicyVersionId > 0, got {policy.PolicyVersionId}");
        Assert.True(policy.PolicyLastUpdateDate > DateTime.MinValue);
    }

    [Fact]
    public async Task GetPolicyAsync_LiveMadera_ReturnsCodeListUrls()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;

        var policy = await provider.GetPolicyAsync(config);

        Assert.True(policy.CodeListUrls.Count >= 15, $"Expected >= 15 code lists, got {policy.CodeListUrls.Count}");
        Assert.True(policy.CodeListUrls.ContainsKey("CASE_TYPE"));
        Assert.True(policy.CodeListUrls.ContainsKey("CASE_CATEGORY"));
        Assert.True(policy.CodeListUrls.ContainsKey("PARTY_TYPE"));
    }

    [Fact]
    public async Task GetPolicyAsync_LiveMadera_ReturnsSpecialEndpoints()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;

        var policy = await provider.GetPolicyAsync(config);

        Assert.NotNull(policy.DocumentListUrl);
        Assert.NotNull(policy.CourtLocationsUrl);
        Assert.NotNull(policy.AttorneyListUrl);
    }

    [Fact]
    public async Task GetPolicyAsync_LiveMadera_CachesResult()
    {
        var cache = new InMemoryEFilingCache();
        using var provider = new JtiEFilingProvider(cache);
        var config = TestConfiguration.Madera;

        // First call — hits network
        var policy1 = await provider.GetPolicyAsync(config);

        // Second call — should come from cache (same object reference)
        var policy2 = await provider.GetPolicyAsync(config);

        Assert.Equal(policy1.PolicyVersionId, policy2.PolicyVersionId);
        Assert.Same(policy1, policy2); // Same reference = from cache
    }
}
