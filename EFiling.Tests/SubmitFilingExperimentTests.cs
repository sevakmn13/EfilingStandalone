using EFiling.Core.Caching;
using EFiling.Core.Enums;
using EFiling.Core.Models;
using EFiling.Providers.JTI;
using EFiling.Providers.JTI.Builders;
using Xunit.Abstractions;

namespace EFiling.Tests;

/// <summary>
/// Experimental tests to attempt a LIVE ReviewFiling submission to Madera staging.
/// Purpose: discover what JTI accepts/rejects, understand payment model, and validate XML.
/// WARNING: These tests create REAL filings in the staging environment.
/// </summary>
[Trait("Category", "Experiment")]
public class SubmitFilingExperimentTests
{
    private readonly ITestOutputHelper _output;

    public SubmitFilingExperimentTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Build a submission from the REAL draft data in the DB (Draft Id=1).
    /// Draft: Family Law/Support > Legal Separation w/o Minor Child, Madera courthouse.
    /// Attorney: Felicia Espinosa (Bar# 267198). Parties: APLNT vs AGENCY.
    /// Lead doc: 218620. File: public PDF (draft had Azure blob, not available here).
    /// </summary>
    private static FilingSubmission BuildFromRealDraft()
    {
        var sub = new FilingSubmission
        {
            FilingType = FilingType.Initial,
            EfspReferenceId = $"EF{Guid.NewGuid():N}",
            SubmitterUsername = "legalhub",

            // From draft: Family Law/Support > Legal Separation w/o Minor Child
            CaseTypeCode = "211110",
            CaseCategoryCode = "212120",

            // Location
            LocationCode = "M",
            LocationName = "Madera Courthouse",
            IncidentZipCode = "93637",
        };

        // Attorney (from draft)
        var attorney = new FilingParty
        {
            ReferenceId = "attorney0",
            RoleCode = "ATT",
            FirstName = "Felicia",
            MiddleName = "A",
            LastName = "Espinosa",
            BarNumber = "267198",
            Contact = new ContactInfo
            {
                MailingAddress = new StructuredAddress
                {
                    AddressType = "ML",
                    Address1 = "2115",
                    Address2 = "Kern St",
                    City = "Fresno",
                    State = "CA",
                    Zip = "93721"
                },
                PhoneNumber = "5594418721",
                PhoneType = "UNK",
                Email = "test@mail.com"
            }
        };

        // Filing party (from draft)
        var filingParty = new FilingParty
        {
            ReferenceId = "filedBy0",
            RoleCode = "APLNT",
            FirstName = "first",
            LastName = "person",
            InterpreterLanguage = "116"
        };

        // Opposing party (from draft)
        var opposingParty = new FilingParty
        {
            ReferenceId = "filedAsTo0",
            RoleCode = "AGENCY",
            FirstName = "second",
            LastName = "person"
        };

        sub.Parties.Add(attorney);
        sub.Parties.Add(filingParty);
        sub.Parties.Add(opposingParty);

        // Attorney-party association (REPRESENTEDBY)
        sub.PartyAssociations.Add(new PartyAssociation
        {
            AssociationType = "REPRESENTEDBY",
            ParticipantRef = "filedBy0",
            RelatedParticipantRef = "attorney0"
        });

        // Lead document (from draft: code 218620)
        sub.LeadDocument = new FilingDocument
        {
            ReferenceId = "doc0",
            DocumentCode = "218620",
            FileControlId = $"doc-{Guid.NewGuid():N}",
            SequenceNumber = 0,
            // Public test PDF (draft had Azure blob — using public PDF instead)
            BinaryLocationUri = "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf"
        };

        // Party-document associations
        sub.PartyDocumentAssociations.Add(new PartyDocumentAssociation
        {
            AssociationType = "FILEDBY",
            ParticipantRef = "filedBy0",
            DocumentRef = "doc0"
        });
        sub.PartyDocumentAssociations.Add(new PartyDocumentAssociation
        {
            AssociationType = "REFERS_TO",
            ParticipantRef = "filedAsTo0",
            DocumentRef = "doc0"
        });

        // Payment: EFSP-handled billing (0/0/ACH)
        // All JTI sample XMLs use this pattern. ProfileId=0 means
        // "EFSP collects payment itself" — JTI doesn't charge anyone.
        sub.Payment = new FilingPayment
        {
            CustomerProfileId = "0",
            CustomerPaymentProfileId = "0",
            PaymentType = "ACH"
        };

        return sub;
    }

    /// <summary>
    /// Real Madera staging config with plaintext credentials (from seed data).
    /// Bypasses encrypted testsettings.json — no EFILING_PASSPHRASE needed.
    /// Environment = "Staging" is required so <see cref="TestConfiguration.RequireStaging"/>
    /// admits this config for live-submission tests (see plan doc §9.10).
    /// </summary>
    private static CourtConfiguration MaderaConfig => new()
    {
        CourtId = "madera",
        DisplayName = "Madera Superior Court",
        Environment = "Staging",
        SoapEndpoint = "https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/FilingReview/",
        CourtRecordEndpoint = "https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/CourtRecord/",
        RestBaseUrl = "https://aux-pub-efm-madera-ca.ecourt.com/ws/rest/ecourt",
        NfrcCallbackUrl = "https://staging.legalhub.com/api/efiling/nfrc",
        Username = "legalhub",
        Password = "[va<8<jC50Y0",
        IsActive = true
    };

    /// <summary>
    /// Test 1: Generate the XML and log it WITHOUT sending — inspect the envelope.
    /// No credentials needed.
    /// </summary>
    [Fact]
    public void GenerateReviewFilingXml_LogOutput()
    {
        var config = MaderaConfig;
        var submission = BuildFromRealDraft();

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, config);

        _output.WriteLine("=== GENERATED REVIEW FILING XML ===");
        _output.WriteLine(xml);
        _output.WriteLine($"\n=== XML Length: {xml.Length} chars ===");

        Assert.NotNull(xml);
        Assert.Contains("ReviewFilingRequestMessage", xml);
        Assert.Contains("CoreFilingMessage", xml);
        Assert.Contains("PaymentMessage", xml);
    }

    /// <summary>
    /// Test 2: Submit REAL draft data to Madera staging and see what comes back.
    /// Uses draft Id=1: Family Law > Legal Separation w/o Minor Child.
    /// </summary>
    [Fact]
    public async Task SubmitFiling_LiveMadera_RealDraft()
    {
        using var provider = new JtiEFilingProvider(new InMemoryEFilingCache());
        var config = MaderaConfig;

        // Safety guard: refuses to submit unless config is explicitly labelled Staging.
        // Per re-implementation plan §9.10, every Tier B live test wraps with this.
        // If MaderaConfig ever gets pointed at production by mistake, this throws
        // before any wire call is made.
        TestConfiguration.RequireStaging(config, nameof(SubmitFiling_LiveMadera_RealDraft));

        var submission = BuildFromRealDraft();

        // Log the generated XML first
        var requestXml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, config);
        _output.WriteLine("=== REQUEST XML ===");
        _output.WriteLine(requestXml);
        _output.WriteLine("");

        // Attempt the submission
        FilingResult result;
        try
        {
            result = await provider.SubmitFilingAsync(config, submission);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"=== EXCEPTION ===");
            _output.WriteLine($"Type: {ex.GetType().FullName}");
            _output.WriteLine($"Message: {ex.Message}");
            if (ex is Providers.JTI.Soap.JtiSoapException soapEx)
            {
                _output.WriteLine($"HTTP Status: {soapEx.HttpStatusCode}");
                _output.WriteLine($"Response Body:\n{soapEx.ResponseBody}");
            }
            _output.WriteLine($"Stack: {ex.StackTrace}");
            throw; // Re-throw to fail the test
        }

        // Log the full result
        _output.WriteLine("=== RESULT ===");
        _output.WriteLine($"Success: {result.Success}");
        _output.WriteLine($"EfmReferenceId: {result.EfmReferenceId ?? "(null)"}");
        _output.WriteLine($"EfspReferenceId: {result.EfspReferenceId ?? "(null)"}");
        _output.WriteLine($"Status: {result.Status}");
        _output.WriteLine($"ErrorCode: {result.ErrorCode}");
        _output.WriteLine($"ErrorText: {result.ErrorText ?? "(null)"}");
        _output.WriteLine("");
        _output.WriteLine("=== RAW RESPONSE XML ===");
        _output.WriteLine(result.RawXml ?? "(null)");
    }

    /// <summary>
    /// Test 3: Generate the XML from real draft data without sending — inspect it.
    /// </summary>
    [Fact]
    public void GenerateXml_FromRealDraft_LogOutput()
    {
        var config = MaderaConfig;
        var submission = BuildFromRealDraft();

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, config);

        _output.WriteLine("=== GENERATED REVIEW FILING XML (REAL DRAFT) ===");
        _output.WriteLine(xml);
        _output.WriteLine($"\n=== XML Length: {xml.Length} chars ===");

        Assert.NotNull(xml);
        Assert.Contains("211110", xml);  // case type
        Assert.Contains("218620", xml);  // doc code
        Assert.Contains("APLNT", xml);   // party role
    }
}
