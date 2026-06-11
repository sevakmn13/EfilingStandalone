using System.Xml.Linq;
using EFiling.Core.Enums;
using EFiling.Core.Models;
using EFiling.Providers.JTI.Soap;

namespace EFiling.Providers.JTI.Parsers;

/// <summary>
/// Reverse-direction mapper — parses a ReviewFilingRequestMessage SOAP XML into a
/// <see cref="FilingSubmission"/>. Complement to <see cref="Builders.ReviewFilingXmlBuilder"/>.
///
/// <para>
/// <b>Coverage (as of 2026-04-22)</b>: minimum viable — targets <c>CIV-INI-001</c>
/// ("New Case (Civil Limited) Sample") shape. Supports:
/// <list type="bullet">
///   <item>Envelope + ReviewFilingRequestMessage body</item>
///   <item>CoreFilingMessage scalars: EfspReferenceId, SubmitterUsername</item>
///   <item>Case scalars: CaseCategoryCode, CaseTypeCode, JurisdictionalGroundsCode, AmountInControversy</item>
///   <item>Location: LocationName (from CaseCourt/OrganizationLocation/LocationName)</item>
///   <item>Parties (person + organization variants): ReferenceId, RoleCode, names, AKA alternates</item>
///   <item>Attorney parties: BAR number, firm, contact info</item>
///   <item>Party participant extensions: efmFeeExemptionRequestType, efmInterpreterLanguage,
///         efspGovernmentExempt, eService, partySubType</item>
///   <item>Documents (lead + connected): DocumentCode, FileControlId, BinaryLocationUri, SequenceNumber, IdentificationSourceText</item>
///   <item>PartyDocumentAssociations (FILEDBY, REFERS_TO) and PartyAssociations (REPRESENTEDBY)</item>
///   <item>Payment: customerProfileId, customerPaymentProfileId, paymentType</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Not supported</b> (silently dropped — not a correctness issue as long as the baseline
/// under test doesn't use them; expand when a scenario needs them):
/// <list type="bullet">
///   <item>Subsequent-filing classType/tagType meta-grammar (CIV-SUB-*, FAM-SUB-*, etc.)</item>
///   <item>CaseDocketID / CaseTrackingID / ComplaintId (subsequent filing markers)</item>
///   <item>No-fee-case / special-status / premise-address / complex-litigation flags</item>
///   <item>Interpreter / case-assignment / complex metadata classes</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Round-trip contract</b>: <c>Parse → Build → Parse</c> is expected to be idempotent for
/// scenarios within the supported coverage. <c>Build → Parse → Build</c> is NOT necessarily
/// equivalent because the builder generates fresh EFSP-scoped values (timestamps, GUIDs).
/// Round-trip tests must load a known sample into the parser to get a stable starting point.
/// </para>
/// </summary>
public static class ReviewFilingRequestParser
{
    // ─── Namespace shortcuts ─────────────────────────────────────────
    private static readonly XNamespace Env = SoapEnvelopeBuilder.NsSoapEnv;
    private static readonly XNamespace Nc = SoapEnvelopeBuilder.NsNiemCore;
    private static readonly XNamespace Ecf = SoapEnvelopeBuilder.NsCommonTypes;
    private static readonly XNamespace Civil = SoapEnvelopeBuilder.NsCivilCase;
    private static readonly XNamespace CivExt = SoapEnvelopeBuilder.NsJtiCivilCaseExt;
    private static readonly XNamespace Cfm = SoapEnvelopeBuilder.NsCoreFilingMessage;
    private static readonly XNamespace CfmExt = SoapEnvelopeBuilder.NsJtiCoreFilingExt;
    private static readonly XNamespace Pay = SoapEnvelopeBuilder.NsPaymentMessage;
    private static readonly XNamespace PayExt = SoapEnvelopeBuilder.NsJtiPaymentExt;
    private static readonly XNamespace CpExt = SoapEnvelopeBuilder.NsJtiCaseParticipantExt;
    private static readonly XNamespace St = SoapEnvelopeBuilder.NsStructures;
    private static readonly XNamespace AddrExt = SoapEnvelopeBuilder.NsJtiStructuredAddressExt;
    private static readonly XNamespace PhoneExt = SoapEnvelopeBuilder.NsJtiTelephoneNumberExt;
    private static readonly XNamespace Jxdm = SoapEnvelopeBuilder.NsJxdm;
    // Subsequent-filing specific namespaces — mirror the builder's DfMeta / DfValue /
    // ContactValueNs / CaseAssign constants. DfMeta is the DocumentFilingMetaData wrapper
    // namespace; DfValue contains the <code>/<classType>/<subType>/<valueRestriction>
    // descriptor children; ContactValueNs holds the flat contactValue fields (audit C-2
    // Bug B wire contract); CaseAssign holds the caseAssignmentValue AssignmentRole.
    private static readonly XNamespace DfMeta = SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData;
    private static readonly XNamespace DfValue = SoapEnvelopeBuilder.NsJtiDocumentValue;
    private static readonly XNamespace ContactValueNs = SoapEnvelopeBuilder.NsJtiContactValue;
    private static readonly XNamespace CaseAssign = SoapEnvelopeBuilder.NsJtiCaseAssignmentType;
    // CourtEventJudgmentNs (Step #15) — holds the <judgmentId> child element nested inside
    // the <ns9:judgments> wrapper (which lives in DfMeta). Judgment is the only classType
    // where wrapper + content namespaces split. See ReviewFilingXmlBuilder.cs and
    // STEP15_JUDGMENT_AUDIT.md §2 for the wire-shape rationale.
    private static readonly XNamespace CourtEventJudgmentNs = SoapEnvelopeBuilder.NsJtiCourtEventJudgment;

    /// <summary>
    /// Parse a ReviewFilingRequest XML string into a <see cref="FilingSubmission"/>.
    /// Throws <see cref="InvalidOperationException"/> if the document doesn't look like a
    /// ReviewFilingRequestMessage (missing envelope, missing body, etc.).
    /// </summary>
    public static FilingSubmission FromXml(string xml)
        => FromXml(XDocument.Parse(xml));

    /// <summary>
    /// Parse a ReviewFilingRequest <see cref="XDocument"/> into a <see cref="FilingSubmission"/>.
    /// </summary>
    public static FilingSubmission FromXml(XDocument doc)
    {
        var body = doc.Root?.Element(Env + "Body")
            ?? throw new InvalidOperationException("Envelope has no Body element.");

        // The ReviewFilingRequestMessage wrapper lives under Body; use LocalName-only lookup
        // because the wsdl:namespace prefix varies ("ns13" in baseline, could differ elsewhere).
        var reviewMsg = body.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "ReviewFilingRequestMessage")
            ?? throw new InvalidOperationException("Body has no ReviewFilingRequestMessage.");

        var cfm = reviewMsg.Element(Cfm + "CoreFilingMessage")
            ?? throw new InvalidOperationException("ReviewFilingRequestMessage has no CoreFilingMessage.");

        var sub = new FilingSubmission();

        ParseCoreFilingMessage(cfm, sub);
        ParsePaymentMessage(reviewMsg, sub);

        return sub;
    }

    // ─── CoreFilingMessage ───────────────────────────────────────────

    private static void ParseCoreFilingMessage(XElement cfm, FilingSubmission sub)
    {
        // EfspReferenceId — from DocumentIdentification/IdentificationID.
        sub.EfspReferenceId = cfm.Element(Nc + "DocumentIdentification")
            ?.Element(Nc + "IdentificationID")?.Value ?? string.Empty;

        // SubmitterUsername — from DocumentSubmitter/EntityPerson/PersonName/PersonFullName.
        sub.SubmitterUsername = cfm.Element(Nc + "DocumentSubmitter")
            ?.Element(Ecf + "EntityPerson")
            ?.Element(Nc + "PersonName")
            ?.Element(Nc + "PersonFullName")?.Value;

        // FilingType — prefer <CfmExt:eFilingCaseFilingType> when present; fall back to detect
        // subsequent filing via presence of CaseDocketID in the case element.
        var filingTypeText = cfm.Element(CfmExt + "eFilingCaseFilingType")?.Value;
        sub.FilingType = string.Equals(filingTypeText, "SUBSEQUENT", StringComparison.OrdinalIgnoreCase)
            ? FilingType.Subsequent
            : FilingType.Initial;

        // MessageToClerk (optional).
        sub.MessageToClerk = cfm.Element(CfmExt + "messageToClerk")?.Value;

        // Case element and its subtree.
        var caseEl = cfm.Element(Nc + "Case");
        if (caseEl != null)
        {
            ParseCase(caseEl, sub);
        }

        // Documents — FilingLeadDocument (single) + FilingConnectedDocument (zero-or-more).
        var leadDoc = cfm.Element(Cfm + "FilingLeadDocument");
        if (leadDoc != null)
        {
            sub.LeadDocument = ParseDocument(leadDoc);
        }

        foreach (var connDoc in cfm.Elements(Cfm + "FilingConnectedDocument"))
        {
            sub.ConnectedDocuments.Add(ParseDocument(connDoc));
        }
    }

    // ─── Case + CaseAugmentation ─────────────────────────────────────

    private static void ParseCase(XElement caseEl, FilingSubmission sub)
    {
        sub.CaseCategoryCode = caseEl.Element(Nc + "CaseCategoryText")?.Value;
        sub.CaseTypeCode = caseEl.Element(CivExt + "CaseTypeText")?.Value;
        sub.JurisdictionalGroundsCode = caseEl.Element(Civil + "JurisdictionalGroundsCode")?.Value;

        // Subsequent-filing markers (if present — drives filing type detection when
        // eFilingCaseFilingType is absent).
        sub.CaseDocketId = caseEl.Element(Nc + "CaseDocketID")?.Value;
        sub.CaseTrackingId = caseEl.Element(Nc + "CaseTrackingID")?.Value;
        // ComplaintId — baseline wire emits <CivExt:Complaint st:id="..."/> as a sibling of
        // CaseDocketID. This is a reference to the sub-case's complaint record in the JTI
        // court system. Controller maps this into the forward path via CourtFilingController's
        // BuildSubmissionFromCreateModel (line ~474 reads model.ComplaintId).
        var complaintEl = caseEl.Element(CivExt + "Complaint");
        sub.ComplaintId = complaintEl?.Attribute(St + "id")?.Value;
        if (!string.IsNullOrEmpty(sub.CaseDocketId))
            sub.FilingType = FilingType.Subsequent;

        // AmountInControversy — numeric content of <Civil:AmountInControversy>.
        var amountText = caseEl.Element(Civil + "AmountInControversy")?.Value;
        if (decimal.TryParse(amountText, out var amount))
            sub.AmountInControversy = amount;

        // ClassActionIndicator (CivilCase base).
        sub.ClassAction = string.Equals(caseEl.Element(Civil + "ClassActionIndicator")?.Value, "true",
            StringComparison.OrdinalIgnoreCase);

        // Extension boolean flags (CivilCaseTypeExt).
        sub.ComplexLitigation = string.Equals(caseEl.Element(CivExt + "complexLitigation")?.Value, "true",
            StringComparison.OrdinalIgnoreCase);
        sub.Asbestos = string.Equals(caseEl.Element(CivExt + "asbestos")?.Value, "true",
            StringComparison.OrdinalIgnoreCase);
        sub.CaliforniaEnvironmentalQualityAct = string.Equals(
            caseEl.Element(CivExt + "californiaEnvironmentalQualityAct")?.Value, "true",
            StringComparison.OrdinalIgnoreCase);
        // Audit F-1 fix: tri-state parse. When the element is absent, leave
        // ConditionallySealed as null (preserves "not set" semantics for CIV-INI-001 round-trip).
        // When present, parse to the literal bool value. Empty/non-bool values parse to null
        // (defensive — XSD type is xs:boolean so only "true"/"false"/"1"/"0" are valid).
        var condSealedEl = caseEl.Element(CivExt + "conditionallySealed");
        if (condSealedEl != null && bool.TryParse(condSealedEl.Value, out var condSealed))
            sub.ConditionallySealed = condSealed;
        else
            sub.ConditionallySealed = null;

        // Location (CivilCaseTypeExt)
        sub.LocationCode = caseEl.Element(CivExt + "location")?.Value;

        // CaseAugmentation → participants.
        var augmentation = caseEl.Element(Ecf + "CaseAugmentation");
        if (augmentation != null)
        {
            ParseParticipants(augmentation, sub);
        }

        // CaseAugmentationExt → court, associations, incident address.
        var augExt = caseEl.Element(CivExt + "CaseAugmentationExt");
        if (augExt != null)
        {
            ParseCaseAugmentationExt(augExt, sub);
        }
    }

    private static void ParseParticipants(XElement augmentation, FilingSubmission sub)
    {
        // All participants live under <p:CaseParticipantExt> at this level. The order of
        // filedBy* / filedAsTo* / attorney* in the wire is not normalized; we rely on the
        // st:id attribute to disambiguate role.
        foreach (var partEl in augmentation.Elements(CpExt + "CaseParticipantExt"))
        {
            // Audit H-3 fix: SF baselines emit id-only CaseParticipantExt references
            // inside CaseAugmentation as POINTERS to participants whose data lives elsewhere (in
            // DocumentFilingMetaData/idReferences). Example: CIV-SUB-003 line 19 emits
            // <CaseParticipantExt st:id="1493543"/> with no inline fields. Creating an empty
            // FilingParty for such a reference would cause the builder to emit a bogus
            // <CaseParticipantExt> with empty RoleCode and empty EntityPerson — not round-trip
            // equivalent. Skip these: the real data is round-tripped via metadata parsing.
            //
            // Filter xmlns declarations out via IsNamespaceDeclaration — they are XAttribute
            // instances but not semantic attributes for this check.
            if (!partEl.HasElements
                && partEl.Attributes()
                    .Where(a => !a.IsNamespaceDeclaration)
                    .All(a => a.Name.LocalName == "id"))
                continue;

            var refId = partEl.Attribute(St + "id")?.Value ?? string.Empty;
            var party = ParseParty(partEl, refId);

            // Attorneys (st:id = "attorney*") are NOT added to sub.Parties in the existing model
            // semantics (the controller keeps them in model.AttorneysJson and injects them into
            // sub.Parties during BuildSubmissionFromCreateModel — with the ATT role code and an
            // empty EntityOrganization sibling). So we populate sub.Parties for all three kinds,
            // matching what the builder expects to consume.
            sub.Parties.Add(party);
        }
    }

    private static FilingParty ParseParty(XElement partEl, string refId)
    {
        var party = new FilingParty
        {
            ReferenceId = refId,
            RoleCode = partEl.Element(Ecf + "CaseParticipantRoleCode")?.Value ?? string.Empty,
        };

        // Entity — EntityPerson (person) or EntityOrganization (org).
        // Attorneys typically have BOTH elements — EntityPerson with name + bar number, AND
        // EntityOrganization with firm name. Non-attorney parties have exactly one.
        var personEl = partEl.Element(Nc + "EntityPerson");
        var orgEl = partEl.Element(Nc + "EntityOrganization");

        if (personEl != null)
        {
            var nameEl = personEl.Element(Nc + "PersonName");
            party.FirstName = nameEl?.Element(Nc + "PersonGivenName")?.Value;
            party.MiddleName = nameEl?.Element(Nc + "PersonMiddleName")?.Value;
            party.LastName = nameEl?.Element(Nc + "PersonSurName")?.Value;
            party.NameSuffix = nameEl?.Element(Nc + "PersonNameSuffixText")?.Value;

            // Bar number (attorneys).
            var otherId = personEl.Element(Nc + "PersonOtherIdentification");
            if (otherId != null
                && otherId.Element(Nc + "IdentificationCategoryText")?.Value == "BAR")
            {
                party.BarNumber = otherId.Element(Nc + "IdentificationID")?.Value;
            }
        }

        if (orgEl != null)
        {
            // For attorney parties (EntityPerson present + EntityOrganization present), the org
            // name is the firm name — overloads OrganizationName field, matching how
            // CourtFilingController populates AttorneyEntryDto.FirmName into
            // FilingParty.OrganizationName. party.IsOrganization stays false because the
            // attorney IS a person; EntityOrganization is the firm's separate entity.
            //
            // For pure organization parties (IsOrganization=true), there's no EntityPerson;
            // this branch sets both IsOrganization and OrganizationName.
            var orgName = orgEl.Element(Nc + "OrganizationName")?.Value;
            if (!string.IsNullOrEmpty(orgName))
            {
                party.OrganizationName = orgName;
            }
            if (personEl == null)
            {
                party.IsOrganization = true;
            }
        }

        // ContactInformation (optional).
        var contactInfo = partEl.Element(Nc + "ContactInformation");
        if (contactInfo != null && contactInfo.HasElements)
        {
            party.Contact = ParseContactInformation(contactInfo);
        }

        // Extension fields — all optional, parse when present.
        party.FeeExemptionRequestType = partEl.Element(CpExt + "efmFeeExemptionRequestType")?.Value;
        party.InterpreterLanguage = partEl.Element(CpExt + "efmInterpreterLanguage")?.Value;
        party.FirstAppearancePaid = string.Equals(
            partEl.Element(CpExt + "efspFirstAppearancePaid")?.Value, "true",
            StringComparison.OrdinalIgnoreCase);
        party.GovernmentExempt = string.Equals(
            partEl.Element(CpExt + "efspGovernmentExempt")?.Value, "true",
            StringComparison.OrdinalIgnoreCase);
        party.EService = string.Equals(
            partEl.Element(CpExt + "eService")?.Value, "true",
            StringComparison.OrdinalIgnoreCase);

        foreach (var subTypeEl in partEl.Elements(CpExt + "partySubType"))
        {
            if (!string.IsNullOrEmpty(subTypeEl.Value))
                party.PartySubTypes.Add(subTypeEl.Value);
        }

        return party;
    }

    private static ContactInfo ParseContactInformation(XElement ci)
    {
        var contact = new ContactInfo();

        var mailingAddr = ci.Element(Nc + "ContactMailingAddress");
        if (mailingAddr != null)
        {
            // The <StructuredAddress> is in the AddrExt namespace.
            var structured = mailingAddr.Element(AddrExt + "StructuredAddress");
            if (structured != null)
            {
                contact.MailingAddress = ParseStructuredAddress(structured);
            }
        }

        var phoneBlock = ci.Element(Nc + "ContactTelephoneNumber");
        if (phoneBlock != null)
        {
            var phoneInfo = phoneBlock.Element(PhoneExt + "TelephoneNumberInformation");
            if (phoneInfo != null)
            {
                contact.PhoneType = phoneInfo.Element(PhoneExt + "TelephoneNumberFullType")?.Value;
                contact.PhoneNumber = phoneInfo.Element(Nc + "TelephoneNumberFullID")?.Value;
            }
        }

        contact.Email = ci.Element(Nc + "ContactEmailID")?.Value;

        return contact;
    }

    /// <summary>
    /// Parse a <c>&lt;addrExt:StructuredAddress&gt;</c> element into a <see cref="StructuredAddress"/>.
    /// Baseline wire emits multi-line street as ONE <c>AddressDeliveryPoint</c> with multiple
    /// <c>StreetFullText</c> children (line 1 + line 2); this matches the Audit E-2 builder fix.
    /// Reusable across contact-mailing-address and premise-address contexts — both use the same
    /// StructuredAddress element in the AddrExt namespace.
    /// </summary>
    private static StructuredAddress ParseStructuredAddress(XElement structured)
    {
        var deliveryPoint = structured.Element(Nc + "AddressDeliveryPoint");
        var streetLines = deliveryPoint?.Elements(Nc + "StreetFullText").ToList();
        return new StructuredAddress
        {
            AddressType = structured.Element(AddrExt + "LocationType")?.Value,
            Country = structured.Element(Nc + "LocationCountryName")?.Value ?? string.Empty,
            Address1 = streetLines?.ElementAtOrDefault(0)?.Value,
            Address2 = streetLines?.ElementAtOrDefault(1)?.Value,
            City = structured.Element(Nc + "LocationCityName")?.Value,
            State = structured.Element(Nc + "LocationStateName")?.Value,
            Zip = structured.Element(Nc + "LocationPostalCode")?.Value,
        };
    }

    // ─── CaseAugmentationExt (court, associations, incident address) ─

    private static void ParseCaseAugmentationExt(XElement augExt, FilingSubmission sub)
    {
        // CaseCourt / OrganizationLocation / LocationName — the human-readable location code ("GIB", etc.).
        var caseCourt = augExt.Element(Jxdm + "CaseCourt");
        var orgLocation = caseCourt?.Element(Nc + "OrganizationLocation");
        sub.LocationName = orgLocation?.Element(Nc + "LocationName")?.Value;

        // Premise address (Unlawful Detainer cases) — full structured address, distinct from
        // the simpler IncidentZipCode-only form. Parser builds a complete StructuredAddress
        // object; the builder's BuildStructuredAddress handles the emission symmetrically.
        var premiseAddr = augExt.Element(CivExt + "premiseAddress");
        var premiseStructured = premiseAddr?.Element(AddrExt + "StructuredAddress");
        if (premiseStructured != null)
        {
            sub.PremiseAddress = ParseStructuredAddress(premiseStructured);
        }

        // Incident address → FilingSubmission.IncidentZipCode (only zip is round-tripped; ECF
        // sometimes has full address but baseline consistently emits only postalCode).
        var incidentAddr = augExt.Element(CivExt + "incidentAddress");
        var incidentStructured = incidentAddr?.Element(AddrExt + "StructuredAddress");
        sub.IncidentZipCode = incidentStructured?.Element(Nc + "LocationPostalCode")?.Value;

        // Party-to-party associations (REPRESENTEDBY typically).
        foreach (var rp in augExt.Elements(CivExt + "relatedParticipants"))
        {
            var associationType = rp.Element(CivExt + "associationType")?.Value ?? string.Empty;
            var participantRef = rp.Element(CivExt + "participant")?.Attribute(St + "ref")?.Value;
            var relatedRef = rp.Element(CivExt + "relatedParticipant")?.Attribute(St + "ref")?.Value;
            if (participantRef != null && relatedRef != null)
            {
                sub.PartyAssociations.Add(new PartyAssociation
                {
                    AssociationType = associationType,
                    ParticipantRef = participantRef,
                    RelatedParticipantRef = relatedRef,
                });
            }
        }

        // Party-to-document associations (FILEDBY, REFERS_TO).
        foreach (var rpd in augExt.Elements(CivExt + "relatedParticipantDocuments"))
        {
            var associationType = rpd.Element(CivExt + "associationType")?.Value ?? string.Empty;
            var participantRef = rpd.Element(CivExt + "participant")?.Attribute(St + "ref")?.Value;
            var relatedDocRef = rpd.Element(CivExt + "relatedDocument")?.Attribute(St + "ref")?.Value;
            if (participantRef != null && relatedDocRef != null)
            {
                sub.PartyDocumentAssociations.Add(new PartyDocumentAssociation
                {
                    AssociationType = associationType,
                    ParticipantRef = participantRef,
                    DocumentRef = relatedDocRef,
                });
            }
        }
    }

    // ─── Document ────────────────────────────────────────────────────

    private static FilingDocument ParseDocument(XElement docEl)
    {
        var doc = new FilingDocument
        {
            ReferenceId = docEl.Attribute(St + "id")?.Value ?? string.Empty,
            SequenceNumber = int.TryParse(docEl.Element(Nc + "DocumentSequenceID")?.Value, out var seq)
                ? seq : 0,
            DocumentCode = docEl.Element(Nc + "DocumentDescriptionText")?.Value ?? string.Empty,
            FileControlId = docEl.Element(Nc + "DocumentFileControlID")?.Value,
            NameExtension = docEl.Element(CfmExt + "nameExtension")?.Value,
        };

        var binary = docEl.Element(Nc + "DocumentBinary");
        doc.BinaryLocationUri = binary?.Element(Nc + "BinaryLocationURI")?.Value ?? string.Empty;
        doc.MimeType = binary?.Element(Nc + "BinaryFormatStandardName")?.Value ?? "application/pdf";

        // IdentificationSourceText — court code inside DocumentIdentification.
        var docIdent = docEl.Element(Nc + "DocumentIdentification");
        doc.IdentificationSourceText = docIdent?.Element(Nc + "IdentificationSourceText")?.Value;

        // complaintType attribute (subsequent-filing marker).
        doc.ComplaintRef = docEl.Attribute("complaintType")?.Value;

        // DocumentFilingMetaData — subsequent-filing-only wire element carrying the classType
        // meta-grammar (see catalog §3.4). The builder emits this from doc.MetadataValues;
        // the parser mirrors it in reverse. Each <documentFilingMetaDataItem> has a required
        // <docValueMetaDataItem> descriptor (code/classType/subType/valueRestriction) and a
        // typed value payload whose element name depends on ClassType.
        var metaWrapper = docEl.Element(DfMeta + "DocumentFilingMetaData");
        if (metaWrapper != null)
        {
            foreach (var itemEl in metaWrapper.Elements(DfMeta + "documentFilingMetaDataItem"))
            {
                var mv = ParseMetadataItem(itemEl);
                if (mv != null)
                    doc.MetadataValues.Add(mv);
            }
        }

        return doc;
    }

    /// <summary>
    /// Parse a single <c>&lt;dfMeta:documentFilingMetaDataItem&gt;</c> into a
    /// <see cref="FilingMetadataValue"/>. Mirrors the builder's <c>BuildMetadataItem</c>
    /// emission — same ClassType dispatch, same value-element naming, same wrapper/child
    /// namespace split (wrapper in DfMeta; ContactValue flat fields in ContactValueNs per
    /// audit C-2 Bug B wire contract).
    /// </summary>
    private static FilingMetadataValue? ParseMetadataItem(XElement itemEl)
    {
        // The <docValueMetaDataItem> descriptor is required; skip items without it.
        var descEl = itemEl.Element(DfMeta + "docValueMetaDataItem");
        if (descEl == null) return null;

        var mv = new FilingMetadataValue
        {
            Code = descEl.Element(DfValue + "code")?.Value ?? string.Empty,
            ClassType = descEl.Element(DfValue + "classType")?.Value ?? string.Empty,
            SubType = descEl.Element(DfValue + "subType")?.Value,
            ValueRestriction = descEl.Element(DfValue + "valueRestriction")?.Value,
        };

        // Typed-value dispatch on ClassType. Same case-insensitive matching as the builder
        // (BuildMetadataItem at ReviewFilingXmlBuilder.cs:721). Canonical casing for the
        // ClassType itself is whatever the wire had — controller's CanonicalizeClassType
        // applies when building, so round-trip preserves the canonical form.
        switch (mv.ClassType.ToLowerInvariant())
        {
            case "text":
                mv.TextValue = itemEl.Element(DfMeta + "textValue")?.Value;
                break;

            case "boolean":
                var boolText = itemEl.Element(DfMeta + "booleanValue")?.Value;
                if (!string.IsNullOrEmpty(boolText))
                    mv.BooleanValue = string.Equals(boolText, "true", StringComparison.OrdinalIgnoreCase);
                break;

            case "codelist":
                mv.CodeValue = itemEl.Element(DfMeta + "codeValue")?.Value;
                break;

            case "currency":
                var curText = itemEl.Element(DfMeta + "currencyValue")?.Value;
                if (decimal.TryParse(curText, out var curVal))
                    mv.CurrencyValue = curVal;
                break;

            case "crsreceiptnumber":
                // Step #46 — round-trip parse for the JTI HTML Layer-A-
                // evidenced crsReceiptNumber classType. Mirrors the text arm shape.
                // See ReviewFilingXmlBuilder.cs:975 for the corresponding builder arm
                // + JTI HTML Document Metadata evidence reference.
                mv.CrsReceiptNumberValue = itemEl.Element(DfMeta + "crsReceiptNumberValue")?.Value;
                break;

            case "date":
                // Audit D-1 wire contract: baseline emits <nc:DateTime> (not <nc:Date>) inside
                // <dateValue>. Builder writes yyyy-MM-ddTHH:mm:ss without offset. DateTime.Parse
                // handles both the full-offset form baselines use (2020-12-31T00:00:00-08:00)
                // and the no-offset form our builder emits.
                var dtText = itemEl.Element(DfMeta + "dateValue")?.Element(Nc + "DateTime")?.Value;
                if (DateTime.TryParse(dtText, out var dtVal))
                    mv.DateValue = dtVal;
                break;

            case "caseparticipant":
            case "attorney":
                // Existing-data: <idReferences> with <id> children and optional
                // <additionalInfoTags>. Multiple <idReferences> elements may appear, one per
                // referenced id. Mirror the builder helper (ReviewFilingXmlBuilder.
                // EmitIdReferencesWithTags). Step #14 (silent-drop #10): populate canonical
                // TaggedReferences with per-id tag fidelity; legacy parallel fields kept
                // populated as a back-compat projection (test asserts, log formatters).
                ParseIdReferencesWithTags(itemEl, mv);
                // Audit H-1 fix: new-data caseParticipant has <caseParticipantValue>
                // children with full party identity (EntityPerson + RoleCode + nested
                // ContactInformation + eService). Multiple <caseParticipantValue> can appear
                // inside a single documentFilingMetaDataItem — CIV-SUB-001 NEW_RESPONDING_PARTY
                // emits two (Ron Wilson + Jacob Tillis). Each becomes a FilingParty in
                // mv.NewPartyValues.
                foreach (var cpvEl in itemEl.Elements(DfMeta + "caseParticipantValue"))
                {
                    mv.NewPartyValues.Add(ParseCaseParticipantValue(cpvEl));
                }

                // Legacy C-2 Bug B path: <contactValue> as a caseParticipant-item child carries
                // inline party contact WITHOUT identity (name/role). Kept for backward
                // compatibility with pre-H-1 callers that use NewPartyValue solely as a contact
                // carrier. On parse, reconstitute NewPartyValue.Contact with the StructuredAddress
                // fields derived from the flat ContactValue fields.
                var newPartyContact = itemEl.Element(DfMeta + "contactValue");
                if (newPartyContact != null && newPartyContact.HasElements)
                {
                    mv.NewPartyValues.Add(new FilingParty
                    {
                        Contact = ParseFlatContactValue(newPartyContact, asContactInfo: true) as ContactInfo
                    });
                }
                break;

            case "caseassignment":
                // Existing-data: same <idReferences>/<additionalInfoTags> shape as caseParticipant.
                // Step #14 (silent-drop #10): use shared per-id helper.
                ParseIdReferencesWithTags(itemEl, mv);
                // New-data: <caseAssignmentValue> carries inline attorney (EntityPerson with
                // name + bar number + EntityOrganization firm + ContactInformation +
                // AssignmentRole + optional eService flag). Mirror builder emission at
                // ReviewFilingXmlBuilder.cs:819-854.
                var caEl = itemEl.Element(DfMeta + "caseAssignmentValue");
                if (caEl != null)
                {
                    mv.CaseAssignmentValue = ParseCaseAssignmentValue(caEl);
                }
                break;

            case "contact":
                // Flat contact shape (audit C-2 Bug B). Wrapper in DfMeta; children in
                // ContactValueNs. Builder emits from mv.ContactValue (ContactValueData).
                var contactEl = itemEl.Element(DfMeta + "contactValue");
                if (contactEl != null && contactEl.HasElements)
                {
                    mv.ContactValue = ParseFlatContactValue(contactEl, asContactInfo: false) as ContactValueData;
                }
                break;

            case "judgment":
                // Step #15 audit (Path C — see docs/STEP15_JUDGMENT_AUDIT.md §2): symmetric
                // to builder. Read all <ns10:judgmentId> children of the <ns9:judgments>
                // wrapper into mv.JudgmentIds. Wrapper namespace is DfMeta; child namespace
                // is CourtEventJudgmentNs. Multiple <judgmentId> children may appear (WSDL
                // declares CourtEventJudgmentType[]) but observed sample only has 1.
                var judgmentsWrapper = itemEl.Element(DfMeta + "judgments");
                if (judgmentsWrapper != null)
                {
                    foreach (var jIdEl in judgmentsWrapper.Elements(CourtEventJudgmentNs + "judgmentId"))
                    {
                        var idText = jIdEl.Value;
                        if (!string.IsNullOrEmpty(idText))
                            mv.JudgmentIds.Add(idText);
                    }
                }
                break;
        }

        return mv;
    }

    /// <summary>
    /// Parse zero-or-more <c>&lt;idReferences&gt;</c> children from <paramref name="itemEl"/>
    /// and populate <see cref="FilingMetadataValue.TaggedReferences"/> (canonical wire shape,
    /// Step #14) plus the legacy parallel <see cref="FilingMetadataValue.IdReferences"/> +
    /// <see cref="FilingMetadataValue.AdditionalInfoTags"/> fields (back-compat readers).
    /// Inverse of <c>ReviewFilingXmlBuilder.EmitIdReferencesWithTags</c>; shared between
    /// caseParticipant / attorney / caseAssignment branches.
    /// </summary>
    private static void ParseIdReferencesWithTags(XElement itemEl, FilingMetadataValue mv)
    {
        foreach (var idRefEl in itemEl.Elements(DfMeta + "idReferences"))
        {
            var idText = idRefEl.Element(DfMeta + "id")?.Value;
            if (string.IsNullOrEmpty(idText)) continue;

            var tref = new TaggedReference { Id = idText };
            foreach (var tagEl in idRefEl.Elements(DfMeta + "additionalInfoTags"))
            {
                var tagType = tagEl.Element(DfMeta + "tagType")?.Value;
                var tagValue = tagEl.Element(DfMeta + "tagValue")?.Value;
                if (string.IsNullOrEmpty(tagType)) continue;

                var tag = new AdditionalInfoTag
                {
                    TagType = tagType,
                    TagValue = tagValue ?? string.Empty,
                };
                tref.Tags.Add(tag);
                // Legacy back-compat projection — flat list of all tags across all refs.
                mv.AdditionalInfoTags.Add(tag);
            }
            mv.TaggedReferences.Add(tref);
            // Legacy back-compat projection — flat list of all ref ids.
            mv.IdReferences.Add(idText);
        }
    }

    /// <summary>
    /// Parse the flat ContactValue shape used inside DocumentFilingMetaData (audit C-2 Bug B).
    /// Wrapper element is in DfMeta; children are in ContactValueNs. Returns either a
    /// <see cref="ContactValueData"/> (for classType="contact") or a <see cref="ContactInfo"/>
    /// (for classType="caseParticipant" new-data, where the inline contact is nested inside
    /// FilingParty.Contact rather than a standalone ContactValueData). The two target types
    /// have overlapping fields but distinct roles in the model; this helper normalizes parsing.
    /// </summary>
    private static object? ParseFlatContactValue(XElement cv, bool asContactInfo)
    {
        var address1 = cv.Element(ContactValueNs + "address1")?.Value;
        var address2 = cv.Element(ContactValueNs + "address2")?.Value;
        var city = cv.Element(ContactValueNs + "city")?.Value;
        var zip = cv.Element(ContactValueNs + "zip")?.Value;
        var state = cv.Element(ContactValueNs + "state")?.Value;
        var country = cv.Element(ContactValueNs + "country")?.Value;
        var phoneType = cv.Element(ContactValueNs + "telephoneType")?.Value;
        var phoneNumber = cv.Element(ContactValueNs + "telephoneNumber")?.Value;
        var email = cv.Element(ContactValueNs + "email")?.Value;
        var addressType = cv.Element(ContactValueNs + "addressType")?.Value;

        if (asContactInfo)
        {
            // NewPartyValue.Contact — nested StructuredAddress form used by FilingParty.
            var hasAddress = !string.IsNullOrEmpty(address1);
            return new ContactInfo
            {
                MailingAddress = hasAddress ? new StructuredAddress
                {
                    AddressType = addressType,
                    Address1 = address1,
                    Address2 = address2,
                    City = city,
                    State = state,
                    Zip = zip,
                    Country = country ?? string.Empty,
                } : null,
                PhoneType = phoneType,
                PhoneNumber = phoneNumber,
                Email = email,
            };
        }

        // Standalone ContactValueData — flat shape used by FilingMetadataValue.ContactValue
        // for classType="contact". Same fields, flat layout (no nested StructuredAddress).
        return new ContactValueData
        {
            Address1 = address1,
            Address2 = address2,
            City = city,
            Zip = zip,
            State = state,
            Country = country,
            PhoneType = phoneType,
            PhoneNumber = phoneNumber,
            Email = email,
            AddressType = addressType,
        };
    }

    /// <summary>
    /// Parse a <c>&lt;dfMeta:caseAssignmentValue&gt;</c> element (classType="caseAssignment"
    /// with valueRestriction="new-data") into a <see cref="CaseAssignmentData"/>. Mirrors
    /// builder emission at <c>ReviewFilingXmlBuilder.cs:819-854</c>.
    /// </summary>
    private static CaseAssignmentData ParseCaseAssignmentValue(XElement caEl)
    {
        var ca = new CaseAssignmentData();

        // Audit H-2 wire contract: EntityPerson and EntityOrganization INSIDE caseAssignmentValue
        // live in the ECF CommonTypes-4.0 namespace (NOT niem-core like CI's CaseParticipantExt).
        // The children (PersonName, PersonOtherIdentification, OrganizationName) stay in niem-core.
        // Builder-side fix symmetric at ReviewFilingXmlBuilder.cs:828.
        var personEl = caEl.Element(Ecf + "EntityPerson");
        if (personEl != null)
        {
            var nameEl = personEl.Element(Nc + "PersonName");
            ca.FirstName = nameEl?.Element(Nc + "PersonGivenName")?.Value;
            ca.MiddleName = nameEl?.Element(Nc + "PersonMiddleName")?.Value;
            ca.LastName = nameEl?.Element(Nc + "PersonSurName")?.Value;

            var otherId = personEl.Element(Nc + "PersonOtherIdentification");
            if (otherId != null
                && otherId.Element(Nc + "IdentificationCategoryText")?.Value == "BAR")
            {
                ca.BarNumber = otherId.Element(Nc + "IdentificationID")?.Value;
            }
        }

        // EntityOrganization → firm name (ECF namespace per H-2).
        var orgEl = caEl.Element(Ecf + "EntityOrganization");
        ca.FirmName = orgEl?.Element(Nc + "OrganizationName")?.Value;

        // ContactInformation → reuse standard parser (niem-core shape, not flat ContactValue).
        var contactEl = caEl.Element(Nc + "ContactInformation");
        if (contactEl != null && contactEl.HasElements)
        {
            ca.Contact = ParseContactInformation(contactEl);
        }

        // AssignmentRole — typically "ATT" per §3.3 observation.
        ca.AssignmentRole = caEl.Element(CaseAssign + "AssignmentRole")?.Value ?? "ATT";

        // eService (CpExt namespace — shared with CaseParticipantExt extension fields).
        ca.EService = string.Equals(
            caEl.Element(CpExt + "eService")?.Value, "true",
            StringComparison.OrdinalIgnoreCase);

        return ca;
    }

    /// <summary>
    /// Parse a <c>&lt;dfMeta:caseParticipantValue&gt;</c> element (classType="caseParticipant"
    /// with valueRestriction="new-data") into a <see cref="FilingParty"/>. Mirrors builder
    /// emission at <c>ReviewFilingXmlBuilder.cs:BuildCaseParticipantValue</c>. Audit H-1 fix
    ///.
    /// <para>
    /// Baseline quirks handled defensively:
    /// <list type="bullet">
    ///   <item>Multiple <c>EntityPerson</c> siblings in one caseParticipantValue (CIV-SUB-001
    ///         emits a second "AFS-identification" placeholder with blank names). Only the
    ///         FIRST EntityPerson is parsed into the FilingParty; downstream alias / AFS
    ///         handling is a future concern — TODO if an audit surfaces it.</item>
    ///   <item><c>st:id=""</c> (empty reference — CIV-SUB-001 NEW_RESPONDING_PARTY line 116).
    ///         Preserved as empty ReferenceId; builder will emit the attribute with empty
    ///         value (symmetric).</item>
    /// </list>
    /// </para>
    /// </summary>
    private static FilingParty ParseCaseParticipantValue(XElement cpvEl)
    {
        var party = new FilingParty
        {
            ReferenceId = cpvEl.Attribute(St + "id")?.Value ?? string.Empty,
            RoleCode = cpvEl.Element(Ecf + "CaseParticipantRoleCode")?.Value ?? string.Empty,
        };

        // EntityPerson (Ecf namespace per H-2 rule). Only the first is parsed; subsequent
        // siblings (e.g., AFS-alias EntityPerson in CIV-SUB-001) are not modeled.
        var personEl = cpvEl.Element(Ecf + "EntityPerson");
        if (personEl != null)
        {
            var nameEl = personEl.Element(Nc + "PersonName");
            party.FirstName = nameEl?.Element(Nc + "PersonGivenName")?.Value;
            party.MiddleName = nameEl?.Element(Nc + "PersonMiddleName")?.Value;
            party.LastName = nameEl?.Element(Nc + "PersonSurName")?.Value;
            party.NameSuffix = nameEl?.Element(Nc + "PersonNameSuffixText")?.Value;

            var otherId = personEl.Element(Nc + "PersonOtherIdentification");
            if (otherId != null
                && otherId.Element(Nc + "IdentificationCategoryText")?.Value == "BAR")
            {
                party.BarNumber = otherId.Element(Nc + "IdentificationID")?.Value;
            }
        }

        // EntityOrganization (Ecf namespace per H-2 rule).
        var orgEl = cpvEl.Element(Ecf + "EntityOrganization");
        if (orgEl != null)
        {
            party.OrganizationName = orgEl.Element(Nc + "OrganizationName")?.Value;
            if (personEl == null)
            {
                party.IsOrganization = true;
            }
        }

        // ContactInformation — nested inside caseParticipantValue (niem-core shape).
        var contactEl = cpvEl.Element(Nc + "ContactInformation");
        if (contactEl != null && contactEl.HasElements)
        {
            party.Contact = ParseContactInformation(contactEl);
        }

        // eService (CpExt namespace).
        party.EService = string.Equals(
            cpvEl.Element(CpExt + "eService")?.Value, "true",
            StringComparison.OrdinalIgnoreCase);

        return party;
    }

    // ─── PaymentMessage ──────────────────────────────────────────────

    private static void ParsePaymentMessage(XElement reviewMsg, FilingSubmission sub)
    {
        var paymentMsg = reviewMsg.Element(Pay + "PaymentMessage");
        if (paymentMsg == null) return;

        var authInfo = paymentMsg.Element(PayExt + "paymentAuthorizationInfo");
        if (authInfo == null) return;

        sub.Payment = new FilingPayment
        {
            CustomerProfileId = authInfo.Element(PayExt + "customerProfileId")?.Value ?? "0",
            CustomerPaymentProfileId = authInfo.Element(PayExt + "customerPaymentProfileId")?.Value ?? "0",
            PaymentType = authInfo.Element(PayExt + "paymentType")?.Value ?? "ACH",
        };
    }
}
