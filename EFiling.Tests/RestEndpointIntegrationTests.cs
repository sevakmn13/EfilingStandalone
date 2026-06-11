using EFiling.Core.Caching;
using EFiling.Providers.JTI;

namespace EFiling.Tests;

/// <summary>
/// Integration tests for Phase 3 REST endpoints against live Madera staging.
/// Credentials loaded from testsettings.json (gitignored).
/// </summary>
[Trait("Category", "Integration")]
public class RestEndpointIntegrationTests
{

    // ─── Code Lists ───────────────────────────────────────────────

    [Fact]
    public async Task GetCodeList_CaseType_ReturnsItems()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;

        var items = await provider.GetCodeListAsync(config, "CASE_TYPE");

        Assert.NotEmpty(items);
        Assert.Contains(items, i => i.Name.Contains("Civil", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, i => i.Name.Contains("Family", StringComparison.OrdinalIgnoreCase));

        // Should have relationships
        var civil = items.First(i => i.Name.Contains("Civil", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(civil.Relationships);
    }

    [Fact]
    public async Task GetCodeList_PartyType_ReturnsItems()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;

        var items = await provider.GetCodeListAsync(config, "PARTY_TYPE");

        Assert.NotEmpty(items);
        // Standard party roles should exist
        Assert.Contains(items, i => i.Code == "PET" || i.Code == "PLAIN" || i.Code == "DEF");
    }

    [Fact]
    public async Task GetCodeList_CaseCategory_ReturnsItemsWithRelationships()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;

        var items = await provider.GetCodeListAsync(config, "CASE_CATEGORY");

        Assert.NotEmpty(items);
        // Categories should have relationships back to CASE_TYPE
        var withRels = items.Where(i => i.Relationships.Count > 0).ToList();
        Assert.NotEmpty(withRels);
    }

    [Fact]
    public async Task GetCodeList_IsCached()
    {
        var cache = new InMemoryEFilingCache();
        using var provider = new JtiEFilingProvider(cache);
        var config = TestConfiguration.Madera;

        var items1 = await provider.GetCodeListAsync(config, "CASE_TYPE");
        var items2 = await provider.GetCodeListAsync(config, "CASE_TYPE");

        Assert.Same(items1, items2); // Same reference = from cache
    }

    // ─── Document List ────────────────────────────────────────────

    [Fact]
    public async Task GetDocumentList_ReturnsDocumentsWithMetadata()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;

        var docs = await provider.GetDocumentListAsync(config);

        Assert.NotEmpty(docs);
        Assert.True(docs.Count >= 10, $"Expected >= 10 documents, got {docs.Count}");

        // At least some documents should have metadata items
        var withMeta = docs.Where(d => d.MetadataItems.Count > 0).ToList();
        Assert.NotEmpty(withMeta);
    }

    [Fact]
    public async Task GetDocumentList_WithCaseType_FiltersResults()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;

        // Fetch all vs filtered by case type
        var all = await provider.GetDocumentListAsync(config);
        var civilOnly = await provider.GetDocumentListAsync(config, caseType: "421110");

        // Filtered should be fewer or equal
        Assert.True(civilOnly.Count <= all.Count,
            $"Filtered ({civilOnly.Count}) should be <= all ({all.Count})");
    }

    // ─── Court Locations ──────────────────────────────────────────

    [Fact]
    public async Task GetCourtLocations_ByZip_ReturnsLocations()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;

        // 93637 = Madera, CA
        var locations = await provider.GetCourtLocationsAsync(config, zipCode: "93637");

        Assert.NotEmpty(locations);
        Assert.Contains(locations, l => l.LocationName.Contains("Madera", StringComparison.OrdinalIgnoreCase));
    }

    // ─── Attorney Lookup ──────────────────────────────────────────

    [Fact]
    public async Task SearchAttorneysByName_JohnSmith_ReturnsResults()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;

        var attorneys = await provider.SearchAttorneysByNameAsync(config, "john", "smith");

        Assert.NotEmpty(attorneys);
        var first = attorneys[0];
        Assert.NotNull(first.BarNumber);
        Assert.NotNull(first.FirstName);
        Assert.NotNull(first.LastName);
    }

    [Fact]
    public async Task LookupAttorneyByBarNumber_Known_ReturnsAttorney()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;

        // First find an attorney by name to get a bar number
        var attorneys = await provider.SearchAttorneysByNameAsync(config, "john", "smith");
        Assert.NotEmpty(attorneys);

        var barNumber = attorneys[0].BarNumber!;
        var attorney = await provider.LookupAttorneyByBarNumberAsync(config, barNumber);

        Assert.NotNull(attorney);
        Assert.Equal(barNumber, attorney.BarNumber);
    }

    [Fact]
    public async Task LookupAttorneyByBarNumber_Unknown_ReturnsNull()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;

        var attorney = await provider.LookupAttorneyByBarNumberAsync(config, "999999999");

        Assert.Null(attorney);
    }
}
