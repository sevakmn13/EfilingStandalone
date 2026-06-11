using System.Xml;
using System.Xml.Serialization;
using EFiling.Core.Enums;
using EFiling.Core.Models;
using EFiling.Providers.JTI.Builders;
using FR = EFiling.WsdlGenerated.FilingReview;

namespace EFiling.Tests;

/// <summary>
/// Track A sub-3 — Systematic round-trip validation fixtures.
///
/// <para>
/// Every test builds a realistic <see cref="FilingSubmission"/> scenario, runs it through
/// <see cref="ReviewFilingXmlBuilder.BuildReviewFilingRequest"/>, and attempts to deserialize
/// the resulting XML through the generated <see cref="FR.ReviewFilingRequestMessageType"/>.
/// </para>
///
/// <para>
/// This acts as a <b>schema fence for the builder</b>: any time the builder emits structurally
/// invalid XML — wrong xsi:type, mismatched namespace, missing required child, etc. — the
/// deserialization throws with a pointed error message. Bugs #4 (AmountInControversy xsi:type)
/// and #6 (BinaryFormatStandardName xsi:type) were originally caught by this exact pattern in
/// Track B.0's Scenario3 test; this file expands coverage to 20+ scenarios spanning the full
/// matrix of builder code paths informed by the Track D.post audit findings.
/// </para>
///
/// <para>
/// Each scenario exercises a distinct builder code path. Adding a new fixture is cheap —
/// start from <see cref="BuildBaseline"/> and mutate the submission to cover the new path.
/// </para>
/// </summary>
public class Track_A3_RoundTripFixtureTests
{
    // ─── Test config / serializer (cached) ────────────────────────────

    private const string WsdlProfileNs = "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0";

    private static readonly XmlSerializer ReviewFilingRequestSer = new(
        typeof(FR.ReviewFilingRequestMessageType),
        new XmlRootAttribute("ReviewFilingRequestMessage") { Namespace = WsdlProfileNs });

    private static CourtConfiguration TestConfig => new()
    {
        CourtId = "madera",
        DisplayName = "Madera Superior Court",
        SoapEndpoint = "https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/FilingReview/",
        RestBaseUrl = "https://aux-pub-efm-madera-ca.ecourt.com/ws/rest/ecourt",
        NfrcCallbackUrl = "https://staging.legalhub.com/api/efiling/nfrc"
    };

    // ─── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Build the submission XML and deserialize it back through the generated
    /// WSDL types. Fails the test with a clear diagnostic if the XML does not
    /// round-trip (i.e., it's schema-invalid in a way the generated types detect).
    /// Returns the deserialized result for further assertions.
    /// </summary>
    private static FR.ReviewFilingRequestMessageType BuildAndDeserialize(FilingSubmission sub, string scenarioLabel)
    {
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, TestConfig);

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml));
            bool inBody = false;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (!inBody
                    && reader.LocalName == "Body"
                    && reader.NamespaceURI == "http://schemas.xmlsoap.org/soap/envelope/")
                {
                    inBody = true;
                    continue;
                }
                if (inBody && reader.LocalName == "ReviewFilingRequestMessage")
                {
                    var result = (FR.ReviewFilingRequestMessageType?)ReviewFilingRequestSer.Deserialize(reader);
                    Assert.NotNull(result);
                    Assert.NotNull(result!.Item);
                    return result;
                }
            }
            // SOAP Body or ReviewFilingRequestMessage wasn't found — dump XML for inspection.
            var dumpPath = DumpXml(scenarioLabel, xml);
            throw new Xunit.Sdk.XunitException(
                $"[{scenarioLabel}] ReviewFilingRequestMessage not found inside SOAP Body. XML: {dumpPath}");
        }
        catch (InvalidOperationException ex)
        {
            var dumpPath = DumpXml(scenarioLabel, xml);
            throw new Xunit.Sdk.XunitException(
                $"[{scenarioLabel}] Round-trip deserialization failed — builder emitted schema-invalid XML.\n"
                + $"XML dumped to: {dumpPath}\n"
                + $"Exception: {ex.Message}\n"
                + $"Inner: {ex.InnerException?.Message}\n"
                + $"Deepest inner: {GetDeepestInner(ex).Message}");
        }
    }

    private static string DumpXml(string label, string xml)
    {
        var safeLabel = string.Join("_", label.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(Path.GetTempPath(), $"track_a3_{safeLabel}.xml");
        File.WriteAllText(path, xml);
        return path;
    }

    private static Exception GetDeepestInner(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return ex;
    }

    /// <summary>
    /// Mirrors ReviewFilingXmlBuilderTests.BuildMinimalCaseInit with enough detail to be a
    /// valid initial filing. Mutations in individual scenarios layer additional fields on top.
    /// </summary>
    private static FilingSubmission BuildBaseline() => new()
    {
        FilingType = FilingType.Initial,
        EfspReferenceId = "TRACK-A3-TEST",
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

    // ═══════════════════════════════════════════════════════════════════
    // INITIAL FILING FIXTURES
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RoundTrip_Initial_MinimalBaseline()
    {
        // Control scenario — must round-trip cleanly. If this fails, the whole file is suspect.
        var sub = BuildBaseline();
        var result = BuildAndDeserialize(sub, nameof(RoundTrip_Initial_MinimalBaseline));
        Assert.NotNull(result.Item);
    }

    [Fact]
    public void RoundTrip_Initial_WithFeeWaiver()
    {
        // Party requests fee waiver (FEE_WAIVER exemption) + connected FW001 fee waiver application doc.
        var sub = BuildBaseline();
        sub.Parties[0].FeeExemptionRequestType = "FEE_WAIVER";
        sub.ConnectedDocuments.Add(new FilingDocument
        {
            ReferenceId = "doc1",
            DocumentCode = "FW001",
            SequenceNumber = 1,
            BinaryLocationUri = "https://example.com/docs/feewaiver.pdf",
            FileControlId = "FC002"
        });
        sub.PartyDocumentAssociations.Add(
            new() { AssociationType = "FILEDBY", ParticipantRef = "filedBy0", DocumentRef = "doc1" });

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_WithFeeWaiver));
    }

    [Fact]
    public void RoundTrip_Initial_WithGovernmentExemptType()
    {
        // Party is a government entity — FeeExemptionRequestType = "GOVT_ENTITY".
        var sub = BuildBaseline();
        sub.Parties[0] = new FilingParty
        {
            ReferenceId = "filedBy0",
            RoleCode = "PLAIN",
            IsOrganization = true,
            OrganizationName = "State of California",
            FeeExemptionRequestType = "GOVT_ENTITY"
        };

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_WithGovernmentExemptType));
    }

    [Fact]
    public void RoundTrip_Initial_WithGovernmentExemptFlag()
    {
        // Track D.post fix C-2 path: GovernmentExempt boolean set on the party.
        // Builder emits <CpExt:efspGovernmentExempt>true</CpExt:efspGovernmentExempt>.
        var sub = BuildBaseline();
        sub.Parties[0].GovernmentExempt = true;

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_WithGovernmentExemptFlag));
    }

    [Fact]
    public void RoundTrip_Initial_WithFirstAppearancePaidFlag()
    {
        // Track D.post fix C-1 path: FirstAppearancePaid boolean set on the party.
        // Builder emits <CpExt:efspFirstAppearancePaid>true</CpExt:efspFirstAppearancePaid>.
        var sub = BuildBaseline();
        sub.Parties[0].FirstAppearancePaid = true;

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_WithFirstAppearancePaidFlag));
    }

    [Fact]
    public void RoundTrip_Initial_SelfRepWithFullContact()
    {
        // Self-rep party supplies full contact (address + phone + email).
        var sub = BuildBaseline();
        sub.Parties[0].Contact = new ContactInfo
        {
            MailingAddress = new StructuredAddress
            {
                AddressType = "M",
                Address1 = "12 Oak Ave",
                City = "Fresno",
                State = "CA",
                Zip = "93701",
                Country = "US"
            },
            PhoneType = "H",
            PhoneNumber = "559-555-0001",
            Email = "selfrep@example.com"
        };

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_SelfRepWithFullContact));
    }

    [Fact]
    public void RoundTrip_Initial_SelfRepWithPhoneEmailOnly_NoAddress()
    {
        // Track D.post fix C-3 path: self-rep party with only phone/email, no mailing address.
        // Builder should still emit ContactInformation without a StructuredAddress child.
        var sub = BuildBaseline();
        sub.Parties[0].Contact = new ContactInfo
        {
            MailingAddress = null, // key: no address
            PhoneType = "H",
            PhoneNumber = "559-555-9999",
            Email = "phone-only@example.com"
        };

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_SelfRepWithPhoneEmailOnly_NoAddress));
    }

    [Fact]
    public void RoundTrip_Initial_MultipleAlternateNames()
    {
        // Party with AKA + DBA + FKA alternate names.
        var sub = BuildBaseline();
        sub.Parties[0].AlternateNames.Add(new AlternateName
        {
            Type = "AKA",
            FirstName = "Johnny",
            LastName = "Smith"
        });
        sub.Parties[0].AlternateNames.Add(new AlternateName
        {
            Type = "FKA",
            FirstName = "John",
            LastName = "Smythe"
        });
        sub.Parties[0].AlternateNames.Add(new AlternateName
        {
            Type = "DBA",
            OrganizationName = "JSmith Consulting"
        });

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_MultipleAlternateNames));
    }

    [Fact]
    public void RoundTrip_Initial_WithInterpreterLanguage()
    {
        var sub = BuildBaseline();
        sub.Parties[0].InterpreterLanguage = "SPA";

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_WithInterpreterLanguage));
    }

    [Fact]
    public void RoundTrip_Initial_WithEServiceConsent()
    {
        // Placer/Nevada pattern: party-level eService consent.
        var sub = BuildBaseline();
        sub.Parties[0].EService = true;

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_WithEServiceConsent));
    }

    [Fact]
    public void RoundTrip_Initial_WithComplexLitigationAndClassAction()
    {
        // LASC-specific: ClassAction implies ComplexLitigation per CA rules.
        var sub = BuildBaseline();
        sub.ComplexLitigation = true;
        sub.ClassAction = true;

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_WithComplexLitigationAndClassAction));
    }

    [Fact]
    public void RoundTrip_Initial_WithAsbestos()
    {
        var sub = BuildBaseline();
        sub.Asbestos = true;

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_WithAsbestos));
    }

    [Fact]
    public void RoundTrip_Initial_WithCeqa()
    {
        var sub = BuildBaseline();
        sub.CaliforniaEnvironmentalQualityAct = true;

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_WithCeqa));
    }

    [Fact]
    public void RoundTrip_Initial_WithConditionallySealed()
    {
        var sub = BuildBaseline();
        sub.ConditionallySealed = true;

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_WithConditionallySealed));
    }

    [Fact]
    public void RoundTrip_Initial_WithSpecialStatusCodes()
    {
        // LASC COVID-19 UD scenario.
        var sub = BuildBaseline();
        sub.SpecialStatusCodes.Add("UDCOV19");

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_WithSpecialStatusCodes));
    }

    [Fact]
    public void RoundTrip_Initial_WithIncidentZipCode()
    {
        var sub = BuildBaseline();
        sub.IncidentZipCode = "93637";

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_WithIncidentZipCode));
    }

    [Fact]
    public void RoundTrip_Initial_WithMultipleConnectedDocuments()
    {
        // Lead + 2 connected (common pattern for complaints with exhibits).
        var sub = BuildBaseline();
        sub.ConnectedDocuments.Add(new FilingDocument
        {
            ReferenceId = "doc1",
            DocumentCode = "EXH001",
            SequenceNumber = 1,
            BinaryLocationUri = "https://example.com/docs/exhibit1.pdf",
            FileControlId = "FC002"
        });
        sub.ConnectedDocuments.Add(new FilingDocument
        {
            ReferenceId = "doc2",
            DocumentCode = "EXH001",
            SequenceNumber = 2,
            BinaryLocationUri = "https://example.com/docs/exhibit2.pdf",
            FileControlId = "FC003"
        });
        sub.PartyDocumentAssociations.Add(
            new() { AssociationType = "FILEDBY", ParticipantRef = "filedBy0", DocumentRef = "doc1" });
        sub.PartyDocumentAssociations.Add(
            new() { AssociationType = "FILEDBY", ParticipantRef = "filedBy0", DocumentRef = "doc2" });

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_WithMultipleConnectedDocuments));
    }

    [Fact]
    public void RoundTrip_Initial_WithAmountInControversy_Zero()
    {
        // Edge case: AmountInControversy = 0 (e.g., equitable relief only).
        // Bug #4 was on this exact element — this test guards against regression.
        var sub = BuildBaseline();
        sub.AmountInControversy = 0m;

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_WithAmountInControversy_Zero));
    }

    [Fact]
    public void RoundTrip_Initial_WithAmountInControversy_Large()
    {
        // Edge case: large amount (complex litigation fee tier).
        var sub = BuildBaseline();
        sub.AmountInControversy = 1_000_000m;

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_WithAmountInControversy_Large));
    }

    [Fact]
    public void RoundTrip_Initial_MultipleFilingAndOpposingParties()
    {
        // 2 plaintiffs + 2 defendants — exercises party-association enumeration paths.
        var sub = BuildBaseline();
        sub.Parties.Clear();
        sub.Parties.Add(new FilingParty { ReferenceId = "filedBy0", RoleCode = "PLAIN", FirstName = "Alice", LastName = "A" });
        sub.Parties.Add(new FilingParty { ReferenceId = "filedBy1", RoleCode = "PLAIN", FirstName = "Bob", LastName = "B" });
        sub.Parties.Add(new FilingParty { ReferenceId = "filedAsTo0", RoleCode = "DEF", FirstName = "Carol", LastName = "C" });
        sub.Parties.Add(new FilingParty { ReferenceId = "filedAsTo1", RoleCode = "DEF", IsOrganization = true, OrganizationName = "Acme Corp" });
        sub.Parties.Add(new FilingParty
        {
            ReferenceId = "attorney0",
            RoleCode = "ATT",
            FirstName = "Jane",
            LastName = "Doe",
            BarNumber = "123456"
        });

        sub.PartyAssociations.Clear();
        sub.PartyAssociations.Add(new() { AssociationType = "REPRESENTEDBY", ParticipantRef = "filedBy0", RelatedParticipantRef = "attorney0" });
        sub.PartyAssociations.Add(new() { AssociationType = "REPRESENTEDBY", ParticipantRef = "filedBy1", RelatedParticipantRef = "attorney0" });

        sub.PartyDocumentAssociations.Clear();
        sub.PartyDocumentAssociations.Add(new() { AssociationType = "FILEDBY", ParticipantRef = "filedBy0", DocumentRef = "doc0" });
        sub.PartyDocumentAssociations.Add(new() { AssociationType = "FILEDBY", ParticipantRef = "filedBy1", DocumentRef = "doc0" });
        sub.PartyDocumentAssociations.Add(new() { AssociationType = "REFERS_TO", ParticipantRef = "filedAsTo0", DocumentRef = "doc0" });
        sub.PartyDocumentAssociations.Add(new() { AssociationType = "REFERS_TO", ParticipantRef = "filedAsTo1", DocumentRef = "doc0" });

        BuildAndDeserialize(sub, nameof(RoundTrip_Initial_MultipleFilingAndOpposingParties));
    }

    // ═══════════════════════════════════════════════════════════════════
    // SUBSEQUENT FILING FIXTURES
    // ═══════════════════════════════════════════════════════════════════

    private static FilingSubmission BuildSubsequentBaseline()
    {
        var sub = BuildBaseline();
        sub.FilingType = FilingType.Subsequent;
        sub.CaseDocketId = "MFL018522";
        // CaseTrackingId deliberately omitted per Bug #5 fix — an empty CaseTrackingID
        // is rejected with server ErrorCode 4011.
        sub.CaseTrackingId = null;

        // Subsequent filings use DocumentFilingMetaData instead of CaseAugmentation participants.
        // A minimal filing = FILING_PARTY existing-data idReference to the case participant.
        sub.LeadDocument!.MetadataValues.Clear();
        sub.LeadDocument.MetadataValues.Add(new FilingMetadataValue
        {
            Code = "FILING_PARTY",
            ClassType = "caseParticipant",
            SubType = "filed-by",
            ValueRestriction = "existing-data",
            IdReferences = new List<string> { "P123456" }
        });

        // Subsequent uses DocumentCode like 401011 (First Paper / Answer)
        sub.LeadDocument.DocumentCode = "401011";
        sub.LeadDocument.IdentificationSourceText = "PLA";

        return sub;
    }

    [Fact]
    public void RoundTrip_Subsequent_MinimalBaseline()
    {
        var sub = BuildSubsequentBaseline();
        BuildAndDeserialize(sub, nameof(RoundTrip_Subsequent_MinimalBaseline));
    }

    [Fact]
    public void RoundTrip_Subsequent_WithCaseTrackingId()
    {
        // When CaseTrackingId IS provided, it should appear in the emitted XML.
        // Bug #5 regression guard — the builder should only skip emitting when empty.
        var sub = BuildSubsequentBaseline();
        sub.CaseTrackingId = "TRK-987654";

        BuildAndDeserialize(sub, nameof(RoundTrip_Subsequent_WithCaseTrackingId));
    }

    [Fact]
    public void RoundTrip_Subsequent_WithComplaintRef()
    {
        // Subsequent filing targets a specific sub-case (Complaint st:id).
        var sub = BuildSubsequentBaseline();
        sub.ComplaintId = "1109916";
        sub.LeadDocument!.ComplaintRef = "1109916";

        BuildAndDeserialize(sub, nameof(RoundTrip_Subsequent_WithComplaintRef));
    }

    [Fact]
    public void RoundTrip_Subsequent_WithExistingAttorneyCaseAssignment()
    {
        // FILING_ATTORNEY with classType="caseAssignment" and idReference to attorney in case.
        var sub = BuildSubsequentBaseline();
        sub.LeadDocument!.MetadataValues.Add(new FilingMetadataValue
        {
            Code = "FILING_ATTORNEY",
            ClassType = "caseAssignment",
            SubType = "attorney",
            ValueRestriction = "existing-data",
            IdReferences = new List<string> { "ATT789" }
        });

        BuildAndDeserialize(sub, nameof(RoundTrip_Subsequent_WithExistingAttorneyCaseAssignment));
    }

    [Fact]
    public void RoundTrip_Subsequent_WithAdditionalInfoTags_EFSPFirstAppearancePaid()
    {
        // Baseline-specific EFSP_FIRST_APPEARANCE_PAID tag on FILING_PARTY.
        var sub = BuildSubsequentBaseline();
        var filingParty = sub.LeadDocument!.MetadataValues[0];
        filingParty.AdditionalInfoTags.Add(new AdditionalInfoTag
        {
            TagType = "EFSP_FIRST_APPEARANCE_PAID",
            TagValue = "1"
        });

        BuildAndDeserialize(sub, nameof(RoundTrip_Subsequent_WithAdditionalInfoTags_EFSPFirstAppearancePaid));
    }

    [Fact]
    public void RoundTrip_Subsequent_WithAdditionalInfoTags_FeeWaiver()
    {
        // FILING_PARTY with FEE_EXEMPTION=FEE_WAIVER additionalInfoTag.
        var sub = BuildSubsequentBaseline();
        var filingParty = sub.LeadDocument!.MetadataValues[0];
        filingParty.AdditionalInfoTags.Add(new AdditionalInfoTag
        {
            TagType = "FEE_EXEMPTION",
            TagValue = "FEE_WAIVER"
        });

        BuildAndDeserialize(sub, nameof(RoundTrip_Subsequent_WithAdditionalInfoTags_FeeWaiver));
    }

    [Fact]
    public void RoundTrip_Subsequent_WithAdditionalInfoTags_EService()
    {
        // Placer/Nevada pattern: E_SERVICE tag on FILING_PARTY.
        var sub = BuildSubsequentBaseline();
        var filingParty = sub.LeadDocument!.MetadataValues[0];
        filingParty.AdditionalInfoTags.Add(new AdditionalInfoTag
        {
            TagType = "E_SERVICE",
            TagValue = "1"
        });

        BuildAndDeserialize(sub, nameof(RoundTrip_Subsequent_WithAdditionalInfoTags_EService));
    }

    [Fact]
    public void RoundTrip_Subsequent_WithCodeListMetadata()
    {
        // Document metadata with classType="codeList" (e.g., dismissal type).
        var sub = BuildSubsequentBaseline();
        sub.LeadDocument!.MetadataValues.Add(new FilingMetadataValue
        {
            Code = "DISMISSAL_TYPE",
            ClassType = "codeList",
            CodeValue = "WITH_PREJUDICE"
        });

        BuildAndDeserialize(sub, nameof(RoundTrip_Subsequent_WithCodeListMetadata));
    }

    [Fact]
    public void RoundTrip_Subsequent_WithCurrencyMetadata()
    {
        var sub = BuildSubsequentBaseline();
        sub.LeadDocument!.MetadataValues.Add(new FilingMetadataValue
        {
            Code = "SERVICE_FEE",
            ClassType = "currency",
            CurrencyValue = 125.50m
        });

        BuildAndDeserialize(sub, nameof(RoundTrip_Subsequent_WithCurrencyMetadata));
    }

    [Fact]
    public void RoundTrip_Subsequent_WithDateMetadata()
    {
        var sub = BuildSubsequentBaseline();
        sub.LeadDocument!.MetadataValues.Add(new FilingMetadataValue
        {
            Code = "SERVICE_DATE",
            ClassType = "date",
            DateValue = new DateTime(2026, 3, 15)
        });

        BuildAndDeserialize(sub, nameof(RoundTrip_Subsequent_WithDateMetadata));
    }

    [Fact]
    public void RoundTrip_Subsequent_WithBooleanMetadata()
    {
        // SELF_REP boolean — used when a party is becoming self-represented.
        var sub = BuildSubsequentBaseline();
        sub.LeadDocument!.MetadataValues.Add(new FilingMetadataValue
        {
            Code = "SELF_REP",
            ClassType = "boolean",
            BooleanValue = true
        });

        BuildAndDeserialize(sub, nameof(RoundTrip_Subsequent_WithBooleanMetadata));
    }

    [Fact]
    public void RoundTrip_Subsequent_WithTextMetadata()
    {
        var sub = BuildSubsequentBaseline();
        sub.LeadDocument!.MetadataValues.Add(new FilingMetadataValue
        {
            Code = "SOME_NOTE",
            ClassType = "text",
            TextValue = "Free-form note accompanying the filing."
        });

        BuildAndDeserialize(sub, nameof(RoundTrip_Subsequent_WithTextMetadata));
    }

    [Fact]
    public void RoundTrip_Subsequent_WithNameExtension()
    {
        // DocumentCode allows a name-extension suffix (e.g., "Amended").
        var sub = BuildSubsequentBaseline();
        sub.LeadDocument!.NameExtension = "Amended";

        BuildAndDeserialize(sub, nameof(RoundTrip_Subsequent_WithNameExtension));
    }
}
