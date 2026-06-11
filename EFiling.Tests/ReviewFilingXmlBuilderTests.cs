using System.Xml.Linq;
using EFiling.Core.Enums;
using EFiling.Core.Models;
using EFiling.Providers.JTI.Builders;
using EFiling.Providers.JTI.Parsers;
using EFiling.Providers.JTI.Soap;

namespace EFiling.Tests;

/// <summary>
/// Unit tests for ReviewFilingXmlBuilder — verifies XML output structure
/// matches the ECF 4.0 / JTI extension schema requirements.
/// </summary>
public class ReviewFilingXmlBuilderTests
{
    private static readonly XNamespace Env = SoapEnvelopeBuilder.NsSoapEnv;
    private static readonly XNamespace Nc = SoapEnvelopeBuilder.NsNiemCore;
    private static readonly XNamespace Ecf = SoapEnvelopeBuilder.NsCommonTypes;
    private static readonly XNamespace Cfm = SoapEnvelopeBuilder.NsCoreFilingMessage;
    private static readonly XNamespace CfmExt = SoapEnvelopeBuilder.NsJtiCoreFilingExt;
    private static readonly XNamespace CivExt = SoapEnvelopeBuilder.NsJtiCivilCaseExt;
    private static readonly XNamespace CpExt = SoapEnvelopeBuilder.NsJtiCaseParticipantExt;
    private static readonly XNamespace PayExt = SoapEnvelopeBuilder.NsJtiPaymentExt;
    private static readonly XNamespace Pay = SoapEnvelopeBuilder.NsPaymentMessage;
    private static readonly XNamespace St = SoapEnvelopeBuilder.NsStructures;
    private static readonly XNamespace Civil = SoapEnvelopeBuilder.NsCivilCase;
    private static readonly XNamespace Wsdl = SoapEnvelopeBuilder.NsWsdlProfile;
    private static readonly XNamespace Xsi = SoapEnvelopeBuilder.NsXsi;

    private static CourtConfiguration TestConfig => new()
    {
        CourtId = "madera",
        DisplayName = "Madera Superior Court",
        SoapEndpoint = "https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/FilingReview/",
        RestBaseUrl = "https://aux-pub-efm-madera-ca.ecourt.com/ws/rest/ecourt",
        NfrcCallbackUrl = "https://staging.legalhub.com/api/efiling/nfrc"
    };

    private static FilingSubmission BuildMinimalCaseInit()
    {
        return new FilingSubmission
        {
            FilingType = FilingType.Initial,
            EfspReferenceId = "TEST-REF-001",
            SubmitterUsername = "testuser",
            CaseTypeCode = "CV",
            CaseCategoryCode = "3701",
            JurisdictionalGroundsCode = "O10K",
            AmountInControversy = 15000m,
            LocationName = "MAD",
            Parties = new List<FilingParty>
            {
                new()
                {
                    ReferenceId = "filedBy0",
                    RoleCode = "PLAIN",
                    FirstName = "John",
                    LastName = "Smith"
                },
                new()
                {
                    ReferenceId = "filedAsTo0",
                    RoleCode = "DEF",
                    IsOrganization = true,
                    OrganizationName = "Acme Corp"
                },
                new()
                {
                    ReferenceId = "attorney0",
                    RoleCode = "ATT",
                    FirstName = "Jane",
                    LastName = "Doe",
                    BarNumber = "123456",
                    Contact = new ContactInfo
                    {
                        MailingAddress = new StructuredAddress
                        {
                            Address1 = "100 Main St",
                            City = "Madera",
                            State = "CA",
                            Zip = "93637",
                            Country = "US",
                            AddressType = "M"
                        },
                        PhoneNumber = "559-555-1234",
                        PhoneType = "W",
                        Email = "jane@lawfirm.com"
                    }
                }
            },
            PartyAssociations = new List<PartyAssociation>
            {
                new() { AssociationType = "REPRESENTEDBY", ParticipantRef = "filedBy0", RelatedParticipantRef = "attorney0" }
            },
            PartyDocumentAssociations = new List<PartyDocumentAssociation>
            {
                new() { AssociationType = "FILEDBY", ParticipantRef = "filedBy0", DocumentRef = "doc0" },
                new() { AssociationType = "REFERS_TO", ParticipantRef = "filedAsTo0", DocumentRef = "doc0" }
            },
            LeadDocument = new FilingDocument
            {
                ReferenceId = "doc0",
                DocumentCode = "COM040",
                SequenceNumber = 0,
                BinaryLocationUri = "https://example.com/docs/complaint.pdf",
                FileControlId = "FC001"
            },
            Payment = new FilingPayment
            {
                CustomerProfileId = "0",
                CustomerPaymentProfileId = "0",
                PaymentType = "ACH"
            }
        };
    }

    // ─── Envelope Structure Tests ────────────────────────────────────

    [Fact]
    public void BuildReviewFiling_HasCorrectSoapEnvelopeStructure()
    {
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(BuildMinimalCaseInit(), TestConfig);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Root);
        Assert.Equal("Envelope", doc.Root!.Name.LocalName);
        Assert.Equal(Env, doc.Root.Name.Namespace);

        var body = doc.Root.Element(Env + "Body");
        Assert.NotNull(body);

        var reviewMsg = body!.Element(Wsdl + "ReviewFilingRequestMessage");
        Assert.NotNull(reviewMsg);
    }

    [Fact]
    public void BuildReviewFiling_HasSendingMDELocationID()
    {
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(BuildMinimalCaseInit(), TestConfig);
        var doc = XDocument.Parse(xml);

        var sendingMde = doc.Descendants(Ecf + "SendingMDELocationID").FirstOrDefault();
        Assert.NotNull(sendingMde);

        var id = sendingMde!.Element(Nc + "IdentificationID");
        Assert.NotNull(id);
        Assert.Equal("https://staging.legalhub.com/api/efiling/nfrc", id!.Value);
    }

    // ─── CoreFilingMessage Tests ─────────────────────────────────────

    [Fact]
    public void BuildReviewFiling_CoreFilingMessage_HasCorrectXsiType()
    {
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(BuildMinimalCaseInit(), TestConfig);
        var doc = XDocument.Parse(xml);

        var cfm = doc.Descendants(Cfm + "CoreFilingMessage").FirstOrDefault();
        Assert.NotNull(cfm);

        var xsiType = cfm!.Attribute(Xsi + "type");
        Assert.NotNull(xsiType);
        Assert.Equal("ns9:CoreFilingMessageExtType", xsiType!.Value);
    }

    [Fact]
    public void BuildReviewFiling_HasDocumentFiledDateAndIdentification()
    {
        var sub = BuildMinimalCaseInit();
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var cfm = doc.Descendants(Cfm + "CoreFilingMessage").First();

        var filedDate = cfm.Element(Nc + "DocumentFiledDate");
        Assert.NotNull(filedDate);
        Assert.Equal("ref1", filedDate!.Attribute("id")?.Value);

        var docId = cfm.Element(Nc + "DocumentIdentification");
        Assert.NotNull(docId);
        Assert.Equal("TEST-REF-001", docId!.Element(Nc + "IdentificationID")?.Value);
    }

    [Fact]
    public void BuildReviewFiling_HasDocumentSubmitter()
    {
        var sub = BuildMinimalCaseInit();
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var cfm = doc.Descendants(Cfm + "CoreFilingMessage").First();
        var submitter = cfm.Element(Nc + "DocumentSubmitter");
        Assert.NotNull(submitter);

        var personName = submitter!.Descendants(Nc + "PersonFullName").FirstOrDefault();
        Assert.NotNull(personName);
        Assert.Equal("testuser", personName!.Value);
    }

    [Fact]
    public void BuildReviewFiling_HasEFilingCaseFilingType_Initial()
    {
        var sub = BuildMinimalCaseInit();
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var filingType = doc.Descendants(CfmExt + "eFilingCaseFilingType").FirstOrDefault();
        Assert.NotNull(filingType);
        Assert.Equal("INITIAL", filingType!.Value);
    }

    [Fact]
    public void BuildReviewFiling_SubsequentFiling_HasCorrectType()
    {
        var sub = BuildMinimalCaseInit();
        sub.FilingType = FilingType.Subsequent;
        sub.CaseDocketId = "CASE-123";

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var filingType = doc.Descendants(CfmExt + "eFilingCaseFilingType").FirstOrDefault();
        Assert.Equal("SUBSEQUENT", filingType!.Value);
    }

    // ─── Case Structure Tests ────────────────────────────────────────

    [Fact]
    public void BuildReviewFiling_Case_HasCivilCaseExtType()
    {
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(BuildMinimalCaseInit(), TestConfig);
        var doc = XDocument.Parse(xml);

        var caseEl = doc.Descendants(Nc + "Case").FirstOrDefault();
        Assert.NotNull(caseEl);

        var xsiType = caseEl!.Attribute(Xsi + "type");
        Assert.NotNull(xsiType);
        Assert.Equal("ns6:CivilCaseTypeExt", xsiType!.Value);
    }

    [Fact]
    public void BuildReviewFiling_Case_HasCaseCategoryAndTypeText()
    {
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(BuildMinimalCaseInit(), TestConfig);
        var doc = XDocument.Parse(xml);

        var caseEl = doc.Descendants(Nc + "Case").First();

        Assert.Equal("3701", caseEl.Element(Nc + "CaseCategoryText")?.Value);
        Assert.Equal("CV", caseEl.Element(CivExt + "CaseTypeText")?.Value);
    }

    [Fact]
    public void BuildReviewFiling_Case_HasAmountInControversy()
    {
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(BuildMinimalCaseInit(), TestConfig);
        var doc = XDocument.Parse(xml);

        var amount = doc.Descendants(Civil + "AmountInControversy").FirstOrDefault();
        Assert.NotNull(amount);
        Assert.Equal("15000", amount!.Value);
    }

    [Fact]
    public void BuildReviewFiling_Case_HasJurisdictionalGroundsCode()
    {
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(BuildMinimalCaseInit(), TestConfig);
        var doc = XDocument.Parse(xml);

        var jgc = doc.Descendants(Civil + "JurisdictionalGroundsCode").FirstOrDefault();
        Assert.NotNull(jgc);
        Assert.Equal("O10K", jgc!.Value);
    }

    // ─── Party Tests ─────────────────────────────────────────────────

    [Fact]
    public void BuildReviewFiling_HasAllParties()
    {
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(BuildMinimalCaseInit(), TestConfig);
        var doc = XDocument.Parse(xml);

        var participants = doc.Descendants(CpExt + "CaseParticipantExt").ToList();
        Assert.Equal(3, participants.Count);
    }

    [Fact]
    public void BuildReviewFiling_Plaintiff_HasCorrectStructure()
    {
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(BuildMinimalCaseInit(), TestConfig);
        var doc = XDocument.Parse(xml);

        var plaintiff = doc.Descendants(CpExt + "CaseParticipantExt")
            .First(p => p.Attribute(St + "id")?.Value == "filedBy0");

        Assert.Equal("PLAIN", plaintiff.Descendants(Ecf + "CaseParticipantRoleCode").First().Value);
        Assert.Equal("John", plaintiff.Descendants(Nc + "PersonGivenName").First().Value);
        Assert.Equal("Smith", plaintiff.Descendants(Nc + "PersonSurName").First().Value);
    }

    [Fact]
    public void BuildReviewFiling_OrgDefendant_HasOrganizationName()
    {
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(BuildMinimalCaseInit(), TestConfig);
        var doc = XDocument.Parse(xml);

        var defendant = doc.Descendants(CpExt + "CaseParticipantExt")
            .First(p => p.Attribute(St + "id")?.Value == "filedAsTo0");

        Assert.Equal("DEF", defendant.Descendants(Ecf + "CaseParticipantRoleCode").First().Value);
        Assert.Equal("Acme Corp", defendant.Descendants(Nc + "OrganizationName").First().Value);
    }

    [Fact]
    public void BuildReviewFiling_Attorney_HasBarNumberAndContact()
    {
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(BuildMinimalCaseInit(), TestConfig);
        var doc = XDocument.Parse(xml);

        var attorney = doc.Descendants(CpExt + "CaseParticipantExt")
            .First(p => p.Attribute(St + "id")?.Value == "attorney0");

        Assert.Equal("ATT", attorney.Descendants(Ecf + "CaseParticipantRoleCode").First().Value);
        Assert.Equal("123456", attorney.Descendants(Nc + "IdentificationID").First().Value);
        Assert.Equal("BAR", attorney.Descendants(Nc + "IdentificationCategoryText").First().Value);

        // Contact
        Assert.Equal("jane@lawfirm.com", attorney.Descendants(Nc + "ContactEmailID").First().Value);
        Assert.Equal("559-555-1234", attorney.Descendants(Nc + "TelephoneNumberFullID").First().Value);
    }

    // ─── Association Tests ───────────────────────────────────────────

    [Fact]
    public void BuildReviewFiling_HasRepresentedByAssociation()
    {
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(BuildMinimalCaseInit(), TestConfig);
        var doc = XDocument.Parse(xml);

        var relatedParticipants = doc.Descendants(CivExt + "relatedParticipants").ToList();
        Assert.Single(relatedParticipants);

        var assoc = relatedParticipants[0];
        Assert.Equal("REPRESENTEDBY", assoc.Element(CivExt + "associationType")?.Value);
        Assert.Equal("filedBy0", assoc.Element(CivExt + "participant")?.Attribute(St + "ref")?.Value);
        Assert.Equal("attorney0", assoc.Element(CivExt + "relatedParticipant")?.Attribute(St + "ref")?.Value);
    }

    [Fact]
    public void BuildReviewFiling_HasFiledByAndRefersToAssociations()
    {
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(BuildMinimalCaseInit(), TestConfig);
        var doc = XDocument.Parse(xml);

        var docAssocs = doc.Descendants(CivExt + "relatedParticipantDocuments").ToList();
        Assert.Equal(2, docAssocs.Count);

        var filedBy = docAssocs.First(a => a.Element(CivExt + "associationType")?.Value == "FILEDBY");
        Assert.Equal("filedBy0", filedBy.Element(CivExt + "participant")?.Attribute(St + "ref")?.Value);
        Assert.Equal("doc0", filedBy.Element(CivExt + "relatedDocument")?.Attribute(St + "ref")?.Value);

        var refersTo = docAssocs.First(a => a.Element(CivExt + "associationType")?.Value == "REFERS_TO");
        Assert.Equal("filedAsTo0", refersTo.Element(CivExt + "participant")?.Attribute(St + "ref")?.Value);
    }

    // ─── Document Tests ──────────────────────────────────────────────

    [Fact]
    public void BuildReviewFiling_HasLeadDocument()
    {
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(BuildMinimalCaseInit(), TestConfig);
        var doc = XDocument.Parse(xml);

        var leadDoc = doc.Descendants(Cfm + "FilingLeadDocument").FirstOrDefault();
        Assert.NotNull(leadDoc);

        Assert.Equal("doc0", leadDoc!.Attribute(St + "id")?.Value);
        Assert.Equal("ns9:DocumentExtType", leadDoc.Attribute(Xsi + "type")?.Value);
        Assert.Equal("COM040", leadDoc.Element(Nc + "DocumentDescriptionText")?.Value);
        Assert.Equal("https://example.com/docs/complaint.pdf",
            leadDoc.Descendants(Nc + "BinaryLocationURI").First().Value);
        Assert.Equal("application/pdf",
            leadDoc.Descendants(Nc + "BinaryFormatStandardName").First().Value);
        Assert.Equal("0", leadDoc.Element(Nc + "DocumentSequenceID")?.Value);
    }

    [Fact]
    public void BuildReviewFiling_WithConnectedDocuments()
    {
        var sub = BuildMinimalCaseInit();
        sub.ConnectedDocuments.Add(new FilingDocument
        {
            ReferenceId = "doc1",
            DocumentCode = "ISS030",
            SequenceNumber = 1,
            BinaryLocationUri = "https://example.com/docs/summons.pdf"
        });

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var connectedDocs = doc.Descendants(Cfm + "FilingConnectedDocument").ToList();
        Assert.Single(connectedDocs);
        Assert.Equal("doc1", connectedDocs[0].Attribute(St + "id")?.Value);
        Assert.Equal("ISS030", connectedDocs[0].Element(Nc + "DocumentDescriptionText")?.Value);
    }

    // ─── Payment Tests ───────────────────────────────────────────────

    [Fact]
    public void BuildReviewFiling_HasPaymentMessage()
    {
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(BuildMinimalCaseInit(), TestConfig);
        var doc = XDocument.Parse(xml);

        var payment = doc.Descendants(Pay + "PaymentMessage").FirstOrDefault();
        Assert.NotNull(payment);

        Assert.Equal("ns12:PaymentMessageTypeExt", payment!.Attribute(Xsi + "type")?.Value);

        var authInfo = payment.Descendants(PayExt + "paymentAuthorizationInfo").FirstOrDefault();
        Assert.NotNull(authInfo);
        Assert.Equal("0", authInfo!.Element(PayExt + "customerProfileId")?.Value);
        Assert.Equal("ACH", authInfo.Element(PayExt + "paymentType")?.Value);
    }

    // ─── Court-Specific Extension Tests ──────────────────────────────

    [Fact]
    public void BuildReviewFiling_ComplexLitigation_IncludesFlag()
    {
        var sub = BuildMinimalCaseInit();
        sub.ComplexLitigation = true;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var flag = doc.Descendants(CivExt + "complexLitigation").FirstOrDefault();
        Assert.NotNull(flag);
        Assert.Equal("true", flag!.Value);
    }

    [Fact]
    public void BuildReviewFiling_ConditionallySealed_IncludesFlag()
    {
        var sub = BuildMinimalCaseInit();
        sub.ConditionallySealed = true;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var flag = doc.Descendants(CivExt + "conditionallySealed").FirstOrDefault();
        Assert.NotNull(flag);
        Assert.Equal("true", flag!.Value);

        // Also check FilingConfidentialityIndicator
        var confIndicator = doc.Descendants(Cfm + "FilingConfidentialityIndicator").First();
        Assert.Equal("true", confIndicator.Value);
    }

    [Fact]
    public void BuildReviewFiling_SpecialStatusCodes_Included()
    {
        var sub = BuildMinimalCaseInit();
        sub.SpecialStatusCodes.Add("UDCOV19");

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var statusCode = doc.Descendants(CivExt + "statusCode").FirstOrDefault();
        Assert.NotNull(statusCode);
        Assert.Equal("UDCOV19", statusCode!.Value);
    }

    [Fact]
    public void BuildReviewFiling_IncidentZipCode_IncludesAddress()
    {
        var sub = BuildMinimalCaseInit();
        sub.IncidentZipCode = "93637";

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var incidentAddr = doc.Descendants(CivExt + "incidentAddress").FirstOrDefault();
        Assert.NotNull(incidentAddr);

        var zipCode = incidentAddr!.Descendants(Nc + "LocationPostalCode").FirstOrDefault();
        Assert.NotNull(zipCode);
        Assert.Equal("93637", zipCode!.Value);
    }

    [Fact]
    public void BuildReviewFiling_NoFeeCase_IncludesFlags()
    {
        var sub = BuildMinimalCaseInit();
        sub.NoFeeCase = true;
        sub.NoFeeCaseSection = "GC70616(a)";

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var noFee = doc.Descendants(CivExt + "noFeeCase").FirstOrDefault();
        Assert.NotNull(noFee);
        Assert.Equal("true", noFee!.Value);

        var section = doc.Descendants(CivExt + "noFeeCaseSection").FirstOrDefault();
        Assert.Equal("GC70616(a)", section!.Value);
    }

    // ─── FeesCalculation Builder Test ────────────────────────────────

    [Fact]
    public void BuildFeesCalculation_ProducesValidXml()
    {
        var sub = BuildMinimalCaseInit();
        var xml = ReviewFilingXmlBuilder.BuildFeesCalculationRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        Assert.NotNull(doc.Root);
        Assert.Equal("Envelope", doc.Root!.Name.LocalName);

        // Should contain CoreFilingMessage and PaymentMessage
        var cfm = doc.Descendants(Cfm + "CoreFilingMessage").FirstOrDefault();
        Assert.NotNull(cfm);

        var payment = doc.Descendants(Pay + "PaymentMessage").FirstOrDefault();
        Assert.NotNull(payment);
    }

    // ─── Party Extension Fields ──────────────────────────────────────

    [Fact]
    public void BuildReviewFiling_PartyWithEService_IncludesFlag()
    {
        var sub = BuildMinimalCaseInit();
        sub.Parties[0].EService = true;

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var plaintiff = doc.Descendants(CpExt + "CaseParticipantExt")
            .First(p => p.Attribute(St + "id")?.Value == "filedBy0");

        var eService = plaintiff.Elements(CpExt + "eService").FirstOrDefault();
        Assert.NotNull(eService);
        Assert.Equal("true", eService!.Value);
    }

    [Fact]
    public void BuildReviewFiling_PartyWithFeeWaiver_IncludesExemptionType()
    {
        var sub = BuildMinimalCaseInit();
        sub.Parties[0].FeeExemptionRequestType = "FEE_WAIVER";

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var plaintiff = doc.Descendants(CpExt + "CaseParticipantExt")
            .First(p => p.Attribute(St + "id")?.Value == "filedBy0");

        var exemption = plaintiff.Elements(CpExt + "efmFeeExemptionRequestType").First();
        Assert.Equal("FEE_WAIVER", exemption.Value);
    }

    [Fact]
    public void BuildReviewFiling_PartyWithDateOfBirth_IncludesDate()
    {
        var sub = BuildMinimalCaseInit();
        sub.Parties[0].DateOfBirth = new DateTime(1990, 5, 15);

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var dob = doc.Descendants(CpExt + "dateOfBirth").FirstOrDefault();
        Assert.NotNull(dob);
        Assert.Equal("1990-05-15", dob!.Descendants(Nc + "Date").First().Value);
    }

    // ─── Phase 6: Subsequent Filing Tests ─────────────────────────

    private static FilingSubmission BuildMinimalSubsequent()
    {
        return new FilingSubmission
        {
            FilingType = FilingType.Subsequent,
            EfspReferenceId = "TEST-SUB-001",
            SubmitterUsername = "testuser",
            CaseDocketId = "24CV00123",
            CaseTrackingId = "99999",
            CaseTypeCode = "CV",
            CaseCategoryCode = "3701",
            ComplaintId = "1",
            LeadDocument = new FilingDocument
            {
                ReferenceId = "doc0",
                DocumentCode = "401011",
                BinaryLocationUri = "https://files.example.com/answer.pdf",
                ComplaintRef = "1"
            },
            Payment = new FilingPayment
            {
                CustomerProfileId = "PROF1",
                CustomerPaymentProfileId = "PAY1",
                PaymentType = "CREDIT"
            }
        };
    }

    [Fact]
    public void BuildReviewFiling_Subsequent_HasCaseDocketIDAndTrackingID()
    {
        var sub = BuildMinimalSubsequent();
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var caseEl = doc.Descendants(Nc + "Case").First();
        var docketId = caseEl.Elements(Nc + "CaseDocketID").FirstOrDefault();
        var trackingId = caseEl.Elements(Nc + "CaseTrackingID").FirstOrDefault();

        Assert.NotNull(docketId);
        Assert.Equal("24CV00123", docketId!.Value);
        Assert.NotNull(trackingId);
        Assert.Equal("99999", trackingId!.Value);
    }

    [Fact]
    public void BuildReviewFiling_Subsequent_HasComplaintTypeAttribute()
    {
        var sub = BuildMinimalSubsequent();
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var leadDoc = doc.Descendants(Cfm + "FilingLeadDocument").First();
        var complaintAttr = leadDoc.Attribute("complaintType");

        Assert.NotNull(complaintAttr);
        Assert.Equal("1", complaintAttr!.Value);
    }

    [Fact]
    public void BuildReviewFiling_Subsequent_FilingTypeIsSUBSEQUENT()
    {
        var sub = BuildMinimalSubsequent();
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var filingType = doc.Descendants(CfmExt + "eFilingCaseFilingType").First();
        Assert.Equal("SUBSEQUENT", filingType.Value);
    }

    [Fact]
    public void BuildReviewFiling_Subsequent_DocumentWithMetadata_HasFilingMetaData()
    {
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var DfValue = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentValue);
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "FILED_BY",
                ClassType = "caseParticipant",
                SubType = "filed-by",
                ValueRestriction = "existing-data",
                IdReferences = new List<string> { "100" }
            },
            new()
            {
                Code = "AS_TO",
                ClassType = "caseParticipant",
                SubType = "refers-to",
                ValueRestriction = "existing-data",
                IdReferences = new List<string> { "101" }
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var metaItems = doc.Descendants(DfMeta + "documentFilingMetaDataItem").ToList();
        Assert.Equal(2, metaItems.Count);

        // NOTE: inner fields (code/classType/subType/valueRestriction) live in the
        // DocumentValue namespace per the JTI schema; only the wrapper elements
        // (documentFilingMetaDataItem, docValueMetaDataItem, idReferences, id) live in
        // the DocumentFilingMetaData namespace.
        var filedBy = metaItems[0];
        Assert.Equal("FILED_BY", filedBy.Descendants(DfValue + "code").First().Value);
        Assert.Equal("100", filedBy.Descendants(DfMeta + "id").First().Value);

        var asTo = metaItems[1];
        Assert.Equal("AS_TO", asTo.Descendants(DfValue + "code").First().Value);
        Assert.Equal("101", asTo.Descendants(DfMeta + "id").First().Value);
    }

    [Fact]
    public void BuildReviewFiling_Subsequent_ContactMetadata_HasContactValue()
    {
        // Wrapper in DfMeta; children in ContactValueNs per catalog §3.4 wire contract.
        // Previously asserted children in DfMeta (matched the pre-fix bug); updated to match the
        // post-fix wire-correct emission. Audit C-2 Bug B fix.
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var ContactValueNs = XNamespace.Get(SoapEnvelopeBuilder.NsJtiContactValue);
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "SELF_REP_ADDRESS",
                ClassType = "contact",
                ContactValue = new ContactValueData
                {
                    Address1 = "123 Main St",
                    City = "Madera",
                    State = "CA",
                    Zip = "93637",
                    Country = "US",
                    PhoneNumber = "5551234567",
                    Email = "test@example.com"
                }
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var contactVal = doc.Descendants(DfMeta + "contactValue").First();
        Assert.Equal("123 Main St", contactVal.Element(ContactValueNs + "address1")!.Value);
        Assert.Equal("Madera", contactVal.Element(ContactValueNs + "city")!.Value);
        Assert.Equal("CA", contactVal.Element(ContactValueNs + "state")!.Value);
        Assert.Equal("93637", contactVal.Element(ContactValueNs + "zip")!.Value);
        Assert.Equal("test@example.com", contactVal.Element(ContactValueNs + "email")!.Value);
    }

    [Fact]
    public void BuildReviewFiling_Subsequent_NewPartyMetadata_EmitsCaseParticipantValue_AuditH1()
    {
        // Audit H-1 fix: new-data caseParticipant with full identity (Name +
        // Role + Contact) emits <caseParticipantValue> with nested <ContactInformation>,
        // NOT a flat <contactValue> sibling. Verified against CIV-SUB-001 baseline shape.
        // Pre-fix (lines 780-809 of ReviewFilingXmlBuilder.cs), the builder dropped the
        // FirstName/LastName/RoleCode fields and only emitted <contactValue> — now corrected.
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var Ecf = XNamespace.Get(SoapEnvelopeBuilder.NsCommonTypes);
        var NcNs = XNamespace.Get("http://niem.gov/niem/niem-core/2.0");
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "NEW_FILED_BY",
                ClassType = "caseParticipant",
                ValueRestriction = "new-data",
                NewPartyValue = new FilingParty
                {
                    ReferenceId = "newParty0",
                    RoleCode = "PLAIN",
                    FirstName = "Jane",
                    LastName = "Doe",
                    Contact = new ContactInfo
                    {
                        MailingAddress = new StructuredAddress
                        {
                            Address1 = "456 Oak Ave",
                            City = "Fresno",
                            State = "CA",
                            Zip = "93721"
                        },
                        Email = "jane@example.com"
                    }
                }
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        // <caseParticipantValue> wrapper in DfMeta.
        var cpv = doc.Descendants(DfMeta + "caseParticipantValue").FirstOrDefault();
        Assert.True(cpv is not null, "Expected <caseParticipantValue> wrapper — H-1 fix missing.");

        // EntityPerson in ECF namespace (H-2 rule), name children in niem-core.
        var person = cpv!.Element(Ecf + "EntityPerson");
        Assert.True(person is not null, "Expected ECF-namespaced <EntityPerson> inside caseParticipantValue.");
        var personName = person!.Element(NcNs + "PersonName");
        Assert.Equal("Jane", personName!.Element(NcNs + "PersonGivenName")!.Value);
        Assert.Equal("Doe", personName.Element(NcNs + "PersonSurName")!.Value);

        // CaseParticipantRoleCode in ECF namespace.
        Assert.Equal("PLAIN", cpv.Element(Ecf + "CaseParticipantRoleCode")!.Value);

        // ContactInformation nested (niem-core), NOT a flat <contactValue> sibling.
        Assert.True(cpv.Element(NcNs + "ContactInformation") is not null,
            "Expected <ContactInformation> nested inside caseParticipantValue (H-1 wire contract).");
        Assert.Null(doc.Descendants(DfMeta + "contactValue").FirstOrDefault());
    }

    // ─── Audit C-2 Bug B regression gate ─────────────────────────────
    // Catalog §3.4 "Audit C-2 root cause (corrected)" — the builder must emit
    // <contactValue> children in the ContactValue namespace (ns10 on the wire,
    // urn:com.journaltech:ecourt:ecf:extension:ContactValue), NOT the DocumentFilingMetaData
    // namespace (ns9). The wrapper element itself stays in DfMeta. Tested against
    // the CIV-SUB-003 / FAM-SUB-005 baseline sample convention.

    [Fact]
    public void BuildReviewFiling_Subsequent_ContactClassType_ChildrenInContactValueNamespace_AuditC2_BugB()
    {
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var ContactValueNs = XNamespace.Get(SoapEnvelopeBuilder.NsJtiContactValue);
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "FILING_PARTY_ADDRESS",
                ClassType = "contact",
                ContactValue = new ContactValueData
                {
                    Address1 = "12223 Davis St.",
                    City = "Sacramento",
                    State = "CA",
                    Zip = "95818",
                    Country = "US",
                    PhoneType = "BUS",
                    Email = "filer@example.com",
                    AddressType = "BUS"
                }
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        // Wrapper must be in DfMeta (unchanged behavior)
        var contactVal = doc.Descendants(DfMeta + "contactValue").FirstOrDefault();
        Assert.True(contactVal is not null,
            "Expected <contactValue> wrapper in DocumentFilingMetaData namespace — missing entirely.");

        // §3.4 wire contract: all children must be in ContactValue namespace.
        string[] expectedFields = { "address1", "city", "state", "zip", "country", "telephoneType", "email", "addressType" };
        foreach (var fieldName in expectedFields)
        {
            var correctlyNamespaced = contactVal!.Element(ContactValueNs + fieldName);
            var wronglyNamespaced = contactVal.Element(DfMeta + fieldName);

            Assert.True(correctlyNamespaced is not null,
                $"§3.4 / audit C-2 Bug B: <{fieldName}> must be in ContactValue namespace "
                + $"(urn:com.journaltech:ecourt:ecf:extension:ContactValue) but is missing from the correct namespace. "
                + (wronglyNamespaced is not null
                    ? $"Found it in the WRONG namespace ({DfMeta.NamespaceName}) — this is the bug."
                    : "Not found in any namespace — possibly a different bug."));

            Assert.True(wronglyNamespaced is null,
                $"§3.4 / audit C-2 Bug B: <{fieldName}> leaked into DocumentFilingMetaData namespace. "
                + "Must only live in ContactValue namespace per wire contract.");
        }
    }

    [Fact]
    public void BuildReviewFiling_Subsequent_NewPartyContactOnlyLegacy_EmitsFlatContactValue_AuditC2_BugB()
    {
        // Audit C-2 Bug B regression gate updated for H-1 dispatch:
        // When NewPartyValue has ONLY Contact populated (no Name, no RoleCode), the builder
        // falls through to the legacy flat <contactValue> emission path. Children must be in
        // the ContactValue namespace (not DfMeta). This preserves backward compatibility with
        // callers that use NewPartyValue as a pure contact carrier, while the H-1-fixed path
        // (full identity → <caseParticipantValue>) is exercised by the sibling
        // BuildReviewFiling_Subsequent_NewPartyMetadata_EmitsCaseParticipantValue_AuditH1 test.
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var ContactValueNs = XNamespace.Get(SoapEnvelopeBuilder.NsJtiContactValue);
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "NEW_FILED_BY",
                ClassType = "caseParticipant",
                ValueRestriction = "new-data",
                NewPartyValue = new FilingParty
                {
                    // Identity fields INTENTIONALLY empty — selects the legacy contact-only
                    // path in the builder's H-1 dispatch (see ReviewFilingXmlBuilder.cs
                    // hasSubstantiveIdentity check).
                    Contact = new ContactInfo
                    {
                        MailingAddress = new StructuredAddress
                        {
                            Address1 = "456 Oak Ave",
                            City = "Fresno",
                            State = "CA",
                            Zip = "93721",
                            Country = "US",
                            AddressType = "HM"
                        },
                        Email = "jane@example.com"
                    }
                }
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var contactVal = doc.Descendants(DfMeta + "contactValue").FirstOrDefault();
        Assert.True(contactVal is not null,
            "Expected flat <contactValue> wrapper for caseParticipant new-data contact-only legacy path.");

        // Same wire contract applies: children in ContactValue namespace.
        string[] expectedFields = { "address1", "city", "state", "zip", "country", "email", "addressType" };
        foreach (var fieldName in expectedFields)
        {
            var correctlyNamespaced = contactVal!.Element(ContactValueNs + fieldName);
            var wronglyNamespaced = contactVal.Element(DfMeta + fieldName);

            Assert.True(correctlyNamespaced is not null,
                $"§3.4 / audit C-2 Bug B (new-party contact-only legacy): <{fieldName}> must be in ContactValue namespace "
                + (wronglyNamespaced is not null
                    ? $"but was emitted in DocumentFilingMetaData namespace instead."
                    : "but is missing entirely."));

            Assert.True(wronglyNamespaced is null,
                $"§3.4 / audit C-2 Bug B (new-party contact-only legacy): <{fieldName}> leaked into DocumentFilingMetaData namespace.");
        }
    }

    [Fact]
    public void BuildReviewFiling_Subsequent_NameExtension_Included()
    {
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.NameExtension = "(Amendment #1)";

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var nameExt = doc.Descendants(CfmExt + "nameExtension").FirstOrDefault();
        Assert.NotNull(nameExt);
        Assert.Equal("(Amendment #1)", nameExt!.Value);
    }

    [Fact]
    public void BuildReviewFiling_Subsequent_NoCaseDocketId_OmitsElement()
    {
        var sub = BuildMinimalCaseInit(); // initial filing, no CaseDocketId
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var caseEl = doc.Descendants(Nc + "Case").First();
        var docketId = caseEl.Elements(Nc + "CaseDocketID").FirstOrDefault();
        Assert.Null(docketId);
    }

    [Fact]
    public void BuildFeesCalculation_Subsequent_HasCaseDocketID()
    {
        var sub = BuildMinimalSubsequent();
        var xml = ReviewFilingXmlBuilder.BuildFeesCalculationRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var caseEl = doc.Descendants(Nc + "Case").First();
        var docketId = caseEl.Elements(Nc + "CaseDocketID").FirstOrDefault();

        Assert.NotNull(docketId);
        Assert.Equal("24CV00123", docketId!.Value);
    }

    // ─── Phase 12: Subsequent Filing Enhancements ────────────────────

    [Fact]
    public void BuildReviewFiling_Subsequent_HasComplaintElement()
    {
        var sub = BuildMinimalSubsequent();
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var caseEl = doc.Descendants(Nc + "Case").First();
        var complaint = caseEl.Elements(CivExt + "Complaint").FirstOrDefault();
        Assert.NotNull(complaint);
        Assert.Equal("1", complaint!.Attribute(St + "id")?.Value);
    }

    [Fact]
    public void BuildReviewFiling_Initial_NoComplaintElement()
    {
        var sub = BuildMinimalCaseInit();
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var caseEl = doc.Descendants(Nc + "Case").First();
        var complaint = caseEl.Elements(CivExt + "Complaint").FirstOrDefault();
        Assert.Null(complaint);
    }

    [Fact]
    public void BuildReviewFiling_Subsequent_SkipsDocumentSubmitter()
    {
        var sub = BuildMinimalSubsequent();
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var cfm = doc.Descendants(Cfm + "CoreFilingMessage").First();
        var submitter = cfm.Element(Nc + "DocumentSubmitter");
        Assert.Null(submitter);
    }

    [Fact]
    public void BuildReviewFiling_Initial_HasDocumentSubmitter()
    {
        var sub = BuildMinimalCaseInit();
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var cfm = doc.Descendants(Cfm + "CoreFilingMessage").First();
        var submitter = cfm.Element(Nc + "DocumentSubmitter");
        Assert.NotNull(submitter);
    }

    [Fact]
    public void BuildReviewFiling_Subsequent_SkipsFilingConfidentialityIndicator()
    {
        var sub = BuildMinimalSubsequent();
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var cfm = doc.Descendants(Cfm + "CoreFilingMessage").First();
        var fci = cfm.Elements(Cfm + "FilingConfidentialityIndicator").FirstOrDefault();
        Assert.Null(fci);
    }

    [Fact]
    public void BuildReviewFiling_Initial_HasFilingConfidentialityIndicator()
    {
        var sub = BuildMinimalCaseInit();
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var cfm = doc.Descendants(Cfm + "CoreFilingMessage").First();
        var fci = cfm.Elements(Cfm + "FilingConfidentialityIndicator").FirstOrDefault();
        Assert.NotNull(fci);
    }

    [Fact]
    public void BuildReviewFiling_Subsequent_NoParties_SkipsCaseAugmentation()
    {
        var sub = BuildMinimalSubsequent();
        sub.Parties.Clear();

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var caseEl = doc.Descendants(Nc + "Case").First();
        var aug = caseEl.Elements(Ecf + "CaseAugmentation").FirstOrDefault();
        Assert.Null(aug);
    }

    [Fact]
    public void BuildReviewFiling_Subsequent_WithParties_HasCaseAugmentation()
    {
        var sub = BuildMinimalSubsequent();
        sub.Parties.Add(new FilingParty
        {
            ReferenceId = "filedBy0",
            RoleCode = "PLAIN",
            FirstName = "John",
            LastName = "Smith"
        });

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var caseEl = doc.Descendants(Nc + "Case").First();
        var aug = caseEl.Elements(Ecf + "CaseAugmentation").FirstOrDefault();
        Assert.NotNull(aug);
    }

    [Fact]
    public void BuildReviewFiling_Subsequent_AdditionalInfoTags_EmittedOnIdReferences()
    {
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "FILING_PARTY",
                ClassType = "caseParticipant",
                SubType = "filedBy",
                ValueRestriction = "existing-data",
                IdReferences = new List<string> { "1493521" },
                AdditionalInfoTags = new List<AdditionalInfoTag>
                {
                    new() { TagType = "E_SERVICE", TagValue = "0" },
                    new() { TagType = "FEE_EXEMPTION", TagValue = "FEE_WAIVER" }
                }
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var idRef = doc.Descendants(DfMeta + "idReferences").First();
        Assert.Equal("1493521", idRef.Element(DfMeta + "id")!.Value);

        var tags = idRef.Elements(DfMeta + "additionalInfoTags").ToList();
        Assert.Equal(2, tags.Count);
        Assert.Equal("E_SERVICE", tags[0].Element(DfMeta + "tagType")!.Value);
        Assert.Equal("0", tags[0].Element(DfMeta + "tagValue")!.Value);
        Assert.Equal("FEE_EXEMPTION", tags[1].Element(DfMeta + "tagType")!.Value);
        Assert.Equal("FEE_WAIVER", tags[1].Element(DfMeta + "tagValue")!.Value);
    }

    /// <summary>
    /// Step #14 regression — silent-drop #10. Pre-fix the builder iterated
    /// <c>foreach (idRef in IdReferences) foreach (tag in AdditionalInfoTags)</c> which
    /// cross-contaminated tags across all refs in a multi-id metadata item: each idReferences
    /// element ended up with EVERY tag from the flat list (party A got party B's tags and
    /// vice versa). Wire-format authority: WSDL <c>TaggedReferenceType</c> carries per-id
    /// <c>additionalInfoTags</c>, so distinct refs must keep distinct tags. Closed by
    /// promoting <see cref="FilingMetadataValue.TaggedReferences"/> to the canonical wire
    /// shape and having the builder iterate per-ref tags from each <c>TaggedReference.Tags</c>.
    /// </summary>
    [Fact]
    public void BuildReviewFiling_Subsequent_TaggedReferences_PerIdTagsPreservedAcrossMultipleRefs()
    {
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "FILING_PARTY",
                ClassType = "caseParticipant",
                SubType = "filedBy",
                ValueRestriction = "existing-data",
                // Two existing parties with DIFFERENT per-id tags. Party A gets FEE_WAIVER;
                // Party B gets E_SERVICE. Each party MUST keep its own tag set in the wire.
                TaggedReferences = new List<TaggedReference>
                {
                    new()
                    {
                        Id = "PARTY-A",
                        Tags = new List<AdditionalInfoTag>
                        {
                            new() { TagType = "FEE_EXEMPTION", TagValue = "FEE_WAIVER" },
                        },
                    },
                    new()
                    {
                        Id = "PARTY-B",
                        Tags = new List<AdditionalInfoTag>
                        {
                            new() { TagType = "E_SERVICE", TagValue = "1" },
                        },
                    },
                },
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var idRefs = doc.Descendants(DfMeta + "idReferences").ToList();
        Assert.Equal(2, idRefs.Count);

        var refA = idRefs.Single(r => r.Element(DfMeta + "id")!.Value == "PARTY-A");
        var refB = idRefs.Single(r => r.Element(DfMeta + "id")!.Value == "PARTY-B");

        var tagsA = refA.Elements(DfMeta + "additionalInfoTags").ToList();
        var tagsB = refB.Elements(DfMeta + "additionalInfoTags").ToList();

        // Party A: exactly one tag, FEE_EXEMPTION=FEE_WAIVER. No cross-contamination from B.
        Assert.Single(tagsA);
        Assert.Equal("FEE_EXEMPTION", tagsA[0].Element(DfMeta + "tagType")!.Value);
        Assert.Equal("FEE_WAIVER", tagsA[0].Element(DfMeta + "tagValue")!.Value);
        Assert.DoesNotContain(tagsA, t => t.Element(DfMeta + "tagType")!.Value == "E_SERVICE");

        // Party B: exactly one tag, E_SERVICE=1. No cross-contamination from A.
        Assert.Single(tagsB);
        Assert.Equal("E_SERVICE", tagsB[0].Element(DfMeta + "tagType")!.Value);
        Assert.Equal("1", tagsB[0].Element(DfMeta + "tagValue")!.Value);
        Assert.DoesNotContain(tagsB, t => t.Element(DfMeta + "tagType")!.Value == "FEE_EXEMPTION");
    }

    // ====================================================================================
    // Step #15 — judgment classType wire-shape locks. Replaces the InlineData("judgment",
    // "judgments") row removed from T8StubSweep above. Sources of truth:
    //   • Path C audit: docs/STEP15_JUDGMENT_AUDIT.md
    //   • Wire-shape evidence: docs/fileing files/Subsequent Filing/Court Specific Concepts/
    //     Example Filing a Writ of Return Sample.xml (LASC vendor sample, 2020-03-20)
    //   • Schema: src/EFiling/EFiling.Providers.JTI/Config/JtiClassTypeSchema.json #/classTypes/judgment
    //   • Builder: ReviewFilingXmlBuilder.cs case "judgment"
    //   • Mapper:  MetadataValueMapper.cs case "judgment"
    //   • Parser:  ReviewFilingRequestParser.cs case "judgment"
    // ====================================================================================

    /// <summary>
    /// Step #15 (Path C) — positive wire-shape lock. The builder MUST emit
    /// <c>&lt;ns9:judgments&gt;&lt;ns10:judgmentId&gt;{id}&lt;/ns10:judgmentId&gt;&lt;/ns9:judgments&gt;</c>
    /// where wrapper namespace is <c>DocumentFilingMetaData</c> and content namespace is
    /// <c>CourtEventJudgment</c> (the only classType where these split). Pre-Step-#15 the
    /// builder threw <see cref="NotImplementedException"/> on judgment; this test would have
    /// failed pre-fix and locks the wire shape post-fix.
    /// </summary>
    [Fact]
    public void BuildReviewFiling_Judgment_ExistingData_EmitsJudgmentsWithJudgmentIdInSeparateNamespace()
    {
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var CourtEventJudgmentNs = XNamespace.Get(SoapEnvelopeBuilder.NsJtiCourtEventJudgment);

        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "JUDGMENT",
                ClassType = "judgment",
                ValueRestriction = "existing-data",
                JudgmentIds = new List<string> { "2562247" },
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        // Wrapper element <ns9:judgments> in DocumentFilingMetaData namespace.
        var judgmentsEl = doc.Descendants(DfMeta + "judgments").SingleOrDefault();
        Assert.NotNull(judgmentsEl);

        // Wrapper has NO <judgmentId> in its OWN namespace — it must be in CourtEventJudgment.
        Assert.Empty(judgmentsEl.Elements(DfMeta + "judgmentId"));

        // Content <judgmentId> in CourtEventJudgment namespace, value matches LASC sample id.
        var judgmentIdEl = judgmentsEl.Element(CourtEventJudgmentNs + "judgmentId");
        Assert.NotNull(judgmentIdEl);
        Assert.Equal("2562247", judgmentIdEl.Value);
    }

    /// <summary>
    /// Step #15 (Path C) — multi-id emission. WSDL declares <c>CourtEventJudgmentType[]</c>
    /// (array) so multiple <c>&lt;judgmentId&gt;</c> children inside one <c>&lt;judgments&gt;</c>
    /// wrapper is wire-spec valid. Observed sample only has 1, but the builder must support N.
    /// </summary>
    [Fact]
    public void BuildReviewFiling_Judgment_ExistingData_MultipleIdsAllEmittedInOrder()
    {
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var CourtEventJudgmentNs = XNamespace.Get(SoapEnvelopeBuilder.NsJtiCourtEventJudgment);

        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "JUDGMENT",
                ClassType = "judgment",
                ValueRestriction = "existing-data",
                JudgmentIds = new List<string> { "100", "200", "300" },
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var judgmentsEl = doc.Descendants(DfMeta + "judgments").Single();
        var judgmentIds = judgmentsEl.Elements(CourtEventJudgmentNs + "judgmentId")
            .Select(e => e.Value).ToList();

        Assert.Equal(new[] { "100", "200", "300" }, judgmentIds);
    }

    /// <summary>
    /// Step #15 (Path C) — new-data path is awaitingEvidence and MUST throw with a clear
    /// message naming the schema flag + the WSDL types a future implementer needs to handle.
    /// Schema: <c>JtiClassTypeSchema.json #/classTypes/judgment/valueRestrictions/newData</c>
    /// has <c>awaitingEvidence:true</c>; until a baseline sample lands, the builder fails
    /// loud rather than emitting a malformed wire shape.
    /// </summary>
    [Fact]
    public void BuildReviewFiling_Judgment_NewData_ThrowsNotImplementedWithSchemaFlagPointer()
    {
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "JUDGMENT",
                ClassType = "judgment",
                ValueRestriction = "new-data",
                // No JudgmentIds — new-data path doesn't use them; would need
                // JudgmentAwardType + JudgmentAwardPartyType inline data per WSDL.
            }
        };

        var ex = Assert.Throws<NotImplementedException>(
            () => ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig));

        Assert.Contains("judgment", ex.Message);
        Assert.Contains("new-data", ex.Message);
        Assert.Contains("awaitingEvidence", ex.Message);
        Assert.Contains("JudgmentAwardType", ex.Message);
    }

    /// <summary>
    /// Step #15 (Path C) — round-trip lock against the LASC vendor sample's exact wire
    /// shape. Uses an inline fragment (the FilingLeadDocument metadata block) representative
    /// of <c>docs/fileing files/Subsequent Filing/Court Specific Concepts/Example Filing
    /// a Writ of Return Sample.xml</c>. Demonstrates parser → model → builder symmetry: a
    /// judgment metadata item parsed from the LASC shape produces a <see cref="FilingMetadataValue"/>
    /// with <see cref="FilingMetadataValue.JudgmentIds"/> populated, and the builder re-emits
    /// the same wire shape (modulo namespace prefix names which are arbitrary per XML spec).
    /// </summary>
    [Fact]
    public void Judgment_RoundTrip_LascWritOfReturnSampleShape_ParserBuilderSymmetric()
    {
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var CourtEventJudgmentNs = XNamespace.Get(SoapEnvelopeBuilder.NsJtiCourtEventJudgment);

        // Step 1: build a submission with a judgment metadata value, then emit XML.
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "JUDGMENT",
                ClassType = "judgment",
                ValueRestriction = "existing-data",
                JudgmentIds = new List<string> { "2562247" },
            }
        };
        var emittedXml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);

        // Step 2: parse the emitted XML back through the parser, confirm round-trip.
        var roundTripped = ReviewFilingRequestParser.FromXml(emittedXml);
        Assert.NotNull(roundTripped.LeadDocument);
        var meta = roundTripped.LeadDocument.MetadataValues.Single();
        Assert.Equal("JUDGMENT", meta.Code);
        Assert.Equal("judgment", meta.ClassType);
        Assert.Equal("existing-data", meta.ValueRestriction);
        Assert.Single(meta.JudgmentIds);
        Assert.Equal("2562247", meta.JudgmentIds[0]);

        // Step 3: re-emit and verify byte-shape equivalence (judgments wrapper + judgmentId
        // child preserved across the round-trip; namespace prefixes may differ but URIs match).
        var reEmittedXml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(roundTripped, TestConfig);
        var reEmittedDoc = XDocument.Parse(reEmittedXml);
        var reEmittedJudgments = reEmittedDoc.Descendants(DfMeta + "judgments").Single();
        var reEmittedJudgmentId = reEmittedJudgments.Element(CourtEventJudgmentNs + "judgmentId");
        Assert.NotNull(reEmittedJudgmentId);
        Assert.Equal("2562247", reEmittedJudgmentId.Value);
    }

    /// <summary>
    /// Step #15 (Path C) — parser arm directly. Verifies that wire XML in the exact LASC
    /// shape (including the namespace split between <c>ns9:judgments</c> wrapper and
    /// <c>ns10:judgmentId</c> child) is read into <see cref="FilingMetadataValue.JudgmentIds"/>
    /// correctly. Uses a hand-crafted fragment matching the vendor sample byte-for-byte.
    /// </summary>
    [Fact]
    public void Judgment_Parser_ReadsLascWritOfReturnFragment_PopulatesJudgmentIds()
    {
        // Inline LASC-shape XML fragment. Namespaces match the LASC vendor sample exactly:
        // ns8 = DocumentValue (descriptor), ns9 = DocumentFilingMetaData (wrapper),
        // ns10 = CourtEventJudgment (content). Wrapped in a minimal envelope that the
        // parser accepts.
        var lascShape = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/""
  xmlns:ns1=""http://niem.gov/niem/niem-core/2.0""
  xmlns:ns8=""urn:com.journaltech:ecourt:ecf:extension:DocumentValue""
  xmlns:ns9=""urn:com.journaltech:ecourt:ecf:extension:DocumentFilingMetaData""
  xmlns:ns10=""urn:com.journaltech:ecourt:ecf:extension:CourtEventJudgment""
  xmlns:ns11=""urn:com.journaltech:ecourt:ecf:extension:CoreFilingMessageExtType""
  xmlns:ns14=""urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0""
  xmlns:ns2=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0""
  xmlns:ns6=""urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CoreFilingMessage-4.0""
  xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
  <SOAP-ENV:Body>
    <ns14:ReviewFilingRequestMessage>
      <ns2:SendingMDELocationID>
        <ns1:IdentificationID>EP</ns1:IdentificationID>
        <ns1:IdentificationSourceText>lasc</ns1:IdentificationSourceText>
      </ns2:SendingMDELocationID>
      <ns6:CoreFilingMessage xsi:type=""ns11:CoreFilingMessageExtType"">
        <ns1:DocumentFiledDate id=""ref1""><ns1:DateTime>2020-03-20T10:26:42-07:00</ns1:DateTime></ns1:DocumentFiledDate>
        <ns1:Case>
          <ns1:CaseDocketID>20STLC00278</ns1:CaseDocketID>
        </ns1:Case>
        <ns6:FilingLeadDocument>
          <ns1:DocumentBinary><ns1:BinaryLocationURI>http://x.example/d.pdf</ns1:BinaryLocationURI></ns1:DocumentBinary>
          <ns1:DocumentDescriptionText>WRIT055</ns1:DocumentDescriptionText>
          <ns1:DocumentFileControlID>33882</ns1:DocumentFileControlID>
          <ns9:DocumentFilingMetaData>
            <ns9:documentFilingMetaDataItem>
              <ns9:docValueMetaDataItem>
                <ns8:code>JUDGMENT</ns8:code>
                <ns8:classType>judgment</ns8:classType>
                <ns8:valueRestriction>existing-data</ns8:valueRestriction>
              </ns9:docValueMetaDataItem>
              <ns9:judgments>
                <ns10:judgmentId>2562247</ns10:judgmentId>
              </ns9:judgments>
            </ns9:documentFilingMetaDataItem>
          </ns9:DocumentFilingMetaData>
        </ns6:FilingLeadDocument>
      </ns6:CoreFilingMessage>
    </ns14:ReviewFilingRequestMessage>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

        var sub = ReviewFilingRequestParser.FromXml(lascShape);
        var meta = sub.LeadDocument!.MetadataValues.Single();

        Assert.Equal("JUDGMENT", meta.Code);
        Assert.Equal("judgment", meta.ClassType);
        Assert.Equal("existing-data", meta.ValueRestriction);
        Assert.Single(meta.JudgmentIds);
        Assert.Equal("2562247", meta.JudgmentIds[0]);
    }

    // ====================================================================================
    // Step #16 — Tier B fixture drift silent-drop guard (silent-drop scoreboard #15).
    // Sources of truth:
    //   • Audit:   docs/STEP16_TIERB_FIXTURE_DRIFT_AUDIT.md
    //   • Helper:  FilingMetadataValue.ReplaceWithSingleId (FilingDocument.cs)
    //   • Origin:  2026-05-20 FAM-SUB-004 Tier B resubmit failed live with 4013/1494948,
    //              even though the iteration-5 fixture override explicitly cleared 1494948.
    //              Diagnostic dump showed the override mutated mv.IdReferences (legacy
    //              back-compat) but mv.TaggedReferences (canonical Step #14 wire-source)
    //              retained the baseline parsed value, which the builder emitted instead.
    // ====================================================================================

    /// <summary>
    /// Step #39 — Path B forcing function. Demonstrates that mutating ONLY the
    /// legacy <see cref="FilingMetadataValue.IdReferences"/> field while leaving
    /// <see cref="FilingMetadataValue.TaggedReferences"/> at its parser-populated
    /// state now THROWS at builder time instead of silently emitting the stale
    /// canonical id. This is the exact silent-drop pattern that broke FAM-SUB-004
    /// on 2026-05-20 (silent-drop scoreboard #15).
    ///
    /// <para>
    /// Before Step #39 the builder silently emitted <c>BASELINE_ID</c> (the
    /// stale <see cref="FilingMetadataValue.TaggedReferences"/> value) while
    /// the fixture author's <c>OVERRIDE_ID</c> intent was dropped. After Step
    /// #39 the divergence triggers an <see cref="InvalidOperationException"/>
    /// with a structured message pointing migrators at
    /// <see cref="FilingMetadataValue.ReplaceWithSingleId"/>. The throw landed
    /// safely on 2026-05-21 because the prior lazy-migration sprint
    /// (Steps #16-#38) retired all 23 Tier B SF fixtures to the helper.
    /// </para>
    /// </summary>
    [Fact]
    public void Step39_PathBForcingFunction_LegacyFieldOnlyMutation_ThrowsOnBuild()
    {
        var sub = BuildMinimalSubsequent();
        var mv = new FilingMetadataValue
        {
            Code = "FILING_PARTY",
            ClassType = "caseParticipant",
            SubType = "filed-by",
            ValueRestriction = "existing-data",
            // Both fields populated as if parsed from a baseline.
            IdReferences = { "BASELINE_ID" },
            TaggedReferences = { new TaggedReference { Id = "BASELINE_ID" } },
        };
        // Pre-Step-#16-style override: mutate ONLY the legacy field. TaggedReferences
        // stays at "BASELINE_ID" — divergent state. Step #39 forcing function
        // detects this and throws at build time.
        mv.IdReferences.Clear();
        mv.IdReferences.Add("OVERRIDE_ID");
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue> { mv };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig));

        // Error message must surface the divergence + name the helper.
        Assert.Contains("divergence detected", ex.Message);
        Assert.Contains("FILING_PARTY", ex.Message);
        Assert.Contains("BASELINE_ID", ex.Message);
        Assert.Contains("OVERRIDE_ID", ex.Message);
        Assert.Contains("ReplaceWithSingleId", ex.Message);
    }

    /// <summary>
    /// Step #39 — Path B forcing function (negative case). Asserts that the
    /// throw does NOT fire when only ONE of the two id-source lists is
    /// populated. This is the legacy fallback path used by production code
    /// (CourtFilingController JSON parser + MetadataValueMapper) which writes
    /// to <see cref="FilingMetadataValue.IdReferences"/> only on fresh mv
    /// objects with empty <see cref="FilingMetadataValue.TaggedReferences"/>.
    /// </summary>
    [Fact]
    public void Step39_PathBForcingFunction_LegacyFallbackPath_DoesNotThrow()
    {
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);

        var sub = BuildMinimalSubsequent();
        var mv = new FilingMetadataValue
        {
            Code = "FILING_PARTY",
            ClassType = "caseParticipant",
            SubType = "filed-by",
            ValueRestriction = "existing-data",
            // Only IdReferences populated; TaggedReferences stays empty (production
            // mapper / controller pattern).
            IdReferences = { "PRODUCTION_ID" },
        };
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue> { mv };

        // Must not throw.
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);
        var idRefs = doc.Descendants(DfMeta + "idReferences").ToList();
        Assert.Single(idRefs);
        Assert.Equal("PRODUCTION_ID", idRefs[0].Element(DfMeta + "id")!.Value);
    }

    /// <summary>
    /// Step #39 — Path B forcing function (positive case). Asserts that
    /// <see cref="FilingMetadataValue.ReplaceWithSingleId"/> (the canonical
    /// migration helper used by all 23 migrated Tier B SF fixtures) produces
    /// a state where TaggedReferences and IdReferences agree, so the forcing
    /// function does NOT throw and the wire emits the override id.
    /// </summary>
    [Fact]
    public void Step39_PathBForcingFunction_ReplaceWithSingleIdHelper_DoesNotThrow()
    {
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);

        var sub = BuildMinimalSubsequent();
        var mv = new FilingMetadataValue
        {
            Code = "FILING_PARTY",
            ClassType = "caseParticipant",
            SubType = "filed-by",
            ValueRestriction = "existing-data",
            // Both fields populated as if parsed from a baseline.
            IdReferences = { "BASELINE_ID" },
            TaggedReferences = { new TaggedReference { Id = "BASELINE_ID" } },
        };
        // Canonical migration: helper mutates BOTH fields atomically.
        mv.ReplaceWithSingleId("OVERRIDE_ID");
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue> { mv };

        // Must not throw; wire emits override id.
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);
        var idRefs = doc.Descendants(DfMeta + "idReferences").ToList();
        Assert.Single(idRefs);
        Assert.Equal("OVERRIDE_ID", idRefs[0].Element(DfMeta + "id")!.Value);
    }

    /// <summary>
    /// Step #16 — positive helper test. Asserts that
    /// <see cref="FilingMetadataValue.ReplaceWithSingleId"/> mutates BOTH the canonical
    /// (<see cref="FilingMetadataValue.TaggedReferences"/>) and legacy
    /// (<see cref="FilingMetadataValue.IdReferences"/> + <see cref="FilingMetadataValue.AdditionalInfoTags"/>)
    /// fields atomically, so the wire emits the override id (and the helper-supplied
    /// tags) cleanly. This is the API every fixture override / programmatic submission
    /// mutator should use post-Step-#14.
    /// </summary>
    [Fact]
    public void Step16_ReplaceWithSingleId_MutatesBothFields_EmitsOverrideOnWire()
    {
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);

        var sub = BuildMinimalSubsequent();
        var mv = new FilingMetadataValue
        {
            Code = "FILING_PARTY",
            ClassType = "caseParticipant",
            SubType = "filed-by",
            ValueRestriction = "existing-data",
            IdReferences = { "BASELINE_ID" },
            TaggedReferences =
            {
                new TaggedReference
                {
                    Id = "BASELINE_ID",
                    Tags =
                    {
                        new AdditionalInfoTag { TagType = "E_SERVICE", TagValue = "1" },
                        new AdditionalInfoTag { TagType = "EFSP_EMAIL", TagValue = "EMAIL_VALUE_HERE" },
                    },
                },
            },
            AdditionalInfoTags =
            {
                new AdditionalInfoTag { TagType = "E_SERVICE", TagValue = "1" },
                new AdditionalInfoTag { TagType = "EFSP_EMAIL", TagValue = "EMAIL_VALUE_HERE" },
            },
        };

        // Use the helper to replace with the override id, preserving E_SERVICE and
        // dropping EFSP_EMAIL (the FAM-SUB-004 pattern).
        var preservedTags = mv.TaggedReferences
            .SelectMany(tr => tr.Tags)
            .Where(t => !string.Equals(t.TagType, "EFSP_EMAIL", StringComparison.OrdinalIgnoreCase))
            .ToList();
        mv.ReplaceWithSingleId("OVERRIDE_ID", preservedTags);

        // Both shapes are now in sync — assert the model state directly first.
        Assert.Single(mv.IdReferences);
        Assert.Equal("OVERRIDE_ID", mv.IdReferences[0]);
        Assert.Single(mv.TaggedReferences);
        Assert.Equal("OVERRIDE_ID", mv.TaggedReferences[0].Id);
        Assert.Single(mv.AdditionalInfoTags);
        Assert.Equal("E_SERVICE", mv.AdditionalInfoTags[0].TagType);
        Assert.DoesNotContain(mv.TaggedReferences[0].Tags, t => t.TagType == "EFSP_EMAIL");

        // Now exercise the wire — assert the wire emits the override.
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue> { mv };
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var idRefs = doc.Descendants(DfMeta + "idReferences").ToList();
        Assert.Single(idRefs);
        Assert.Equal("OVERRIDE_ID", idRefs[0].Element(DfMeta + "id")!.Value);

        // Tags: only E_SERVICE survives, EFSP_EMAIL was dropped.
        var tags = idRefs[0].Elements(DfMeta + "additionalInfoTags").ToList();
        Assert.Single(tags);
        Assert.Equal("E_SERVICE", tags[0].Element(DfMeta + "tagType")!.Value);
        Assert.Equal("1", tags[0].Element(DfMeta + "tagValue")!.Value);
        Assert.DoesNotContain("EFSP_EMAIL", xml);
        Assert.DoesNotContain("BASELINE_ID", xml);
    }

    /// <summary>
    /// Step #16 — argument validation. Helper must reject empty/null id (defensive
    /// programming; an empty id slipping through would emit an empty
    /// <c>&lt;ns14:id&gt;&lt;/ns14:id&gt;</c> element which Madera would reject with
    /// 4013 and which would be hard to root-cause).
    /// </summary>
    [Fact]
    public void Step16_ReplaceWithSingleId_RejectsEmptyId()
    {
        var mv = new FilingMetadataValue
        {
            Code = "FILING_PARTY",
            ClassType = "caseParticipant",
            ValueRestriction = "existing-data",
        };

        Assert.Throws<ArgumentException>(() => mv.ReplaceWithSingleId(""));
        Assert.Throws<ArgumentException>(() => mv.ReplaceWithSingleId(null!));
    }

    /// <summary>
    /// Step #14 — symmetric per-id-tag fidelity for caseAssignment (attorney) existing-data.
    /// Same cross-contamination bug existed at <c>ReviewFilingXmlBuilder.cs:997-1006</c> and
    /// is closed by the same <see cref="FilingMetadataValue.TaggedReferences"/> promotion.
    /// </summary>
    [Fact]
    public void BuildReviewFiling_Subsequent_TaggedReferences_CaseAssignment_PerIdTagsPreserved()
    {
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "FILING_ATTORNEY",
                ClassType = "caseAssignment",
                ValueRestriction = "existing-data",
                TaggedReferences = new List<TaggedReference>
                {
                    new()
                    {
                        Id = "ATT-1",
                        Tags = new List<AdditionalInfoTag>
                        {
                            new() { TagType = "E_SERVICE", TagValue = "1" },
                        },
                    },
                    new()
                    {
                        Id = "ATT-2",
                        // ATT-2 has NO tags — must emit an idReferences with id only, no
                        // additionalInfoTags. Pre-fix this would have got ATT-1's tag too.
                        Tags = new List<AdditionalInfoTag>(),
                    },
                },
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var idRefs = doc.Descendants(DfMeta + "idReferences").ToList();
        Assert.Equal(2, idRefs.Count);

        var ref1 = idRefs.Single(r => r.Element(DfMeta + "id")!.Value == "ATT-1");
        var ref2 = idRefs.Single(r => r.Element(DfMeta + "id")!.Value == "ATT-2");

        Assert.Single(ref1.Elements(DfMeta + "additionalInfoTags"));
        Assert.Empty(ref2.Elements(DfMeta + "additionalInfoTags"));
    }

    [Fact]
    public void BuildReviewFiling_Subsequent_CaseAssignment_ExistingData_HasIdReferences()
    {
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var DfValue = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentValue);
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "FILING_ATTORNEY",
                ClassType = "caseAssignment",
                ValueRestriction = "existing-data",
                IdReferences = new List<string> { "ATT-789" },
                AdditionalInfoTags = new List<AdditionalInfoTag>
                {
                    new() { TagType = "E_SERVICE", TagValue = "1" }
                }
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var item = doc.Descendants(DfMeta + "documentFilingMetaDataItem").First();
        var desc = item.Element(DfMeta + "docValueMetaDataItem")!;
        // classType lives in DocumentValue namespace, not DocumentFilingMetaData
        Assert.Equal("caseAssignment", desc.Element(DfValue + "classType")!.Value);

        var idRef = item.Element(DfMeta + "idReferences")!;
        Assert.Equal("ATT-789", idRef.Element(DfMeta + "id")!.Value);

        var tag = idRef.Element(DfMeta + "additionalInfoTags")!;
        Assert.Equal("E_SERVICE", tag.Element(DfMeta + "tagType")!.Value);
    }

    [Fact]
    public void BuildReviewFiling_Subsequent_CaseAssignment_NewData_HasCaseAssignmentValue()
    {
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var CaseAssign = XNamespace.Get(SoapEnvelopeBuilder.NsJtiCaseAssignmentType);
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "NEW_ATTORNEY",
                ClassType = "caseAssignment",
                ValueRestriction = "new-data",
                CaseAssignmentValue = new CaseAssignmentData
                {
                    FirstName = "Robert",
                    LastName = "Lawyer",
                    BarNumber = "555555",
                    FirmName = "Lawyer & Associates",
                    AssignmentRole = "ATT",
                    EService = true
                }
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var caVal = doc.Descendants(DfMeta + "caseAssignmentValue").First();
        Assert.NotNull(caVal);

        // Audit H-2 fix: EntityPerson / EntityOrganization inside
        // caseAssignmentValue use ECF CommonTypes-4.0 namespace (not niem-core). Children
        // (PersonName, PersonOtherIdentification, OrganizationName) remain in niem-core.
        var person = caVal.Element(Ecf + "EntityPerson")!;
        var name = person.Element(Nc + "PersonName")!;
        Assert.Equal("Robert", name.Element(Nc + "PersonGivenName")!.Value);
        Assert.Equal("Lawyer", name.Element(Nc + "PersonSurName")!.Value);

        var barId = person.Element(Nc + "PersonOtherIdentification")!;
        Assert.Equal("555555", barId.Element(Nc + "IdentificationID")!.Value);

        var org = caVal.Element(Ecf + "EntityOrganization")!;
        Assert.Equal("Lawyer & Associates", org.Element(Nc + "OrganizationName")!.Value);

        var role = caVal.Element(CaseAssign + "AssignmentRole")!;
        Assert.Equal("ATT", role.Value);
    }

    [Fact]
    public void BuildReviewFiling_Document_IdentificationSourceText_WhenSet()
    {
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.IdentificationSourceText = "PLA";

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var docId = doc.Descendants(Cfm + "FilingLeadDocument").First()
            .Element(Nc + "DocumentIdentification")!;
        Assert.Equal("PLA", docId.Element(Nc + "IdentificationSourceText")!.Value);
    }

    [Fact]
    public void BuildReviewFiling_Document_NoIdentificationSourceText_WhenNotSet()
    {
        var sub = BuildMinimalCaseInit();
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var docId = doc.Descendants(Cfm + "FilingLeadDocument").First()
            .Element(Nc + "DocumentIdentification")!;
        Assert.Null(docId.Element(Nc + "IdentificationSourceText"));
    }

    [Fact]
    public void BuildReviewFiling_Envelope_HasNewNamespaceDeclarations()
    {
        var sub = BuildMinimalCaseInit();
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        // ns14 = DocumentFilingMetaData, ns15 = DocumentValue, ns16 = CaseAssignmentType
        var root = doc.Root!;
        var ns14 = root.Attributes().FirstOrDefault(a => a.Name.LocalName == "ns14");
        var ns15 = root.Attributes().FirstOrDefault(a => a.Name.LocalName == "ns15");
        var ns16 = root.Attributes().FirstOrDefault(a => a.Name.LocalName == "ns16");

        Assert.NotNull(ns14);
        Assert.Equal(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData, ns14!.Value);
        Assert.NotNull(ns15);
        Assert.Equal(SoapEnvelopeBuilder.NsJtiDocumentValue, ns15!.Value);
        Assert.NotNull(ns16);
        Assert.Equal(SoapEnvelopeBuilder.NsJtiCaseAssignmentType, ns16!.Value);
    }

    // ─── Phase 12: GetCaseListRequest Search Mode Tests ──────────────

    [Fact]
    public void BuildGetCaseListRequest_CaseNumber_HasCaseDocketID()
    {
        var criteria = new CaseSearchCriteria { CaseDocketId = "24CV00123" };
        var xml = SoapEnvelopeBuilder.BuildGetCaseListRequest("madera", criteria);

        Assert.Contains("CaseDocketID", xml);
        Assert.Contains("24CV00123", xml);
    }

    [Fact]
    public void BuildGetCaseListRequest_PartyIndividual_HasPersonGivenAndSurName()
    {
        var criteria = new CaseSearchCriteria { FirstName = "John", LastName = "Smith" };
        var xml = SoapEnvelopeBuilder.BuildGetCaseListRequest("madera", criteria);

        Assert.Contains("PersonGivenName", xml);
        Assert.Contains("John", xml);
        Assert.Contains("PersonSurName", xml);
        Assert.Contains("Smith", xml);
        Assert.DoesNotContain("PersonFullName", xml);
    }

    [Fact]
    public void BuildGetCaseListRequest_PartyBusiness_HasOrganizationName()
    {
        var criteria = new CaseSearchCriteria { OrganizationName = "Acme Corp" };
        var xml = SoapEnvelopeBuilder.BuildGetCaseListRequest("madera", criteria);

        Assert.Contains("EntityOrganization", xml);
        Assert.Contains("OrganizationName", xml);
        Assert.Contains("Acme Corp", xml);
        Assert.DoesNotContain("EntityPerson", xml);
    }

    [Fact]
    public void BuildGetCaseListRequest_Title_HasCaseTitleText()
    {
        var criteria = new CaseSearchCriteria { CaseTitle = "Smith v. Doe" };
        var xml = SoapEnvelopeBuilder.BuildGetCaseListRequest("madera", criteria);

        Assert.Contains("CaseTitleText", xml);
        Assert.Contains("Smith v. Doe", xml);
    }

    [Fact]
    public void BuildGetCaseListRequest_LegacyFullName_HasPersonFullName()
    {
        var criteria = new CaseSearchCriteria { PartySearchTerm = "John Smith" };
        var xml = SoapEnvelopeBuilder.BuildGetCaseListRequest("madera", criteria);

        Assert.Contains("PersonFullName", xml);
        Assert.Contains("John Smith", xml);
    }

    [Fact]
    public void BuildGetCaseListRequest_BackwardCompatOverload_Works()
    {
        var xml = SoapEnvelopeBuilder.BuildGetCaseListRequest("madera", caseDocketId: "CASE-1");
        Assert.Contains("CaseDocketID", xml);
        Assert.Contains("CASE-1", xml);
    }

    // ─── Residual b + c regression gates ────────
    // Catalog §3.0 observation #3 (fail-closed on unknowns) + §3.14 (attorney classType
    // has wire.wrapperElement="attorneyValue", NOT "caseParticipantValue"). Pre-fix, the
    // builder switch had (a) no default arm → unknown classTypes silently dropped the
    // value child, and (b) a shared `case "caseparticipant": case "attorney":` fall-through
    // that emitted <caseParticipantValue> for attorney new-data. Both fixed in this session.

    [Fact]
    public void BuildReviewFiling_AttorneyClassType_NewData_EmitsAttorneyValueWrapper_ResidualC()
    {
        // Residual c: attorney new-data must emit <attorneyValue> (not <caseParticipantValue>).
        // Attorney is V2 awaiting-evidence per JtiClassTypeSchema.json; internal shape is a
        // hypothesis (mirrors caseParticipant children today). Wrapper element is non-negotiable
        // per the schema knownBugs entry.
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "NEW_ATTORNEY",
                ClassType = "attorney",
                ValueRestriction = "new-data",
                NewPartyValue = new FilingParty
                {
                    ReferenceId = "attorney1",
                    RoleCode = "ATT",
                    FirstName = "Jane",
                    LastName = "Doe",
                    BarNumber = "123456"
                }
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        // The attorney metadata item MUST contain <attorneyValue>.
        var attorneyValue = doc.Descendants(DfMeta + "attorneyValue").FirstOrDefault();
        Assert.True(attorneyValue is not null,
            "Expected <attorneyValue> wrapper for attorney new-data (schema §3.14). " +
            "Pre-fix the builder emitted <caseParticipantValue> via fall-through bug.");

        // Sanity: the fall-through bug — if present — would emit <caseParticipantValue>
        // INSIDE the attorney metadata item (scoped, not globally; caseParticipant items
        // elsewhere in the envelope are independent).
        var attorneyItem = attorneyValue!.Parent!;
        Assert.Null(attorneyItem.Element(DfMeta + "caseParticipantValue"));
    }

    [Fact]
    public void BuildReviewFiling_AttorneyClassType_ExistingData_EmitsIdReferences()
    {
        // Residual c companion test: existing-data path for attorney still works via
        // idReferences (shared pattern with caseParticipant/caseAssignment). This confirms
        // the split didn't regress the existing-data path.
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "FILING_ATTORNEY",
                ClassType = "attorney",
                ValueRestriction = "existing-data",
                IdReferences = new List<string> { "ATT-999" }
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        // Find the idReferences element specifically under the attorney metadata item.
        var idRef = doc.Descendants(DfMeta + "documentFilingMetaDataItem")
            .SelectMany(item => item.Elements(DfMeta + "idReferences"))
            .FirstOrDefault(r => r.Element(DfMeta + "id")?.Value == "ATT-999");
        Assert.True(idRef is not null,
            "Expected <idReferences><id>ATT-999</id></idReferences> for attorney existing-data.");
    }

    [Fact]
    public void BuildReviewFiling_UnknownClassType_ThrowsInvalidOperationException_ResidualB()
    {
        // Residual b: default arm fail-closed per catalog §3.0 observation #3. Pre-fix the
        // switch silently emitted a docValueMetaDataItem with only the descriptor and no
        // value child; post-fix it throws with a message pointing to JtiClassTypeSchema.json.
        // "quantumNonsense" is NOT a schema-declared classType → InvalidOperationException
        // path (as opposed to the schema-declared-but-unimplemented NotImplementedException
        // path gated below).
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "BOGUS_FIELD",
                ClassType = "quantumNonsense",
                ValueRestriction = "new-data",
                TextValue = "irrelevant"
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig));

        // Message must surface both the offending classType and a pointer to the schema so
        // a future developer can resolve it without archaeology.
        Assert.Contains("quantumNonsense", ex.Message);
        Assert.Contains("BOGUS_FIELD", ex.Message);
        Assert.Contains("JtiClassTypeSchema.json", ex.Message);
    }

    [Theory]
    [InlineData("number",           "numberValue")]
    [InlineData("email",             "emailValue")]
    // [InlineData("crsReceiptNumber", "crsReceiptNumberValue")] — REMOVED Step #46
    //. Path 2 of the Step #46 deep-probe of JTI HTML docs surfaced
    // Layer A evidence: the Document Metadata page's Class Types section enumerates
    // `crsReceiptNumber` as "String. The calendar reservation number generated by the
    // CRS system." Builder + parser arms landed alongside (same evidence tier as
    // text / currency). See ReviewFilingXmlBuilderTests's new positive emission test
    // BuildReviewFiling_CrsReceiptNumber_EmitsScalarWrapper for the wire-shape lock.
    [InlineData("address",           "addressValue")]
    [InlineData("document",          "documentValue")]
    [InlineData("scheduledEvent",   "scheduledEventValue")]
    [InlineData("action",            "actionValue")]
    [InlineData("caseSpecialStatus","caseSpecialStatusValue")]
    // [InlineData("judgment", "judgments")] — REMOVED Step #15 (Path C). Builder now
    // implements the existing-data arm. See positive shape test
    // BuildReviewFiling_Judgment_ExistingData_EmitsJudgmentsWithJudgmentIdInSeparateNamespace
    // for the wire-shape lock + new-data throw test below.
    [InlineData("relatedCase",       "relatedCaseValue")]
    public void BuildReviewFiling_SchemaDeclaredButUnimplementedClassType_ThrowsNotImplemented_T8StubSweep(
        string classType, string expectedWrapperElement)
    {
        // T-8 stub sweep: 10 classTypes are declared in
        // JtiClassTypeSchema.json but have no builder arm yet. The schema-aware default
        // arm reads the schema at runtime, distinguishes "known-but-unimplemented" from
        // "completely-unknown", and throws NotImplementedException with the expected wire
        // wrapper + evidence level baked into the message. This test is parameterized
        // across all 10 — when a builder arm is added later (e.g., from a new baseline
        // sample), remove the corresponding InlineData row and add a positive wire-shape
        // test instead.
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "STUB_FIELD",
                ClassType = classType,
                ValueRestriction = "new-data",
                TextValue = "irrelevant"
            }
        };

        var ex = Assert.Throws<NotImplementedException>(
            () => ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig));

        // Message must name the offending classType + the expected wrapper so the next
        // engineer can implement the arm without archaeology.
        Assert.Contains(classType, ex.Message);
        Assert.Contains(expectedWrapperElement, ex.Message);
        Assert.Contains("builder arm", ex.Message);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Step #46 — crsReceiptNumber wire-shape lock.
    //
    // Replaces the InlineData("crsReceiptNumber", "crsReceiptNumberValue") row
    // removed from T8StubSweep above. Sources of truth:
    //   • Path 2 audit: docs/PROGRESS.md Step #46 narrative
    //   • Layer A evidence: docs/fileing files/Document Metadata/
    //     Document Metadata _ EFM Documentation.html line 626
    //     ("crsReceiptNumber — String. The calendar reservation number
    //      generated by the CRS system.")
    //   • WSDL wire shape: scalar <crsReceiptNumberValue>{string}</crsReceiptNumberValue>
    //     wrapper at EFiling.WsdlGenerated/FilingReview/Reference.cs:11474
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildReviewFiling_CrsReceiptNumber_EmitsScalarWrapper()
    {
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var DfValue = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentValue);

        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "CRS_RECEIPT",
                ClassType = "crsReceiptNumber",
                ValueRestriction = "new-data",
                CrsReceiptNumberValue = "RES-2026-00123456"
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var item = doc.Descendants(DfMeta + "documentFilingMetaDataItem").FirstOrDefault();
        Assert.NotNull(item);

        // Descriptor must carry classType="crsReceiptNumber" (canonical casing).
        var descriptor = item.Element(DfMeta + "docValueMetaDataItem");
        Assert.NotNull(descriptor);
        Assert.Equal("crsReceiptNumber", descriptor.Element(DfValue + "classType")?.Value);

        // Wire shape per WSDL + JTI HTML doc: scalar string inside
        // <crsReceiptNumberValue> in the DocumentFilingMetaData namespace.
        var crsEl = item.Element(DfMeta + "crsReceiptNumberValue");
        Assert.NotNull(crsEl);
        Assert.Equal("RES-2026-00123456", crsEl.Value);
        Assert.False(crsEl.HasElements,
            "crsReceiptNumberValue is a scalar wrapper per JTI Document Metadata HTML doc " +
            "('String. The calendar reservation number generated by the CRS system.') and " +
            "WSDL clrType=string. Must NOT contain child elements.");
    }

    [Fact]
    public void BuildReviewFiling_CrsReceiptNumber_NullValue_OmitsWrapper()
    {
        // Consistent with text arm semantics: null TextValue / CrsReceiptNumberValue
        // emits NO wrapper element (silent skip). The descriptor still emits.
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);

        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "CRS_RECEIPT",
                ClassType = "crsReceiptNumber",
                ValueRestriction = "new-data",
                CrsReceiptNumberValue = null
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var item = doc.Descendants(DfMeta + "documentFilingMetaDataItem").FirstOrDefault();
        Assert.NotNull(item);
        Assert.Null(item.Element(DfMeta + "crsReceiptNumberValue"));
    }

    [Fact]
    public void ParseReviewFiling_CrsReceiptNumber_RoundTrips()
    {
        // Build → parse round-trip ensures the parser arm restores
        // CrsReceiptNumberValue from the wire shape produced by the builder.
        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            new()
            {
                Code = "CRS_RECEIPT",
                ClassType = "crsReceiptNumber",
                ValueRestriction = "new-data",
                CrsReceiptNumberValue = "RES-2026-00123456"
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var roundTripped = ReviewFilingRequestParser.FromXml(xml);

        Assert.NotNull(roundTripped.LeadDocument);
        var mv = roundTripped.LeadDocument.MetadataValues.FirstOrDefault(m => m.Code == "CRS_RECEIPT");
        Assert.NotNull(mv);
        Assert.Equal("crsReceiptNumber", mv.ClassType);
        Assert.Equal("RES-2026-00123456", mv.CrsReceiptNumberValue);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Unknown valueRestriction anchor
    //
    // Partner to MetadataValueMapperTests.DetermineTagValue_UnknownTagType_*. Documents the
    // deliberate passthrough policy for valueRestriction — the builder does NOT validate
    // the string, it dispatches on which FilingMetadataValue fields are populated
    // (IdReferences vs NewPartyValues/CaseAssignmentValue). An unknown valueRestriction
    // string is emitted verbatim inside <descriptor>. Madera's wire validator is the
    // authoritative gate, not the builder.
    //
    // If a future change introduces builder-side validation (e.g., enum of "new-data" /
    // "existing-data"), flip this test to Assert.Throws and add a mapper-level guard too.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildReviewFiling_UnknownValueRestriction_EmitsVerbatim_AnchorsPassthroughPolicy()
    {
        var DfMeta = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData);
        var DfValue = XNamespace.Get(SoapEnvelopeBuilder.NsJtiDocumentValue);

        var sub = BuildMinimalSubsequent();
        sub.LeadDocument!.MetadataValues = new List<FilingMetadataValue>
        {
            // classType=text with an unrecognized valueRestriction. "text" is a well-known
            // classType so we exercise the valueRestriction passthrough in isolation without
            // conflating it with classType fail-closed.
            new()
            {
                Code = "SOME_TEXT_FIELD",
                ClassType = "text",
                ValueRestriction = "banana-data", // not "new-data" or "existing-data"
                TextValue = "hello"
            }
        };

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        // The unknown valueRestriction must appear verbatim inside <docValueMetaDataItem>.
        // That's the descriptor wrapper per builder line ~787 (not a ns-prefixed "descriptor"
        // element — JTI's wire grammar names it "docValueMetaDataItem" in DfMeta ns and the
        // valueRestriction child is in DfValue ns).
        var ourItem = doc.Descendants(DfMeta + "documentFilingMetaDataItem")
            .FirstOrDefault(i => i.Descendants(DfValue + "code").Any(c => c.Value == "SOME_TEXT_FIELD"));
        Assert.True(ourItem is not null,
            "Builder did not emit the text metadata item for our test case — upstream setup is wrong.");

        var restrictionEl = ourItem!
            .Elements(DfMeta + "docValueMetaDataItem")
            .SelectMany(d => d.Elements(DfValue + "valueRestriction"))
            .FirstOrDefault();

        Assert.True(restrictionEl is not null,
            "Passthrough anchor: the builder must echo whatever valueRestriction string the " +
            "caller provides, including unknowns. If the builder now validates valueRestriction, " +
            "flip this test to Assert.Throws and update the anchor comment.");
        Assert.Equal("banana-data", restrictionEl!.Value);
    }

    [Fact]
    public void BuildReviewFiling_AlternateNameWithSuffix_EmitsPersonNameSuffixText()
    {
        // Wire-level pin for the 2026-05-17 AKA Suffix silent-drop fix.
        // Before the fix the AKA path emitted PersonGivenName/MiddleName/SurName but NOT
        // PersonNameSuffixText, even though the main party path always did. This locked the
        // suffix into the silent-drop chain at the wire boundary regardless of whether
        // upstream layers carried it through. This test pins the wire emission so the
        // builder cannot regress.
        var sub = BuildMinimalCaseInit();
        sub.Parties[0].AlternateNames.Add(new AlternateName
        {
            Type = "AKA",
            FirstName = "Johnny",
            LastName = "Smith",
            NameSuffix = "Jr."
        });

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        // Find the AKA EntityPerson — the second one inside the first CaseParticipantExt
        // (first is the main party itself, second is the AKA).
        var caseParticipant = doc.Descendants(CpExt + "CaseParticipantExt").First();
        var entityPersons = caseParticipant.Elements(Nc + "EntityPerson").ToList();
        Assert.Equal(2, entityPersons.Count);

        var akaPersonName = entityPersons[1].Element(Nc + "PersonName");
        Assert.NotNull(akaPersonName);
        var akaSuffix = akaPersonName!.Element(Nc + "PersonNameSuffixText");
        Assert.NotNull(akaSuffix);
        Assert.Equal("Jr.", akaSuffix!.Value);
    }

    [Fact]
    public void BuildReviewFiling_NewPartyMetadataCaseParticipantValue_WithAkaSuffix_EmitsBothEntityPersonsWithSuffix()
    {
        // Wire-level pin for the 2026-05-17 Tier B finding: pre-fix the SF metadata-driven
        // new-party path silently dropped AKAs at the BuildCaseParticipantValue layer. Steps
        // #7-#8 wired AKAs through DTO + mapper + the CC initial-filing builder path
        // (BuildParticipant), but missed this SF metadata path. Verified live via Tier B —
        // the wire body had the main party EntityPerson but NO sibling AKA EntityPerson.
        // This test pins the post-fix shape: two EntityPerson children inside
        // caseParticipantValue, both carrying PersonNameSuffixText when populated.
        var sub = BuildMinimalCaseInit();
        var newParty = new FilingParty
        {
            ReferenceId = "newP0",
            RoleCode = "PLAIN",
            FirstName = "Tier",
            MiddleName = "B",
            LastName = "Verifier",
            NameSuffix = "Jr.",
        };
        newParty.AlternateNames.Add(new AlternateName
        {
            Type = "AKA",
            FirstName = "Aka",
            LastName = "Tester",
            NameSuffix = "Sr.",
        });
        sub.LeadDocument!.MetadataValues.Add(new FilingMetadataValue
        {
            Code = "NEW_FILING_PARTY",
            ClassType = "caseParticipant",
            SubType = "filed-by",
            ValueRestriction = "new-data",
            NewPartyValues = new List<FilingParty> { newParty },
        });

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        // Find the SF caseParticipantValue (NOT the Case-level CaseParticipantExt).
        var caseParticipantValue = doc.Descendants()
            .First(e => e.Name.LocalName == "caseParticipantValue");
        var entityPersons = caseParticipantValue.Elements()
            .Where(e => e.Name.LocalName == "EntityPerson").ToList();
        Assert.Equal(2, entityPersons.Count);

        // Primary party with suffix (Step #8 fix).
        var primarySuffix = entityPersons[0]
            .Elements().First(e => e.Name.LocalName == "PersonName")
            .Elements().FirstOrDefault(e => e.Name.LocalName == "PersonNameSuffixText");
        Assert.NotNull(primarySuffix);
        Assert.Equal("Jr.", primarySuffix!.Value);

        // AKA with suffix (Tier B Stage-1 finding fix). Pre-fix this entire EntityPerson
        // was missing because BuildCaseParticipantValue didn't iterate AlternateNames.
        var akaName = entityPersons[1]
            .Elements().First(e => e.Name.LocalName == "PersonName");
        var akaGiven = akaName.Elements().First(e => e.Name.LocalName == "PersonGivenName").Value;
        var akaSur = akaName.Elements().First(e => e.Name.LocalName == "PersonSurName").Value;
        var akaSuffix = akaName.Elements().FirstOrDefault(e => e.Name.LocalName == "PersonNameSuffixText");
        Assert.Equal("Aka", akaGiven);
        Assert.Equal("Tester", akaSur);
        Assert.NotNull(akaSuffix);
        Assert.Equal("Sr.", akaSuffix!.Value);

        // AKA carries its Type via PersonOtherIdentification (parallel to CIV-SUB-001 baseline
        // shape — IdentificationCategoryText AFS marker).
        var akaCategory = entityPersons[1].Descendants()
            .First(e => e.Name.LocalName == "IdentificationCategoryText").Value;
        Assert.Equal("AKA", akaCategory);
    }

    [Fact]
    public void BuildReviewFiling_NewAttorneyCaseAssignment_WithSuffix_EmitsPersonNameSuffixText()
    {
        // Wire-level pin for the 2026-05-17 new-attorney Suffix silent-drop fix (parallel to
        // the AKA Suffix fix). Pre-fix: caseAssignmentValue builder emitted Given/Middle/Sur
        // but never PersonNameSuffixText, even though main-party caseParticipantValue + main
        // party CaseParticipantExt always did. Closes the silent-drop chain end-to-end.
        var sub = BuildMinimalCaseInit();
        sub.LeadDocument!.MetadataValues.Add(new FilingMetadataValue
        {
            Code = "NEW_ATTORNEY",
            ClassType = "caseAssignment",
            SubType = "attorney",
            ValueRestriction = "new-data",
            CaseAssignmentValue = new CaseAssignmentData
            {
                FirstName = "Jane",
                LastName = "Lawyer",
                NameSuffix = "Esq.",
                BarNumber = "555555",
                AssignmentRole = "ATT",
            }
        });

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        // Find the caseAssignmentValue → EntityPerson → PersonName → PersonNameSuffixText.
        var caEntityPerson = doc.Descendants()
            .First(e => e.Name.LocalName == "caseAssignmentValue")
            .Elements().First(e => e.Name.LocalName == "EntityPerson");
        var caPersonName = caEntityPerson.Elements().First(e => e.Name.LocalName == "PersonName");
        var caSuffix = caPersonName.Elements().FirstOrDefault(e => e.Name.LocalName == "PersonNameSuffixText");
        Assert.NotNull(caSuffix);
        Assert.Equal("Esq.", caSuffix!.Value);
    }

    [Fact]
    public void BuildReviewFiling_AlternateNameWithoutSuffix_OmitsPersonNameSuffixText()
    {
        // Sibling of the suffix-emit test: empty/null NameSuffix must produce no element
        // (consistent with how the builder treats every other optional name part).
        var sub = BuildMinimalCaseInit();
        sub.Parties[0].AlternateNames.Add(new AlternateName
        {
            Type = "AKA",
            FirstName = "Johnny",
            LastName = "Smith"
            // NameSuffix intentionally null
        });

        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);
        var doc = XDocument.Parse(xml);

        var caseParticipant = doc.Descendants(CpExt + "CaseParticipantExt").First();
        var akaPerson = caseParticipant.Elements(Nc + "EntityPerson").Skip(1).First();
        var akaPersonName = akaPerson.Element(Nc + "PersonName");
        Assert.NotNull(akaPersonName);
        Assert.Null(akaPersonName!.Element(Nc + "PersonNameSuffixText"));
    }
}
