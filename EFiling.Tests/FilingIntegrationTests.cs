using EFiling.Core.Caching;
using EFiling.Core.Enums;
using EFiling.Core.Models;
using EFiling.Providers.JTI;
using EFiling.Tests.LiveMadera;

namespace EFiling.Tests;

/// <summary>
/// Integration tests for Phase 4 filing operations against live Madera staging.
/// These test fee calculation with real court data.
/// NOTE: SubmitFiling is NOT tested here to avoid creating real filings.
///
/// <para>
/// <b>Live-Madera gating:</b> All tests in this class issue real SOAP calls to Madera
/// staging. They use <see cref="LiveMaderaFactAttribute"/> so they skip unless the opt-in env var
/// is set. See <see cref="LiveMaderaOptIn"/>.
/// </para>
/// </summary>
[Trait("Category", "LiveMadera")]
public class FilingIntegrationTests
{
    private static FilingSubmission BuildMaderaCaseInitSubmission()
    {
        return new FilingSubmission
        {
            FilingType = FilingType.Initial,
            EfspReferenceId = $"TEST-{Guid.NewGuid():N}",
            SubmitterUsername = "legalhub",
            CaseTypeCode = "CV",
            CaseCategoryCode = "10101",
            Parties = new List<FilingParty>
            {
                new()
                {
                    ReferenceId = "filedBy0",
                    RoleCode = "PLA",
                    FirstName = "Test",
                    LastName = "Plaintiff"
                },
                new()
                {
                    ReferenceId = "filedAsTo0",
                    RoleCode = "DEF",
                    FirstName = "Test",
                    LastName = "Defendant"
                }
            },
            PartyDocumentAssociations = new List<PartyDocumentAssociation>
            {
                new() { AssociationType = "FILEDBY", ParticipantRef = "filedBy0", DocumentRef = "doc0" },
                new() { AssociationType = "REFERS_TO", ParticipantRef = "filedAsTo0", DocumentRef = "doc0" }
            },
            LeadDocument = new FilingDocument
            {
                ReferenceId = "doc0",
                DocumentCode = "170285",
                SequenceNumber = 0,
                BinaryLocationUri = "https://example.com/test.pdf"
            },
            Payment = new FilingPayment
            {
                CustomerProfileId = "0",
                CustomerPaymentProfileId = "0",
                PaymentType = "ACH"
            }
        };
    }

    [LiveMaderaFact]
    public async Task CalculateFeesAsync_LiveMadera_ReturnsResponse()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;
        var submission = BuildMaderaCaseInitSubmission();

        var result = await provider.CalculateFeesAsync(config, submission);

        // We expect either a valid fee calculation or an error with details
        // (the test uses placeholder doc codes so an error is acceptable)
        Assert.NotNull(result);
        Assert.NotNull(result.RawXml);

        // Log result for debugging
        if (result.ErrorCode != 0)
        {
            Console.WriteLine($"Fee calc error {result.ErrorCode}: {result.ErrorText}");
        }
        else
        {
            Console.WriteLine($"Total fees: ${result.TotalAmount}");
            foreach (var item in result.LineItems)
                Console.WriteLine($"  {item.AccountingCostCode}: ${item.Amount} - {item.Description}");
            if (!string.IsNullOrEmpty(result.ExemptionType))
                Console.WriteLine($"  Exemption: {result.ExemptionType}");
        }
    }

    [LiveMaderaFact]
    public async Task CalculateFeesAsync_LiveMadera_XmlIsWellFormed()
    {
        // Verify the XML we generate is at least parseable by the server
        // (even if the response is an error due to test data)
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;
        var submission = BuildMaderaCaseInitSubmission();

        var result = await provider.CalculateFeesAsync(config, submission);

        // The server responded (didn't reject our XML with HTTP error)
        Assert.NotNull(result.RawXml);
        Assert.True(result.RawXml!.Contains("Envelope"), "Response should be a SOAP envelope");
    }

    // ─── Phase 5 — GetFilingList ─────────────────────────────────

    [LiveMaderaFact]
    public async Task GetFilingListAsync_LiveMadera_ReturnsListOrError()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;
        var criteria = new FilingListCriteria
        {
            FromDate = DateTime.UtcNow.AddDays(-30),
            ToDate = DateTime.UtcNow
        };

        var items = await provider.GetFilingListAsync(config, criteria);

        // May return empty list if no filings, or a real list
        Assert.NotNull(items);
        Console.WriteLine($"GetFilingList returned {items.Count} items");
        foreach (var item in items.Take(5))
            Console.WriteLine($"  [{item.FilingId}] {item.CaseTitle} — {item.Status} ({item.ReceivedDate:yyyy-MM-dd})");
    }

    [LiveMaderaFact]
    public async Task GetFilingStatusAsync_LiveMadera_InvalidId_ReturnsResponse()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = TestConfiguration.Madera;

        // Use a fake ID — we expect an error but not a crash
        var result = await provider.GetFilingStatusAsync(config, efmReferenceId: "0");

        Assert.NotNull(result);
        Console.WriteLine($"Filing status: {result.FilingStatus}");
        if (result.EfmReferenceId != null)
            Console.WriteLine($"  EFM ID: {result.EfmReferenceId}");
    }
}
