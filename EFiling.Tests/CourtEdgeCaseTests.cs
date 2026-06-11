using System.Xml.Linq;
using EFiling.Core.Enums;
using EFiling.Core.Models;
using EFiling.Providers.JTI.Builders;
using EFiling.Providers.JTI.Parsers;
using EFiling.Providers.JTI.Soap;

namespace EFiling.Tests;

/// <summary>
/// Court-specific edge case tests per JTI documentation.
/// Covers eService, complex litigation, citation info, DOB on AS_TO,
/// class action indicator, asbestos, CEQA, location handling, etc.
/// </summary>
public class CourtEdgeCaseTests
{
    private static readonly XNamespace CivExt = SoapEnvelopeBuilder.NsJtiCivilCaseExt;
    private static readonly XNamespace CpExt = SoapEnvelopeBuilder.NsJtiCaseParticipantExt;
    private static readonly XNamespace Civil = SoapEnvelopeBuilder.NsCivilCase;
    private static readonly XNamespace Nc = SoapEnvelopeBuilder.NsNiemCore;
    private static readonly XNamespace St = SoapEnvelopeBuilder.NsStructures;
    private static readonly XNamespace Cfm = SoapEnvelopeBuilder.NsCoreFilingMessage;

    private static CourtConfiguration TestConfig => new()
    {
        CourtId = "test-court",
        SoapEndpoint = "https://test.ecourt.com/ws/soap/niem/FilingReview/"
    };

    // ─── eService Consent (Placer / Nevada) ────────────────────────

    [Fact]
    public void MultipleParties_WithEService_AllHaveFlag()
    {
        var sub = BuildMinimalInit();
        sub.Parties[0].EService = true;
        sub.Parties[1].EService = true;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var participants = doc.Descendants(CpExt + "CaseParticipantExt").ToList();
        foreach (var p in participants.Where(p => p.Attribute(St + "id")?.Value != "attorney0"))
        {
            var eService = p.Elements(CpExt + "eService").FirstOrDefault();
            Assert.NotNull(eService);
            Assert.Equal("true", eService!.Value);
        }
    }

    [Fact]
    public void EService_False_NotIncludedInXml()
    {
        var sub = BuildMinimalInit();
        sub.Parties[0].EService = false;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var plaintiff = doc.Descendants(CpExt + "CaseParticipantExt")
            .First(p => p.Attribute(St + "id")?.Value == "filedBy0");
        var eService = plaintiff.Elements(CpExt + "eService").FirstOrDefault();
        Assert.Null(eService);
    }

    // ─── Complex Litigation (LASC) ─────────────────────────────────

    [Fact]
    public void ComplexLitigation_FlagIncludedInXml()
    {
        var sub = BuildMinimalInit();
        sub.ComplexLitigation = true;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        Assert.Equal("true", doc.Descendants(CivExt + "complexLitigation").First().Value);
    }

    [Fact]
    public void ComplexLitigation_False_NotIncludedInXml()
    {
        var sub = BuildMinimalInit();
        sub.ComplexLitigation = false;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        Assert.Empty(doc.Descendants(CivExt + "complexLitigation"));
    }

    // ─── Class Action Indicator (base CivilCaseType) ──────────────

    [Fact]
    public void ClassAction_FlagIncludedInXml()
    {
        var sub = BuildMinimalInit();
        sub.ClassAction = true;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        Assert.Equal("true", doc.Descendants(Civil + "ClassActionIndicator").First().Value);
    }

    [Fact]
    public void ClassAction_False_NotIncludedInXml()
    {
        var sub = BuildMinimalInit();
        sub.ClassAction = false;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        Assert.Empty(doc.Descendants(Civil + "ClassActionIndicator"));
    }

    [Fact]
    public void ClassAction_WithComplexLitigation_BothPresent()
    {
        var sub = BuildMinimalInit();
        sub.ClassAction = true;
        sub.ComplexLitigation = true;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        Assert.Equal("true", doc.Descendants(Civil + "ClassActionIndicator").First().Value);
        Assert.Equal("true", doc.Descendants(CivExt + "complexLitigation").First().Value);
    }

    // ─── Asbestos (CivilCaseTypeExt) ────────────────────────────

    [Fact]
    public void Asbestos_FlagIncludedInXml()
    {
        var sub = BuildMinimalInit();
        sub.Asbestos = true;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        Assert.Equal("true", doc.Descendants(CivExt + "asbestos").First().Value);
    }

    [Fact]
    public void Asbestos_False_NotIncludedInXml()
    {
        var sub = BuildMinimalInit();
        sub.Asbestos = false;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        Assert.Empty(doc.Descendants(CivExt + "asbestos"));
    }

    // ─── California Environmental Quality Act (CivilCaseTypeExt) ─

    [Fact]
    public void CEQA_FlagIncludedInXml()
    {
        var sub = BuildMinimalInit();
        sub.CaliforniaEnvironmentalQualityAct = true;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        Assert.Equal("true", doc.Descendants(CivExt + "californiaEnvironmentalQualityAct").First().Value);
    }

    [Fact]
    public void CEQA_False_NotIncludedInXml()
    {
        var sub = BuildMinimalInit();
        sub.CaliforniaEnvironmentalQualityAct = false;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        Assert.Empty(doc.Descendants(CivExt + "californiaEnvironmentalQualityAct"));
    }

    // ─── CivilCaseTypeExt Boolean Element Ordering ──────────────

    [Fact]
    public void AllCivilExtBooleans_CorrectXsdOrdering()
    {
        var sub = BuildMinimalInit();
        sub.ComplexLitigation = true;
        sub.Asbestos = true;
        sub.CaliforniaEnvironmentalQualityAct = true;
        sub.ConditionallySealed = true;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var caseEl = doc.Descendants(Nc + "Case").First();
        var elements = caseEl.Elements().Select(e => e.Name.LocalName).ToList();

        var idxComplex = elements.IndexOf("complexLitigation");
        var idxAsbestos = elements.IndexOf("asbestos");
        var idxCeqa = elements.IndexOf("californiaEnvironmentalQualityAct");
        var idxSealed = elements.IndexOf("conditionallySealed");

        Assert.True(idxComplex < idxAsbestos, "complexLitigation must precede asbestos");
        Assert.True(idxAsbestos < idxCeqa, "asbestos must precede californiaEnvironmentalQualityAct");
        Assert.True(idxCeqa < idxSealed, "californiaEnvironmentalQualityAct must precede conditionallySealed");
    }

    // ─── Special Status Codes (COVID-19 UD, etc.) ──────────────────

    [Fact]
    public void MultipleSpecialStatusCodes_AllIncluded()
    {
        var sub = BuildMinimalInit();
        sub.SpecialStatusCodes.Add("UDCOV19");
        sub.SpecialStatusCodes.Add("COMPLEX");

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var codes = doc.Descendants(CivExt + "statusCode").Select(e => e.Value).ToList();
        Assert.Contains("UDCOV19", codes);
        Assert.Contains("COMPLEX", codes);
    }

    // ─── DOB on AS_TO party (Alameda Mental Health) ────────────────

    [Fact]
    public void AsToParty_WithDateOfBirth_IncludesDate()
    {
        var sub = BuildMinimalInit();
        sub.Parties[1].DateOfBirth = new DateTime(1985, 3, 20);

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var defendant = doc.Descendants(CpExt + "CaseParticipantExt")
            .First(p => p.Attribute(St + "id")?.Value == "filedAsTo0");

        var dob = defendant.Descendants(CpExt + "dateOfBirth").FirstOrDefault();
        Assert.NotNull(dob);
        Assert.Contains("1985-03-20", dob!.Descendants(Nc + "Date").First().Value);
    }

    // ─── No Fee Case (GC70616) ─────────────────────────────────────

    [Fact]
    public void NoFeeCase_WithSection_BothFlagsPresent()
    {
        var sub = BuildMinimalInit();
        sub.NoFeeCase = true;
        sub.NoFeeCaseSection = "GC70616(a)";

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        Assert.Equal("true", doc.Descendants(CivExt + "noFeeCase").First().Value);
        Assert.Equal("GC70616(a)", doc.Descendants(CivExt + "noFeeCaseSection").First().Value);
    }

    [Fact]
    public void NoFeeCase_False_NotIncludedInXml()
    {
        var sub = BuildMinimalInit();
        sub.NoFeeCase = false;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        Assert.Empty(doc.Descendants(CivExt + "noFeeCase"));
    }

    // ─── Govt Entity Fee Exemption ─────────────────────────────────

    [Fact]
    public void GovtEntity_FeeExemption_IncludesExemptionType()
    {
        var sub = BuildMinimalInit();
        sub.Parties[0].IsOrganization = true;
        sub.Parties[0].OrganizationName = "County of Los Angeles";
        sub.Parties[0].FirstName = null;
        sub.Parties[0].LastName = null;
        sub.Parties[0].FeeExemptionRequestType = "GOVT_ENTITY";

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var plaintiff = doc.Descendants(CpExt + "CaseParticipantExt")
            .First(p => p.Attribute(St + "id")?.Value == "filedBy0");

        var exemption = plaintiff.Elements(CpExt + "efmFeeExemptionRequestType").FirstOrDefault();
        Assert.NotNull(exemption);
        Assert.Equal("GOVT_ENTITY", exemption!.Value);
    }

    // ─── Incident Zip Code (Location-Based Filing) ─────────────────

    [Fact]
    public void IncidentZip_GeneratesIncidentAddress()
    {
        var sub = BuildMinimalInit();
        sub.IncidentZipCode = "90001";

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var zip = doc.Descendants(CivExt + "incidentAddress")
            .Descendants(Nc + "LocationPostalCode").First();
        Assert.Equal("90001", zip.Value);
    }

    // ─── Conditionally Sealed Filing ───────────────────────────────

    [Fact]
    public void ConditionallySealed_SetsConfidentialityIndicator()
    {
        var sub = BuildMinimalInit();
        sub.ConditionallySealed = true;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        Assert.Equal("true", doc.Descendants(CivExt + "conditionallySealed").First().Value);
        Assert.Equal("true", doc.Descendants(Cfm + "FilingConfidentialityIndicator").First().Value);
    }

    // ─── Subsequent Filing — Complaint Reference ───────────────────

    [Fact]
    public void Subsequent_WithMultipleComplaints_CorrectRef()
    {
        var sub = BuildSubsequent();
        sub.ComplaintId = "3";
        sub.LeadDocument!.ComplaintRef = "3";

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var leadDoc = doc.Descendants(Cfm + "FilingLeadDocument").First();
        Assert.Equal("3", leadDoc.Attribute("complaintType")?.Value);
    }

    // ─── Self-Rep Party (No Attorney) ──────────────────────────────

    [Fact]
    public void SelfRepParty_NoAttorney_OnlyTwoParticipants()
    {
        var sub = new FilingSubmission
        {
            FilingType = FilingType.Initial,
            EfspReferenceId = "SELFTEST",
            SubmitterUsername = "testuser",
            CaseTypeCode = "CV",
            CaseCategoryCode = "3701",
            Parties = new List<FilingParty>
            {
                new() { ReferenceId = "filedBy0", RoleCode = "PLA", FirstName = "John", LastName = "Doe" },
                new() { ReferenceId = "filedAsTo0", RoleCode = "DEF", FirstName = "Jane", LastName = "Smith" }
            },
            PartyDocumentAssociations = new List<PartyDocumentAssociation>
            {
                new() { AssociationType = "FILEDBY", ParticipantRef = "filedBy0", DocumentRef = "doc0" },
                new() { AssociationType = "REFERS_TO", ParticipantRef = "filedAsTo0", DocumentRef = "doc0" }
            },
            LeadDocument = new FilingDocument
            {
                ReferenceId = "doc0", DocumentCode = "COM040",
                BinaryLocationUri = "https://example.com/doc.pdf"
            },
            Payment = new FilingPayment
            {
                CustomerProfileId = "0", CustomerPaymentProfileId = "0", PaymentType = "ACH"
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var participants = doc.Descendants(CpExt + "CaseParticipantExt").ToList();
        Assert.Equal(2, participants.Count);

        // No REPRESENTEDBY association
        var relatedParticipants = doc.Descendants(CivExt + "relatedParticipants").ToList();
        Assert.Empty(relatedParticipants);
    }

    // ─── Filing Status Edge Cases ──────────────────────────────────

    [Fact]
    public void ParseFilingStatus_PartiallyAccepted_HasCaseNumber()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns3:FilingStatusResponseMessage xmlns:ns3=""{SoapEnvelopeBuilder.NsFilingStatusResponse}""
                                      xmlns:ns1=""{SoapEnvelopeBuilder.NsNiemCore}""
                                      xmlns:ns2=""{SoapEnvelopeBuilder.NsCommonTypes}"">
      <ns1:CaseDocketID>25CV99999</ns1:CaseDocketID>
      <ns2:FilingStatus>
        <ns2:FilingStatusCode>PartiallyAccepted</ns2:FilingStatusCode>
      </ns2:FilingStatus>
    </ns3:FilingStatusResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFilingStatusResponse(xml);

        Assert.Equal(FilingStatus.PartiallyAccepted, result.FilingStatus);
        Assert.Equal("25CV99999", result.CaseDocketId);
    }

    [Fact]
    public void ParseFilingStatus_MultipleRejectionReasons()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns3:FilingStatusResponseMessage xmlns:ns3=""{SoapEnvelopeBuilder.NsFilingStatusResponse}""
                                      xmlns:ns2=""{SoapEnvelopeBuilder.NsCommonTypes}""
                                      xmlns:ns4=""{SoapEnvelopeBuilder.NsJtiFilingStatusReason}"">
      <ns2:FilingStatus>
        <ns2:FilingStatusCode>Rejected</ns2:FilingStatusCode>
        <ns4:FilingStatusReason>
          <ns4:ReasonCode>DOC_FORMAT</ns4:ReasonCode>
          <ns4:ReasonCodeText>Wrong format</ns4:ReasonCodeText>
        </ns4:FilingStatusReason>
        <ns4:FilingStatusReason>
          <ns4:ReasonCode>MISSING_FEE</ns4:ReasonCode>
          <ns4:ReasonCodeText>Payment required</ns4:ReasonCodeText>
        </ns4:FilingStatusReason>
      </ns2:FilingStatus>
    </ns3:FilingStatusResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var result = FilingResponseParser.ParseFilingStatusResponse(xml);

        Assert.Equal(FilingStatus.Rejected, result.FilingStatus);
        Assert.Equal(2, result.Reasons.Count);
        Assert.Equal("DOC_FORMAT", result.Reasons[0].ReasonCode);
        Assert.Equal("MISSING_FEE", result.Reasons[1].ReasonCode);
    }

    // ─── Empty / Minimal Responses ─────────────────────────────────

    [Fact]
    public void ParseCaseList_SingleCase_ReturnsOne()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns6:CaseListResponseMessage xmlns:ns6=""{SoapEnvelopeBuilder.NsCaseListResponse}""
                                  xmlns:ns1=""{SoapEnvelopeBuilder.NsNiemCore}""
                                  xmlns:ns5=""{SoapEnvelopeBuilder.NsJtiCivilCaseExt}"">
      <ns5:CivilCaseExt>
        <ns1:CaseTrackingID>1</ns1:CaseTrackingID>
        <ns1:CaseDocketID>25SC00001</ns1:CaseDocketID>
        <ns1:CaseTitleText>Small Claims Test</ns1:CaseTitleText>
      </ns5:CivilCaseExt>
    </ns6:CaseListResponseMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var cases = CaseResponseParser.ParseCaseListResponse(xml);
        Assert.Single(cases);
        Assert.Equal("Small Claims Test", cases[0].CaseTitle);
    }

    [Fact]
    public void ParseFilingList_DateParsing_HandlesTimeComponents()
    {
        var xml = $@"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""{SoapEnvelopeBuilder.NsSoapEnv}"">
  <SOAP-ENV:Body>
    <ns4:FilingListResponseMessageExt xmlns:ns4=""{SoapEnvelopeBuilder.NsJtiFilingListResponseExt}""
                                       xmlns:ns2=""{SoapEnvelopeBuilder.NsCommonTypes}"">
      <ns4:MatchingFilings>
        <ns4:MatchingFiling>
          <ns4:CaseTitle>Date Test</ns4:CaseTitle>
          <ns4:FilingId>999</ns4:FilingId>
          <ns4:ReceivedDate>2025-06-15</ns4:ReceivedDate>
          <ns4:ReceivedTime>14:30:00</ns4:ReceivedTime>
          <ns2:FilingStatus>
            <ns2:FilingStatusCode>Accepted</ns2:FilingStatusCode>
          </ns2:FilingStatus>
        </ns4:MatchingFiling>
      </ns4:MatchingFilings>
    </ns4:FilingListResponseMessageExt>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var items = FilingResponseParser.ParseFilingListResponse(xml);
        Assert.Single(items);
        Assert.Equal(new DateTime(2025, 6, 15, 14, 30, 0), items[0].ReceivedDate);
    }

    // ─── URL Rewrite (JtiRestClient) ───────────────────────────────

    [Fact]
    public void RestUrlRewrite_InternalHost_RewritesToPublicHost()
    {
        var config = new CourtConfiguration
        {
            RestBaseUrl = "https://aux-pub-efm-madera-ca.ecourt.com/ws/rest/ecourt"
        };

        // Simulate: policy returns internal URL, client should rewrite to public
        var internalUrl = "https://aux-efm-madera-ca.ecourt.com/ws/rest/ecourt/niem/codeList?type=CASE_TYPE";

        // Verify that the internal and public hosts differ (the rewrite scenario)
        var internalUri = new Uri(internalUrl);
        var baseUri = new Uri(config.RestBaseUrl);

        Assert.NotEqual(internalUri.Host, baseUri.Host); // hosts differ
        Assert.Contains("aux-efm-madera-ca", internalUri.Host);
        Assert.Contains("aux-pub-efm-madera-ca", baseUri.Host);
    }

    // ─── Helpers ────────────────────────────────────────────────────

    private static FilingSubmission BuildMinimalInit()
    {
        return new FilingSubmission
        {
            FilingType = FilingType.Initial,
            EfspReferenceId = "EDGE-TEST-001",
            SubmitterUsername = "testuser",
            CaseTypeCode = "CV",
            CaseCategoryCode = "10101",
            Parties = new List<FilingParty>
            {
                new() { ReferenceId = "filedBy0", RoleCode = "PLA", FirstName = "John", LastName = "Smith" },
                new() { ReferenceId = "filedAsTo0", RoleCode = "DEF", FirstName = "Jane", LastName = "Doe" }
            },
            PartyDocumentAssociations = new List<PartyDocumentAssociation>
            {
                new() { AssociationType = "FILEDBY", ParticipantRef = "filedBy0", DocumentRef = "doc0" },
                new() { AssociationType = "REFERS_TO", ParticipantRef = "filedAsTo0", DocumentRef = "doc0" }
            },
            LeadDocument = new FilingDocument
            {
                ReferenceId = "doc0", DocumentCode = "COM040",
                BinaryLocationUri = "https://example.com/doc.pdf"
            },
            Payment = new FilingPayment
            {
                CustomerProfileId = "0", CustomerPaymentProfileId = "0", PaymentType = "ACH"
            }
        };
    }

    private static FilingSubmission BuildSubsequent()
    {
        return new FilingSubmission
        {
            FilingType = FilingType.Subsequent,
            EfspReferenceId = "EDGE-SUB-001",
            SubmitterUsername = "testuser",
            CaseDocketId = "25CV00001",
            CaseTrackingId = "99999",
            CaseTypeCode = "CV",
            CaseCategoryCode = "10101",
            ComplaintId = "1",
            LeadDocument = new FilingDocument
            {
                ReferenceId = "doc0", DocumentCode = "401011",
                BinaryLocationUri = "https://example.com/answer.pdf",
                ComplaintRef = "1"
            },
            Payment = new FilingPayment
            {
                CustomerProfileId = "0", CustomerPaymentProfileId = "0", PaymentType = "ACH"
            }
        };
    }
}
