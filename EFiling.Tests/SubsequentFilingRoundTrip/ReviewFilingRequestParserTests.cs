using EFiling.Providers.JTI.Parsers;
// Disambiguate from the catalog-level FilingType enum (Initiation/Subsequent) declared in
// CanonicalScenarios.cs. The submission-model FilingType uses values Initial/Subsequent.
using CoreFilingType = EFiling.Core.Enums.FilingType;

namespace EFiling.Tests.SubsequentFilingRoundTrip;

/// <summary>
/// Unit tests for <see cref="ReviewFilingRequestParser"/>.
///
/// <para>
/// Each test loads a named baseline scenario (via <see cref="SampleLoader"/>) and asserts
/// that the parser extracts specific fields correctly. Tests are organized by which
/// FilingSubmission aspect they verify — envelope, parties, documents, payment.
/// </para>
///
/// <para>
/// These tests drive parser correctness independent of the builder / round-trip. If a test
/// here fails but a round-trip test elsewhere passes (or vice versa), the failure localizes
/// cleanly to parse-side vs build-side.
/// </para>
/// </summary>
public class ReviewFilingRequestParserTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-001 — simplest Civil CI baseline — drives the minimum-viable parser.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_001_ParsesEnvelopeAndCoreFilingMessage_Scalars()
    {
        var xml = SampleLoader.LoadXmlText(CanonicalScenarios.GetById("CIV-INI-001"));
        var sub = ReviewFilingRequestParser.FromXml(xml);

        Assert.Equal("YOUR_IDENTIFICATIONID_HERE", sub.EfspReferenceId);
        Assert.Equal("YOUR_USERNAME_HERE", sub.SubmitterUsername);
        Assert.Equal(CoreFilingType.Initial, sub.FilingType);
    }

    [Fact]
    public void CIV_INI_001_ParsesCaseScalars()
    {
        var xml = SampleLoader.LoadXmlText(CanonicalScenarios.GetById("CIV-INI-001"));
        var sub = ReviewFilingRequestParser.FromXml(xml);

        Assert.Equal("411900", sub.CaseCategoryCode);
        Assert.Equal("421110", sub.CaseTypeCode);
        Assert.Equal("L10", sub.JurisdictionalGroundsCode);
        Assert.Equal(0m, sub.AmountInControversy);
        Assert.Equal("GIB", sub.LocationName); // from CaseCourt/OrganizationLocation/LocationName
        Assert.Equal("95747", sub.IncidentZipCode); // from incidentAddress/StructuredAddress/LocationPostalCode
    }

    [Fact]
    public void CIV_INI_001_ParsesParties()
    {
        var xml = SampleLoader.LoadXmlText(CanonicalScenarios.GetById("CIV-INI-001"));
        var sub = ReviewFilingRequestParser.FromXml(xml);

        Assert.Equal(2, sub.Parties.Count);

        var plaintiff = sub.Parties.Single(p => p.ReferenceId == "filedBy0");
        Assert.Equal("PLAIN", plaintiff.RoleCode);
        Assert.False(plaintiff.IsOrganization);
        Assert.Equal("Mark", plaintiff.FirstName);
        Assert.Equal("Smith", plaintiff.LastName);

        var defendant = sub.Parties.Single(p => p.ReferenceId == "filedAsTo0");
        Assert.Equal("DEF", defendant.RoleCode);
        Assert.False(defendant.IsOrganization);
        Assert.Equal("Stephen", defendant.FirstName);
        Assert.Equal("Williams", defendant.LastName);
    }

    [Fact]
    public void CIV_INI_001_ParsesParty_ContactInformation_OnFilingParty()
    {
        var xml = SampleLoader.LoadXmlText(CanonicalScenarios.GetById("CIV-INI-001"));
        var sub = ReviewFilingRequestParser.FromXml(xml);

        var plaintiff = sub.Parties.Single(p => p.ReferenceId == "filedBy0");
        Assert.NotNull(plaintiff.Contact);
        Assert.NotNull(plaintiff.Contact!.MailingAddress);
        Assert.Equal("1222 South Davis St.", plaintiff.Contact.MailingAddress!.Address1);
        Assert.Equal("Sacramento", plaintiff.Contact.MailingAddress.City);
        Assert.Equal("CA", plaintiff.Contact.MailingAddress.State);
        Assert.Equal("95747", plaintiff.Contact.MailingAddress.Zip);
        Assert.Equal("US", plaintiff.Contact.MailingAddress.Country);
        Assert.Equal("HM", plaintiff.Contact.MailingAddress.AddressType);
        Assert.Equal("USER_EMAIL_ADDRESS_HERE", plaintiff.Contact.Email);
    }

    [Fact]
    public void CIV_INI_001_ParsesDocuments_LeadAndConnected()
    {
        var xml = SampleLoader.LoadXmlText(CanonicalScenarios.GetById("CIV-INI-001"));
        var sub = ReviewFilingRequestParser.FromXml(xml);

        Assert.NotNull(sub.LeadDocument);
        Assert.Equal("doc0", sub.LeadDocument!.ReferenceId);
        Assert.Equal("401068", sub.LeadDocument.DocumentCode);
        Assert.Equal("2990", sub.LeadDocument.FileControlId);
        Assert.Equal("YOUR_URL_HERE", sub.LeadDocument.BinaryLocationUri);
        Assert.Equal(0, sub.LeadDocument.SequenceNumber);
        Assert.Equal("PLA", sub.LeadDocument.IdentificationSourceText);

        // Two connected documents in CIV-INI-001 baseline.
        Assert.Equal(2, sub.ConnectedDocuments.Count);
        Assert.Equal("doc1", sub.ConnectedDocuments[0].ReferenceId);
        Assert.Equal(1, sub.ConnectedDocuments[0].SequenceNumber);
        Assert.Equal("doc2", sub.ConnectedDocuments[1].ReferenceId);
        Assert.Equal(2, sub.ConnectedDocuments[1].SequenceNumber);
    }

    [Fact]
    public void CIV_INI_001_ParsesPartyDocumentAssociations()
    {
        var xml = SampleLoader.LoadXmlText(CanonicalScenarios.GetById("CIV-INI-001"));
        var sub = ReviewFilingRequestParser.FromXml(xml);

        // Baseline has 6 relatedParticipantDocuments: 3x FILEDBY (filedBy0 → doc0/1/2) +
        // 3x REFERS_TO (filedAsTo0 → doc0/1/2).
        Assert.Equal(6, sub.PartyDocumentAssociations.Count);

        var fileBy = sub.PartyDocumentAssociations.Where(a => a.AssociationType == "FILEDBY").ToList();
        Assert.Equal(3, fileBy.Count);
        Assert.All(fileBy, a => Assert.Equal("filedBy0", a.ParticipantRef));

        var refersTo = sub.PartyDocumentAssociations.Where(a => a.AssociationType == "REFERS_TO").ToList();
        Assert.Equal(3, refersTo.Count);
        Assert.All(refersTo, a => Assert.Equal("filedAsTo0", a.ParticipantRef));
    }

    [Fact]
    public void CIV_INI_001_ParsesPaymentMessage()
    {
        var xml = SampleLoader.LoadXmlText(CanonicalScenarios.GetById("CIV-INI-001"));
        var sub = ReviewFilingRequestParser.FromXml(xml);

        Assert.NotNull(sub.Payment);
        Assert.Equal("0", sub.Payment!.CustomerProfileId);
        Assert.Equal("0", sub.Payment.CustomerPaymentProfileId);
        Assert.Equal("ACH", sub.Payment.PaymentType);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-004 — Fee Waiver baseline — verifies extension-field parsing.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_004_ParsesFeeExemptionRequestType()
    {
        var xml = SampleLoader.LoadXmlText(CanonicalScenarios.GetById("CIV-INI-004"));
        var sub = ReviewFilingRequestParser.FromXml(xml);

        var plaintiff = sub.Parties.Single(p => p.ReferenceId == "filedBy0");
        Assert.Equal("FEE_WAIVER", plaintiff.FeeExemptionRequestType);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-011 — Interpreter + attorney — verifies efmInterpreterLanguage and
    // attorney BAR-number parsing in a multi-party scenario.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_011_ParsesInterpreterLanguage_AndAttorneyBarNumber()
    {
        var xml = SampleLoader.LoadXmlText(CanonicalScenarios.GetById("CIV-INI-011"));
        var sub = ReviewFilingRequestParser.FromXml(xml);

        var plaintiff = sub.Parties.Single(p => p.ReferenceId == "filedBy0");
        Assert.Equal("109", plaintiff.InterpreterLanguage);

        var attorney = sub.Parties.Single(p => p.ReferenceId == "attorney0");
        Assert.Equal("ATT", attorney.RoleCode);
        Assert.Equal("712345", attorney.BarNumber);
        Assert.NotNull(attorney.Contact);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-006 — Gov Ent Exempt with organization plaintiff.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_006_ParsesOrganizationPlaintiff_WithGovtEntityExemption()
    {
        var xml = SampleLoader.LoadXmlText(CanonicalScenarios.GetById("CIV-INI-006"));
        var sub = ReviewFilingRequestParser.FromXml(xml);

        var plaintiff = sub.Parties.Single(p => p.ReferenceId == "filedBy0");
        Assert.True(plaintiff.IsOrganization);
        Assert.Equal("County of Placer", plaintiff.OrganizationName);
        Assert.Equal("GOVT_ENTITY", plaintiff.FeeExemptionRequestType);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-010 — eService + Self-Rep — verifies eService boolean parsing.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_010_ParsesEService_OnSelfRepPlaintiff()
    {
        var xml = SampleLoader.LoadXmlText(CanonicalScenarios.GetById("CIV-INI-010"));
        var sub = ReviewFilingRequestParser.FromXml(xml);

        var plaintiff = sub.Parties.Single(p => p.ReferenceId == "filedBy0");
        Assert.True(plaintiff.EService);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Malformed input — parser throws with a helpful message, doesn't silently
    // return a blank submission.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FromXml_MissingEnvelope_Throws()
    {
        var invalidXml = "<root><child/></root>";

        var ex = Assert.Throws<InvalidOperationException>(
            () => ReviewFilingRequestParser.FromXml(invalidXml));

        Assert.Contains("Body", ex.Message);
    }

    [Fact]
    public void FromXml_MissingReviewFilingRequestMessage_Throws()
    {
        var invalidXml = @"<SOAP-ENV:Envelope xmlns:SOAP-ENV=""http://schemas.xmlsoap.org/soap/envelope/"">
                             <SOAP-ENV:Body/>
                           </SOAP-ENV:Envelope>";

        var ex = Assert.Throws<InvalidOperationException>(
            () => ReviewFilingRequestParser.FromXml(invalidXml));

        Assert.Contains("ReviewFilingRequestMessage", ex.Message);
    }
}
