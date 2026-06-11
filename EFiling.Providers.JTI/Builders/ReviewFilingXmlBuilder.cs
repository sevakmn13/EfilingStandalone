using System.Xml.Linq;
using EFiling.Core.Enums;
using EFiling.Core.Models;
using EFiling.Providers.JTI.Config;
using EFiling.Providers.JTI.Soap;

namespace EFiling.Providers.JTI.Builders;

/// <summary>
/// Builds the ReviewFilingRequestMessage SOAP XML from a <see cref="FilingSubmission"/>.
/// Also builds the FeesCalculationQueryMessage (same CoreFilingMessage body + PaymentMessage).
/// Data-driven and flexible — all court-specific fields are optional on the model.
/// </summary>
public static class ReviewFilingXmlBuilder
{
    // ─── XNamespaces ─────────────────────────────────────────────────
    static readonly XNamespace Env    = SoapEnvelopeBuilder.NsSoapEnv;
    static readonly XNamespace Nc     = SoapEnvelopeBuilder.NsNiemCore;
    static readonly XNamespace Ecf    = SoapEnvelopeBuilder.NsCommonTypes;
    static readonly XNamespace Jxdm   = SoapEnvelopeBuilder.NsJxdm;
    static readonly XNamespace Udt    = SoapEnvelopeBuilder.NsUnqualifiedDataTypes;
    static readonly XNamespace Civil  = SoapEnvelopeBuilder.NsCivilCase;
    static readonly XNamespace CivExt = SoapEnvelopeBuilder.NsJtiCivilCaseExt;
    static readonly XNamespace Cfm    = SoapEnvelopeBuilder.NsCoreFilingMessage;
    static readonly XNamespace CfmExt = SoapEnvelopeBuilder.NsJtiCoreFilingExt;
    static readonly XNamespace Pay    = SoapEnvelopeBuilder.NsPaymentMessage;
    static readonly XNamespace PayExt = SoapEnvelopeBuilder.NsJtiPaymentExt;
    static readonly XNamespace Wsdl   = SoapEnvelopeBuilder.NsWsdlProfile;
    static readonly XNamespace Xsi    = SoapEnvelopeBuilder.NsXsi;
    static readonly XNamespace St     = SoapEnvelopeBuilder.NsStructures;
    static readonly XNamespace CpExt  = SoapEnvelopeBuilder.NsJtiCaseParticipantExt;
    static readonly XNamespace AddrExt = SoapEnvelopeBuilder.NsJtiStructuredAddressExt;
    static readonly XNamespace PhoneExt = SoapEnvelopeBuilder.NsJtiTelephoneNumberExt;
    static readonly XNamespace FeesQ  = SoapEnvelopeBuilder.NsFeesCalcQuery;
    static readonly XNamespace FeesQExt = SoapEnvelopeBuilder.NsJtiFeesCalcQueryExt;
    static readonly XNamespace DfMeta = SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData;
    static readonly XNamespace DfValue = SoapEnvelopeBuilder.NsJtiDocumentValue;
    static readonly XNamespace CaseAssign = SoapEnvelopeBuilder.NsJtiCaseAssignmentType;
    // ContactValue extension namespace — audit C-2 Bug B fix (catalog §3.4). The <contactValue>
    // wrapper lives in DfMeta (DocumentFilingMetaData), but its flat field children (address1,
    // city, state, zip, country, telephoneType, email, addressType) must be in this namespace.
    // Confirmed by baseline wire samples (CIV-SUB-003, FAM-SUB-005) and WSDL-generated
    // ContactValue type's [XmlTypeAttribute(Namespace="urn:...ContactValue")].
    static readonly XNamespace ContactValueNs = SoapEnvelopeBuilder.NsJtiContactValue;
    // CourtEventJudgment extension namespace — Step #15 (judgment classType, see
    // STEP15_JUDGMENT_AUDIT.md). The <ns9:judgments> wrapper lives in DfMeta but its
    // <judgmentId> child element lives in this namespace per WSDL CourtEventJudgmentType
    // (FilingReview/Reference.cs:22743) and observed LASC Writ of Return Sample wire shape.
    // Judgment is the ONLY classType where wrapper + content namespaces differ.
    static readonly XNamespace CourtEventJudgmentNs = SoapEnvelopeBuilder.NsJtiCourtEventJudgment;

    /// <summary>
    /// Build a ReviewFilingRequestMessage SOAP envelope.
    /// </summary>
    public static string BuildReviewFilingRequest(FilingSubmission sub, CourtConfiguration config)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            BuildEnvelope(sub, config, isFeesCalc: false)
        );
        return doc.Declaration + doc.ToString(SaveOptions.None);
    }

    /// <summary>
    /// Build a FeesCalculationQueryMessage SOAP envelope.
    /// Same CoreFilingMessage body as ReviewFiling, wrapped differently.
    /// </summary>
    public static string BuildFeesCalculationRequest(FilingSubmission sub, CourtConfiguration config)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            BuildEnvelope(sub, config, isFeesCalc: true)
        );
        return doc.Declaration + doc.ToString(SaveOptions.None);
    }

    // ─── Envelope ────────────────────────────────────────────────────

    private static XElement BuildEnvelope(FilingSubmission sub, CourtConfiguration config, bool isFeesCalc)
    {
        // Build SOAP Header — inject JTI test header when TestFilingMode is set
        var header = new XElement(Env + "Header");
        if (config.TestFilingMode == TestFilingMode.AutoAccept)
        {
            header.Add(new XElement(XName.Get("status", "com.journaltech.niem.test"), "This is a test filing"));
        }
        else if (config.TestFilingMode == TestFilingMode.AutoReject)
        {
            header.Add(new XElement(XName.Get("status", "com.journaltech.niem.test"), "Auto Reject Filing"));
        }

        var envelope = new XElement(Env + "Envelope",
            new XAttribute(XNamespace.Xmlns + "SOAP-ENV", Env),
            new XAttribute(XNamespace.Xmlns + "ns1", Nc),
            new XAttribute(XNamespace.Xmlns + "ns2", Ecf),
            new XAttribute(XNamespace.Xmlns + "ns3", Jxdm),
            new XAttribute(XNamespace.Xmlns + "ns4", Udt),
            new XAttribute(XNamespace.Xmlns + "ns5", Civil),
            new XAttribute(XNamespace.Xmlns + "ns6", CivExt),
            new XAttribute(XNamespace.Xmlns + "ns7", SoapEnvelopeBuilder.NsUblCbc),
            new XAttribute(XNamespace.Xmlns + "ns8", Cfm),
            new XAttribute(XNamespace.Xmlns + "ns9", CfmExt),
            new XAttribute(XNamespace.Xmlns + "ns10", Pay),
            new XAttribute(XNamespace.Xmlns + "ns11", SoapEnvelopeBuilder.NsUblCac),
            new XAttribute(XNamespace.Xmlns + "ns12", PayExt),
            new XAttribute(XNamespace.Xmlns + "ns13", Wsdl),
            new XAttribute(XNamespace.Xmlns + "ns14", DfMeta),
            new XAttribute(XNamespace.Xmlns + "ns15", DfValue),
            new XAttribute(XNamespace.Xmlns + "ns16", CaseAssign),
            // CourtEventJudgmentNs (Step #15) is intentionally NOT declared at envelope
            // level — adding it unconditionally would attach `xmlns:nsN="..."` to every
            // outgoing envelope (including the 48 baseline-matching CI + SF round-trips
            // that don't use judgment), which would break diff tests. XLinq auto-prefixes
            // the namespace inline on the first <judgmentId> element when judgment is
            // actually emitted (only for filings carrying classType=judgment metadata).
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            header,
            new XElement(Env + "Body",
                isFeesCalc ? BuildFeesCalcBody(sub, config) : BuildReviewFilingBody(sub, config)
            )
        );

        return envelope;
    }

    // ─── ReviewFiling Body ───────────────────────────────────────────

    private static XElement BuildReviewFilingBody(FilingSubmission sub, CourtConfiguration config)
    {
        var msg = new XElement(Wsdl + "ReviewFilingRequestMessage",
            BuildSendingMDE(config),
            BuildCoreFilingMessage(sub, config)
        );
        if (sub.Payment != null)
            msg.Add(BuildPaymentMessage(sub));
        return msg;
    }

    // ─── FeesCalculation Body ────────────────────────────────────────

    private static XElement BuildFeesCalcBody(FilingSubmission sub, CourtConfiguration config)
    {
        // WSDL: input element is ns18:FeesCalculationQueryMessage (ECF base namespace).
        // FeesCalculationQueryMessageType extends QueryMessageType (has SendingMDELocationID)
        // and contains a choice of CoreFilingMessage | CoreFilingMessageType.
        // The ext (FeesCalculationQueryMessageTypeExt) adds PaymentMessage | PaymentMessageExt.
        // Must declare xsi:type so the server knows to expect the ext's PaymentMessage child.
        return new XElement(FeesQ + "FeesCalculationQueryMessage",
            new XAttribute(Xsi + "type", "ns14:FeesCalculationQueryMessageTypeExt"),
            new XAttribute(XNamespace.Xmlns + "ns14", FeesQExt),
            BuildSendingMDE(config),
            BuildCoreFilingMessage(sub, config),
            BuildPaymentMessage(sub)
        );
    }

    // ─── Shared Building Blocks ──────────────────────────────────────

    private static XElement BuildSendingMDE(CourtConfiguration config)
    {
        // IdentificationID = NFRC callback URL (where JTI sends NFRC messages).
        // For ReviewFiling this MUST be a valid URL; for FeesCalc it's not validated.
        var identificationId = !string.IsNullOrEmpty(config.NfrcCallbackUrl)
            ? config.NfrcCallbackUrl
            : config.CourtId ?? string.Empty;

        return new XElement(Ecf + "SendingMDELocationID",
            new XElement(Nc + "IdentificationID", identificationId),
            new XElement(Nc + "IdentificationSourceText", config.CourtId ?? string.Empty)
        );
    }

    private static XElement BuildCoreFilingMessage(FilingSubmission sub, CourtConfiguration config)
    {
        var cfm = new XElement(Cfm + "CoreFilingMessage",
            new XAttribute(Xsi + "type", "ns9:CoreFilingMessageExtType")
        );
        cfm.Add(BuildCoreFilingMessageContent(sub, config));
        return cfm;
    }

    private static object[] BuildCoreFilingMessageContent(FilingSubmission sub, CourtConfiguration config)
    {
        var content = new List<object>();

        // DocumentFiledDate
        content.Add(new XElement(Nc + "DocumentFiledDate",
            new XAttribute("id", "ref1"),
            new XElement(Nc + "DateTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
        ));

        // DocumentIdentification
        content.Add(new XElement(Nc + "DocumentIdentification",
            new XElement(Nc + "IdentificationID", sub.EfspReferenceId)
        ));

        // DocumentSubmitter (initial filings only)
        if (sub.FilingType == FilingType.Initial && !string.IsNullOrEmpty(sub.SubmitterUsername))
        {
            content.Add(new XElement(Nc + "DocumentSubmitter",
                new XElement(Ecf + "EntityPerson",
                    new XAttribute("id", "Person1"),
                    new XElement(Nc + "PersonName",
                        new XElement(Nc + "PersonFullName", sub.SubmitterUsername)
                    )
                )
            ));
        }

        // Case element (CivilCaseTypeExt for all case types via JTI)
        content.Add(BuildCase(sub, config));

        // FilingConfidentialityIndicator (initial filings only)
        if (sub.FilingType == FilingType.Initial)
        {
            // FilingConfidentialityIndicator ALWAYS emits for Initial filings (unconditional
            // child of FilingLeadDocument per wire contract). Defaults to false when the
            // tri-state ConditionallySealed is null (Audit F-1 2026-04-22).
            content.Add(new XElement(Cfm + "FilingConfidentialityIndicator",
                (sub.ConditionallySealed ?? false).ToString().ToLowerInvariant()
            ));
        }

        // FilingLeadDocument
        if (sub.LeadDocument != null)
            content.Add(BuildDocument(sub.LeadDocument, isLead: true));

        // FilingConnectedDocuments
        foreach (var doc in sub.ConnectedDocuments)
            content.Add(BuildDocument(doc, isLead: false));

        // CoreFilingMessageExtType extensions
        if (!string.IsNullOrEmpty(sub.MessageToClerk))
            content.Add(new XElement(CfmExt + "messageToClerk", sub.MessageToClerk));

        if (!string.IsNullOrEmpty(sub.SubmitterUsername))
        {
            content.Add(new XElement(CfmExt + "appClientUsername", sub.SubmitterUsername));
            content.Add(new XElement(CfmExt + "appClientParentUsername", sub.SubmitterUsername));
        }

        var filingTypeValue = sub.FilingType == FilingType.Initial ? "INITIAL" : "SUBSEQUENT";
        content.Add(new XElement(CfmExt + "eFilingCaseFilingType", filingTypeValue));

        return content.ToArray();
    }

    // ─── Case ────────────────────────────────────────────────────────

    private static XElement BuildCase(FilingSubmission sub, CourtConfiguration config)
    {
        var caseEl = new XElement(Nc + "Case",
            new XAttribute(Xsi + "type", "ns6:CivilCaseTypeExt")
        );

        // CaseDocketID (required for subsequent filings)
        if (!string.IsNullOrEmpty(sub.CaseDocketId))
            caseEl.Add(new XElement(Nc + "CaseDocketID", sub.CaseDocketId));

        // CaseTrackingID (subsequent filings)
        if (!string.IsNullOrEmpty(sub.CaseTrackingId))
            caseEl.Add(new XElement(Nc + "CaseTrackingID", sub.CaseTrackingId));

        // Complaint element (subsequent filings — links to sub-case)
        if (!string.IsNullOrEmpty(sub.ComplaintId))
            caseEl.Add(new XElement(CivExt + "Complaint",
                new XAttribute(St + "id", sub.ComplaintId)));

        // CaseCategoryText
        if (!string.IsNullOrEmpty(sub.CaseCategoryCode))
            caseEl.Add(new XElement(Nc + "CaseCategoryText", sub.CaseCategoryCode));

        // CaseAugmentation — contains participants (skip for subsequent when no parties)
        if (sub.FilingType == FilingType.Initial || sub.Parties.Count > 0)
            caseEl.Add(BuildCaseAugmentation(sub));

        // CivilCase fields
        if (sub.AmountInControversy.HasValue)
        {
            // Bug #4 fix (Track A): xsi:type must reference the niem-core AmountType
            // (http://niem.gov/niem/niem-core/2.0#AmountType), NOT the UBL UnqualifiedDataTypes
            // AmountType. Previous literal "ns4:AmountType" relied on XDocument auto-binding
            // ns4 to the UBL UDT namespace, producing a schema-invalid xsi:type. Explicitly
            // declare a local `nc` prefix for niem-core and use it so the QName is unambiguous
            // regardless of XDocument's auto-prefix assignment.
            caseEl.Add(new XElement(Civil + "AmountInControversy",
                new XAttribute(XNamespace.Xmlns + "nc", Nc.NamespaceName),
                new XAttribute(Xsi + "type", "nc:AmountType"),
                sub.AmountInControversy.Value.ToString("0")
            ));
        }

        // ClassActionIndicator — base CivilCaseType, between AmountInControversy and JurisdictionalGroundsCode
        if (sub.ClassAction)
            caseEl.Add(new XElement(Civil + "ClassActionIndicator", "true"));

        if (!string.IsNullOrEmpty(sub.JurisdictionalGroundsCode))
            caseEl.Add(new XElement(Civil + "JurisdictionalGroundsCode", sub.JurisdictionalGroundsCode));

        // CivilCaseTypeExt fields
        if (!string.IsNullOrEmpty(sub.CaseTypeCode))
            caseEl.Add(new XElement(CivExt + "CaseTypeText", sub.CaseTypeCode));

        // CaseAugmentationExt — court, associations, addresses
        caseEl.Add(BuildCaseAugmentationExt(sub, config));

        // CivilCaseTypeExt boolean flags (XSD ordering: complexLitigation → asbestos → CEQA → conditionallySealed)
        if (sub.ComplexLitigation)
            caseEl.Add(new XElement(CivExt + "complexLitigation", "true"));

        if (sub.Asbestos)
            caseEl.Add(new XElement(CivExt + "asbestos", "true"));

        if (sub.CaliforniaEnvironmentalQualityAct)
            caseEl.Add(new XElement(CivExt + "californiaEnvironmentalQualityAct", "true"));

        // Audit F-1 fix: tri-state emission. Only emit when producer explicitly
        // set the value (ConditionallySealed.HasValue). Emits the actual bool value (lowercase
        // "true"/"false"). When null the element is omitted entirely — matches CIV-INI-001
        // baseline which has no <conditionallySealed>, while CIV-INI-007 emits "false" explicitly.
        if (sub.ConditionallySealed.HasValue)
            caseEl.Add(new XElement(CivExt + "conditionallySealed",
                sub.ConditionallySealed.Value.ToString().ToLowerInvariant()));

        // Special status codes
        if (sub.SpecialStatusCodes.Count > 0)
        {
            var statusEl = new XElement(CivExt + "CaseSpecialStatus");
            foreach (var code in sub.SpecialStatusCodes)
                statusEl.Add(new XElement(CivExt + "SpecialStatusItem",
                    new XElement(CivExt + "statusCode", code)));
            caseEl.Add(statusEl);
        }

        // Location
        if (!string.IsNullOrEmpty(sub.LocationCode))
            caseEl.Add(new XElement(CivExt + "location", sub.LocationCode));

        return caseEl;
    }

    // ─── CaseAugmentation (ECF CommonTypes — contains parties) ───────

    private static XElement BuildCaseAugmentation(FilingSubmission sub)
    {
        var aug = new XElement(Ecf + "CaseAugmentation");

        // CaseParticipants
        foreach (var party in sub.Parties)
            aug.Add(BuildParticipant(party));

        return aug;
    }

    // ─── CaseAugmentationExt (JTI — court, associations, addresses) ──

    private static XElement BuildCaseAugmentationExt(FilingSubmission sub, CourtConfiguration config)
    {
        var augExt = new XElement(CivExt + "CaseAugmentationExt");

        // CaseCourt
        augExt.Add(BuildCaseCourt(sub, config));

        // Premise address (Unlawful Detainer)
        if (sub.PremiseAddress != null)
            augExt.Add(new XElement(CivExt + "premiseAddress", BuildStructuredAddress(sub.PremiseAddress)));

        // Incident address (incident zip code)
        if (!string.IsNullOrEmpty(sub.IncidentZipCode))
        {
            augExt.Add(new XElement(CivExt + "incidentAddress",
                new XElement(AddrExt + "StructuredAddress",
                    new XElement(Nc + "LocationPostalCode", sub.IncidentZipCode)
                )
            ));
        }

        // Citation (Parking Appeals)
        if (sub.Citation != null)
        {
            var citEl = new XElement(CivExt + "citation");
            if (!string.IsNullOrEmpty(sub.Citation.CitationId))
                citEl.Add(new XElement(Jxdm + "ActivityIdentification",
                    new XElement(Nc + "IdentificationID", sub.Citation.CitationId)));
            if (sub.Citation.ActivityDate.HasValue)
                citEl.Add(new XElement(Nc + "ActivityDate",
                    new XElement(Nc + "Date", sub.Citation.ActivityDate.Value.ToString("yyyy-MM-dd"))));
            augExt.Add(citEl);
        }

        // No-fee case
        if (sub.NoFeeCase)
        {
            augExt.Add(new XElement(CivExt + "noFeeCase", "true"));
            if (!string.IsNullOrEmpty(sub.NoFeeCaseSection))
                augExt.Add(new XElement(CivExt + "noFeeCaseSection", sub.NoFeeCaseSection));
        }

        // Party-to-party associations (REPRESENTEDBY)
        foreach (var assoc in sub.PartyAssociations)
        {
            augExt.Add(new XElement(CivExt + "relatedParticipants",
                new XElement(CivExt + "associationType", assoc.AssociationType),
                new XElement(CivExt + "participant", new XAttribute(St + "ref", assoc.ParticipantRef)),
                new XElement(CivExt + "relatedParticipant", new XAttribute(St + "ref", assoc.RelatedParticipantRef))
            ));
        }

        // Party-to-document associations (FILEDBY, REFERS_TO)
        foreach (var assoc in sub.PartyDocumentAssociations)
        {
            augExt.Add(new XElement(CivExt + "relatedParticipantDocuments",
                new XElement(CivExt + "associationType", assoc.AssociationType),
                new XElement(CivExt + "participant", new XAttribute(St + "ref", assoc.ParticipantRef)),
                new XElement(CivExt + "relatedDocument", new XAttribute(St + "ref", assoc.DocumentRef))
            ));
        }

        // Number of parcels (Eminent Domain)
        if (sub.NumberOfParcels.HasValue)
            augExt.Add(new XElement(CivExt + "noOfParcels", sub.NumberOfParcels.Value));

        return augExt;
    }

    // ─── CaseCourt ───────────────────────────────────────────────────

    private static XElement BuildCaseCourt(FilingSubmission sub, CourtConfiguration config)
    {
        var courtEl = new XElement(Jxdm + "CaseCourt",
            new XElement(Nc + "OrganizationIdentification",
                new XElement(Nc + "IdentificationID", config.DisplayName ?? config.CourtId ?? string.Empty),
                new XElement(Nc + "IdentificationSourceText", config.CourtId ?? string.Empty)
            )
        );

        if (!string.IsNullOrEmpty(sub.LocationName))
        {
            courtEl.Add(new XElement(Nc + "OrganizationLocation",
                new XElement(Nc + "LocationDescriptionText", "ParentLocationCode"),
                new XElement(Nc + "LocationName", sub.LocationName)
            ));
        }

        courtEl.Add(new XElement(Jxdm + "CourtName", config.DisplayName ?? config.CourtId ?? string.Empty));

        return courtEl;
    }

    // ─── CaseParticipant ─────────────────────────────────────────────

    private static XElement BuildParticipant(FilingParty party)
    {
        var partEl = new XElement(CpExt + "CaseParticipantExt",
            new XAttribute(St + "id", party.ReferenceId)
        );

        // CaseParticipantRoleCode
        partEl.Add(new XElement(Ecf + "CaseParticipantRoleCode", party.RoleCode));

        // Entity (Person or Organization)
        if (party.IsOrganization)
        {
            partEl.Add(new XElement(Nc + "EntityOrganization",
                new XAttribute(Xsi + "type", "ns2:OrganizationType"),
                new XElement(Nc + "OrganizationName", party.OrganizationName ?? string.Empty)
            ));
        }
        else
        {
            var personEl = new XElement(Nc + "EntityPerson",
                new XAttribute(Xsi + "type", "ns2:PersonType")
            );

            // PersonName
            var nameEl = new XElement(Nc + "PersonName");
            if (!string.IsNullOrEmpty(party.FirstName))
                nameEl.Add(new XElement(Nc + "PersonGivenName", party.FirstName));
            if (!string.IsNullOrEmpty(party.MiddleName))
                nameEl.Add(new XElement(Nc + "PersonMiddleName", party.MiddleName));
            if (!string.IsNullOrEmpty(party.LastName))
                nameEl.Add(new XElement(Nc + "PersonSurName", party.LastName));
            if (!string.IsNullOrEmpty(party.NameSuffix))
                nameEl.Add(new XElement(Nc + "PersonNameSuffixText", party.NameSuffix));
            personEl.Add(nameEl);

            // Bar number (for attorneys)
            if (!string.IsNullOrEmpty(party.BarNumber))
            {
                personEl.Add(new XElement(Nc + "PersonOtherIdentification",
                    new XElement(Nc + "IdentificationID", party.BarNumber),
                    new XElement(Nc + "IdentificationCategoryText", "BAR")
                ));
            }

            partEl.Add(personEl);

            // Attorneys have an EntityOrganization for the firm. Emit OrganizationName when the
            // party's firm name is populated (controller sets FilingParty.OrganizationName from
            // AttorneyEntryDto.FirmName). Emit an empty EntityOrganization element when the firm
            // name is absent — some baseline samples (e.g., FAM-INI-001) show this form for
            // attorneys whose firm isn't on the wire.
            //
            // Audit E-1 fix: pre-fix code unconditionally emitted
            // <EntityOrganization/> ignoring OrganizationName, silently dropping the firm name
            // for ANY attorney party submitted with a firm. Discovered via CIV-INI-003 round-trip
            // test surfacing a 1-child-expected-0-child-actual diff on the attorney party.
            if (party.RoleCode == "ATT")
            {
                var firmOrgEl = new XElement(Nc + "EntityOrganization",
                    new XAttribute(Xsi + "type", "ns2:OrganizationType"));
                if (!string.IsNullOrEmpty(party.OrganizationName))
                {
                    firmOrgEl.Add(new XElement(Nc + "OrganizationName", party.OrganizationName));
                }
                partEl.Add(firmOrgEl);
            }
        }

        // Alternate names (AKA, DBA, FDBA, FKA, ALIAS)
        foreach (var aka in party.AlternateNames)
        {
            if (!string.IsNullOrEmpty(aka.OrganizationName))
            {
                partEl.Add(new XElement(Nc + "EntityOrganization",
                    new XAttribute(Xsi + "type", "ns2:OrganizationType"),
                    new XElement(Nc + "OrganizationName", aka.OrganizationName),
                    new XElement(Nc + "OrganizationIdentification",
                        new XElement(Nc + "IdentificationCategoryText", aka.Type))
                ));
            }
            else
            {
                var akaPersonEl = new XElement(Nc + "EntityPerson",
                    new XAttribute(Xsi + "type", "ns2:PersonType"));
                var akaNameEl = new XElement(Nc + "PersonName");
                if (!string.IsNullOrEmpty(aka.FirstName))
                    akaNameEl.Add(new XElement(Nc + "PersonGivenName", aka.FirstName));
                if (!string.IsNullOrEmpty(aka.MiddleName))
                    akaNameEl.Add(new XElement(Nc + "PersonMiddleName", aka.MiddleName));
                if (!string.IsNullOrEmpty(aka.LastName))
                    akaNameEl.Add(new XElement(Nc + "PersonSurName", aka.LastName));
                if (!string.IsNullOrEmpty(aka.NameSuffix))
                    akaNameEl.Add(new XElement(Nc + "PersonNameSuffixText", aka.NameSuffix));
                akaPersonEl.Add(akaNameEl);
                akaPersonEl.Add(new XElement(Nc + "PersonOtherIdentification",
                    new XElement(Nc + "IdentificationCategoryText", aka.Type)));
                partEl.Add(akaPersonEl);
            }
        }

        // ContactInformation
        if (party.Contact != null)
            partEl.Add(BuildContactInformation(party.Contact));

        // CaseParticipantExtType extension fields — only emit when they have actual values.
        // referenceId / primaryId — for existing parties in subsequent filings
        if (!string.IsNullOrEmpty(party.PrimaryId))
        {
            partEl.Add(new XElement(CpExt + "referenceId", party.ReferenceId));
            partEl.Add(new XElement(CpExt + "primaryId", party.PrimaryId));
        }

        // JTI sample XMLs omit all of these for basic parties.
        if (!string.IsNullOrEmpty(party.FeeExemptionRequestType))
            partEl.Add(new XElement(CpExt + "efmFeeExemptionRequestType", party.FeeExemptionRequestType));
        if (party.FirstAppearancePaid)
            partEl.Add(new XElement(CpExt + "efspFirstAppearancePaid", "true"));
        if (party.GovernmentExempt)
            partEl.Add(new XElement(CpExt + "efspGovernmentExempt", "true"));
        if (!string.IsNullOrEmpty(party.InterpreterLanguage))
            partEl.Add(new XElement(CpExt + "efmInterpreterLanguage", party.InterpreterLanguage));

        // WSDL Order: partySubType(15) → dateOfBirth(17) → gender(18) → eService(23)
        foreach (var subType in party.PartySubTypes)
            partEl.Add(new XElement(CpExt + "partySubType", subType));

        if (party.DateOfBirth.HasValue)
            partEl.Add(new XElement(CpExt + "dateOfBirth",
                new XElement(Nc + "Date", party.DateOfBirth.Value.ToString("yyyy-MM-dd"))));

        if (!string.IsNullOrEmpty(party.Gender))
            partEl.Add(new XElement(CpExt + "gender", party.Gender));

        if (party.EService)
            partEl.Add(new XElement(CpExt + "eService", "true"));

        return partEl;
    }

    // ─── ContactInformation ──────────────────────────────────────────

    private static XElement BuildContactInformation(ContactInfo contact)
    {
        var ciEl = new XElement(Nc + "ContactInformation");

        if (contact.MailingAddress != null)
        {
            ciEl.Add(new XElement(Nc + "ContactMailingAddress",
                BuildStructuredAddress(contact.MailingAddress)
            ));
        }

        if (!string.IsNullOrEmpty(contact.PhoneNumber))
        {
            ciEl.Add(new XElement(Nc + "ContactTelephoneNumber",
                new XElement(PhoneExt + "TelephoneNumberInformation",
                    new XElement(PhoneExt + "TelephoneNumberFullType", contact.PhoneType ?? "W"),
                    new XElement(Nc + "TelephoneNumberFullID", contact.PhoneNumber)
                )
            ));
        }

        if (!string.IsNullOrEmpty(contact.Email))
            ciEl.Add(new XElement(Nc + "ContactEmailID", contact.Email));

        return ciEl;
    }

    // ─── StructuredAddress ───────────────────────────────────────────

    private static XElement BuildStructuredAddress(StructuredAddress addr)
    {
        var addrEl = new XElement(AddrExt + "StructuredAddress");

        if (!string.IsNullOrEmpty(addr.AddressType))
            addrEl.Add(new XElement(AddrExt + "LocationType", addr.AddressType));

        if (!string.IsNullOrEmpty(addr.Country))
            addrEl.Add(new XElement(Nc + "LocationCountryName", addr.Country));

        // Audit E-2 fix: Emit ONE <AddressDeliveryPoint> with multiple
        // <StreetFullText> children for multi-line addresses, matching baseline wire samples
        // (CIV-INI-002 / -003 / -007 all emit this form). Pre-fix code emitted TWO separate
        // <AddressDeliveryPoint> elements each with one child, which is arguably also valid per
        // the StreetType XSD definition (unbounded StreetFullText) but diverged from baseline.
        // Discovered via CIV-INI-002 round-trip test; unblocks the same issue on all multi-line
        // attorney / party addresses.
        if (!string.IsNullOrEmpty(addr.Address1) || !string.IsNullOrEmpty(addr.Address2))
        {
            var deliveryPoint = new XElement(Nc + "AddressDeliveryPoint",
                new XAttribute(Xsi + "type", "ns1:StreetType"));
            if (!string.IsNullOrEmpty(addr.Address1))
                deliveryPoint.Add(new XElement(Nc + "StreetFullText", addr.Address1));
            if (!string.IsNullOrEmpty(addr.Address2))
                deliveryPoint.Add(new XElement(Nc + "StreetFullText", addr.Address2));
            addrEl.Add(deliveryPoint);
        }

        if (!string.IsNullOrEmpty(addr.City))
            addrEl.Add(new XElement(Nc + "LocationCityName", addr.City));

        if (!string.IsNullOrEmpty(addr.State))
        {
            addrEl.Add(new XElement(Nc + "LocationStateName", addr.State));
            addrEl.Add(new XElement(Nc + "LocationState", addr.State));
        }

        if (!string.IsNullOrEmpty(addr.Zip))
            addrEl.Add(new XElement(Nc + "LocationPostalCode", addr.Zip));

        return addrEl;
    }

    // ─── Document ────────────────────────────────────────────────────

    private static XElement BuildDocument(FilingDocument doc, bool isLead)
    {
        var elemName = isLead ? Cfm + "FilingLeadDocument" : Cfm + "FilingConnectedDocument";
        var docEl = new XElement(elemName,
            new XAttribute(St + "id", doc.ReferenceId),
            new XAttribute(Xsi + "type", "ns9:DocumentExtType")
        );

        // complaintType attribute (links document to specific sub-case for subsequent filings)
        if (!string.IsNullOrEmpty(doc.ComplaintRef))
            docEl.Add(new XAttribute("complaintType", doc.ComplaintRef));

        // DocumentBinary
        // Bug #6 fix (Track A): BinaryFormatStandardName's xsi:type must reference the
        // niem-core TextType (http://niem.gov/niem/niem-core/2.0#TextType), NOT the UBL
        // UnqualifiedDataTypes TextType. Same pattern as Bug #4 (AmountInControversy).
        // Previous literal "ns4:TextType" relied on XDocument auto-binding ns4 to the
        // UBL UDT namespace; declare the `nc` prefix locally for unambiguous resolution.
        docEl.Add(new XElement(Nc + "DocumentBinary",
            new XElement(Nc + "BinaryFormatStandardName",
                new XAttribute(XNamespace.Xmlns + "nc", Nc.NamespaceName),
                new XAttribute(Xsi + "type", "nc:TextType"),
                doc.MimeType),
            new XElement(Nc + "BinaryLocationURI", doc.BinaryLocationUri)
        ));

        // DocumentDescriptionText (document code)
        docEl.Add(new XElement(Nc + "DocumentDescriptionText", doc.DocumentCode));

        // DocumentFileControlID
        if (!string.IsNullOrEmpty(doc.FileControlId))
            docEl.Add(new XElement(Nc + "DocumentFileControlID", doc.FileControlId));

        // DocumentIdentification
        var docIdEl = new XElement(Nc + "DocumentIdentification",
            new XElement(Nc + "IdentificationID", doc.DocumentCode)
        );
        if (!string.IsNullOrEmpty(doc.IdentificationSourceText))
            docIdEl.Add(new XElement(Nc + "IdentificationSourceText", doc.IdentificationSourceText));
        docEl.Add(docIdEl);

        // DocumentSequenceID
        docEl.Add(new XElement(Nc + "DocumentSequenceID", doc.SequenceNumber));

        // DocumentFilingMetaData (for subsequent filings with metadata)
        if (doc.MetadataValues.Count > 0)
        {
            var metaEl = new XElement(DfMeta + "DocumentFilingMetaData");
            foreach (var mv in doc.MetadataValues)
                metaEl.Add(BuildMetadataItem(mv));
            docEl.Add(metaEl);
        }

        // Name extension
        if (!string.IsNullOrEmpty(doc.NameExtension))
            docEl.Add(new XElement(CfmExt + "nameExtension", doc.NameExtension));

        return docEl;
    }

    // ─── idReferences emission (Step #14 silent-drop #10 fix) ────────

    /// <summary>
    /// Emit zero-or-more <c>&lt;idReferences&gt;</c> child elements onto <paramref name="itemEl"/>,
    /// preserving per-id <c>additionalInfoTags</c> fidelity. Used by the caseParticipant /
    /// attorney / caseAssignment existing-data branches in <see cref="BuildMetadataItem"/>.
    /// <para>
    /// Wire shape per WSDL <c>TaggedReferenceType</c> (FilingReview <c>Reference.cs</c>:
    /// 22240-22274): each <c>&lt;idReferences&gt;</c> element carries exactly one <c>&lt;id&gt;</c>
    /// child and zero-or-more <c>&lt;additionalInfoTags&gt;</c> children, where each tag is its
    /// OWN <c>&lt;tagType&gt;</c>+<c>&lt;tagValue&gt;</c> pair. Distinct references with distinct
    /// tags must keep their tags distinct on the wire.
    /// </para>
    /// <para>
    /// Source-of-truth selection:
    /// <list type="number">
    ///   <item>If <see cref="FilingMetadataValue.TaggedReferences"/> is non-empty, iterate it
    ///         and emit each ref with its OWN <c>Tags</c>. Canonical wire-correct path.</item>
    ///   <item>Else fall back to legacy parallel <see cref="FilingMetadataValue.IdReferences"/>
    ///         and <see cref="FilingMetadataValue.AdditionalInfoTags"/>. The legacy path
    ///         duplicates ALL flat tags onto EVERY idReferences element. Safe for the
    ///         single-id case (the dominant pattern across baselines + test fixtures), but
    ///         cross-contaminates in multi-id-with-tags scenarios. Production paths
    ///         (controller -> mapper) write to <c>TaggedReferences</c>; only test fixtures
    ///         and the parser back-compat shim still touch the legacy fields.</item>
    /// </list>
    /// </para>
    /// </summary>
    private static void EmitIdReferencesWithTags(XElement itemEl, FilingMetadataValue mv)
    {
        // ─── Step #39 — Path B forcing function ────────────────
        // Detect divergence between the canonical wire-source (TaggedReferences)
        // and the legacy back-compat projection (IdReferences). When BOTH lists
        // have data, they must agree (same ids in the same order). Diverging
        // states are the silent-drop class introduced by the Step #14 wire-source
        // ratchet — fixture authors who mutate ONLY the legacy field leave
        // TaggedReferences pinned to baseline parsed-from-XML values, and the
        // builder emits the stale baseline id while silently dropping the
        // override intent.
        //
        // Now safe to land: the lazy-migration sprint (Steps #16–#38) retired all
        // 23 Tier B SF fixtures to the `FilingMetadataValue.ReplaceWithSingleId`
        // helper which mutates both fields atomically. Zero un-migrated fixtures
        // remain in MaderaLiveFixtures.cs. Production paths (CourtFilingController,
        // MetadataValueMapper) write to IdReferences only on fresh mv objects with
        // empty TaggedReferences — they hit the legacy fallback path below, NOT
        // the divergence-throw above, because TaggedReferences.Count == 0.
        //
        // The asymmetric "one populated, other empty" case stays supported as
        // the legacy fallback path (production code + pre-Step-#14 tests). Only
        // BOTH-populated-and-divergent triggers the throw.
        if (mv.TaggedReferences.Count > 0 && mv.IdReferences.Count > 0)
        {
            var canonicalIds = mv.TaggedReferences.Select(t => t.Id).ToList();
            if (!canonicalIds.SequenceEqual(mv.IdReferences))
            {
                throw new InvalidOperationException(
                    $"FilingMetadataValue divergence detected (Step #14 silent-drop class). " +
                    $"Code={mv.Code ?? "<null>"}, ClassType={mv.ClassType ?? "<null>"}, " +
                    $"ValueRestriction={mv.ValueRestriction ?? "<null>"}. " +
                    $"TaggedReferences (canonical wire-source) = [{string.Join(",", canonicalIds)}]; " +
                    $"IdReferences (legacy projection) = [{string.Join(",", mv.IdReferences)}]. " +
                    $"These must agree when both are populated. The builder emits from " +
                    $"TaggedReferences; mutating only IdReferences silently drops the override. " +
                    $"Use FilingMetadataValue.ReplaceWithSingleId(id, tags) to mutate atomically.");
            }
        }

        // Canonical path: per-id tag fidelity from TaggedReferences.
        if (mv.TaggedReferences.Count > 0)
        {
            foreach (var tref in mv.TaggedReferences)
            {
                var refEl = new XElement(DfMeta + "idReferences",
                    new XElement(DfMeta + "id", tref.Id));
                foreach (var tag in tref.Tags)
                    refEl.Add(new XElement(DfMeta + "additionalInfoTags",
                        new XElement(DfMeta + "tagType", tag.TagType),
                        new XElement(DfMeta + "tagValue", tag.TagValue)));
                itemEl.Add(refEl);
            }
            return;
        }

        // Legacy back-compat: flat IdReferences + AdditionalInfoTags. Cross-contaminates if
        // both N > 1 ids and M > 0 tags are present, but no current callsite hits that.
        foreach (var idRef in mv.IdReferences)
        {
            var refEl = new XElement(DfMeta + "idReferences",
                new XElement(DfMeta + "id", idRef));
            foreach (var tag in mv.AdditionalInfoTags)
                refEl.Add(new XElement(DfMeta + "additionalInfoTags",
                    new XElement(DfMeta + "tagType", tag.TagType),
                    new XElement(DfMeta + "tagValue", tag.TagValue)));
            itemEl.Add(refEl);
        }
    }

    // ─── caseParticipantValue (Audit H-1 2026-04-22) ─────────────────

    /// <summary>
    /// Build a <c>&lt;caseParticipantValue&gt;</c> element carrying a new party's identity
    /// (EntityPerson or EntityOrganization) + CaseParticipantRoleCode + optional
    /// ContactInformation + eService flag. Used inside <c>&lt;documentFilingMetaDataItem&gt;</c>
    /// for classType="caseParticipant" with valueRestriction="new-data".
    /// <para>
    /// Namespace contract (verified against CIV-SUB-001 baseline lines 63-97):
    /// <list type="bullet">
    ///   <item><c>caseParticipantValue</c> wrapper: DfMeta namespace.</item>
    ///   <item><c>EntityPerson</c>/<c>EntityOrganization</c>: Ecf (CommonTypes-4.0) namespace —
    ///         same H-2 rule as <c>caseAssignmentValue</c>. Diverges from the niem-core
    ///         convention used by Case-level CaseParticipantExt.</item>
    ///   <item><c>PersonName</c>, <c>PersonOtherIdentification</c>, <c>OrganizationName</c>:
    ///         niem-core (unchanged).</item>
    ///   <item><c>CaseParticipantRoleCode</c>: Ecf.</item>
    ///   <item><c>ContactInformation</c>: niem-core (standard).</item>
    ///   <item><c>eService</c>: CpExt (CaseParticipantExt extension namespace).</item>
    /// </list>
    /// </para>
    /// </summary>
    private static XElement BuildCaseParticipantValue(FilingParty party)
    {
        var cpvEl = new XElement(DfMeta + "caseParticipantValue",
            new XAttribute(St + "id", party.ReferenceId ?? string.Empty));

        // Entity — EntityPerson for persons, EntityOrganization for orgs. Same dispatch as
        // BuildParticipant but emitting under Ecf namespace per H-2 rule (not Nc).
        if (party.IsOrganization)
        {
            var orgEl = new XElement(Ecf + "EntityOrganization");
            if (!string.IsNullOrEmpty(party.OrganizationName))
                orgEl.Add(new XElement(Nc + "OrganizationName", party.OrganizationName));
            cpvEl.Add(orgEl);
        }
        else
        {
            var personEl = new XElement(Ecf + "EntityPerson");
            var nameEl = new XElement(Nc + "PersonName");
            if (!string.IsNullOrEmpty(party.FirstName))
                nameEl.Add(new XElement(Nc + "PersonGivenName", party.FirstName));
            if (!string.IsNullOrEmpty(party.MiddleName))
                nameEl.Add(new XElement(Nc + "PersonMiddleName", party.MiddleName));
            if (!string.IsNullOrEmpty(party.LastName))
                nameEl.Add(new XElement(Nc + "PersonSurName", party.LastName));
            if (!string.IsNullOrEmpty(party.NameSuffix))
                nameEl.Add(new XElement(Nc + "PersonNameSuffixText", party.NameSuffix));
            personEl.Add(nameEl);
            cpvEl.Add(personEl);
        }

        // Alternate names (AKA, DBA, FDBA, FKA, ALIAS) — emitted as sibling EntityPerson /
        // EntityOrganization entries inside caseParticipantValue. Mirrors the BuildParticipant
        // path (Case-level CaseParticipantExt, line ~525) which already iterates AKAs.
        //
        // 2026-05-17 Tier B finding: pre-fix the SF metadata-driven new-party path silently
        // dropped AKAs at this layer even though Step #7 wired them through DTO + mapper and
        // Step #8 added Suffix support. The CC initial-filing flow (BuildParticipant) emitted
        // them; only the SF metadata flow had this gap. Closes the chain end-to-end on the SF
        // wire shape parallel to CIV-SUB-001 baseline (lines 80-95 — primary EntityPerson +
        // sibling AKA EntityPerson with PersonOtherIdentification AFS marker).
        foreach (var aka in party.AlternateNames)
        {
            if (!string.IsNullOrEmpty(aka.OrganizationName))
            {
                // Wrapper namespace = Ecf per H-2 rule (caseParticipantValue divergence from
                // niem-core convention used at Case-level CaseParticipantExt). Children remain
                // niem-core. Sub-element name matches BuildParticipant's CC-side precedent.
                var akaOrgEl = new XElement(Ecf + "EntityOrganization");
                akaOrgEl.Add(new XElement(Nc + "OrganizationName", aka.OrganizationName));
                if (!string.IsNullOrEmpty(aka.Type))
                {
                    akaOrgEl.Add(new XElement(Nc + "OrganizationIdentification",
                        new XElement(Nc + "IdentificationCategoryText", aka.Type)));
                }
                cpvEl.Add(akaOrgEl);
            }
            else
            {
                var akaPersonEl = new XElement(Ecf + "EntityPerson");
                var akaNameEl = new XElement(Nc + "PersonName");
                if (!string.IsNullOrEmpty(aka.FirstName))
                    akaNameEl.Add(new XElement(Nc + "PersonGivenName", aka.FirstName));
                if (!string.IsNullOrEmpty(aka.MiddleName))
                    akaNameEl.Add(new XElement(Nc + "PersonMiddleName", aka.MiddleName));
                if (!string.IsNullOrEmpty(aka.LastName))
                    akaNameEl.Add(new XElement(Nc + "PersonSurName", aka.LastName));
                if (!string.IsNullOrEmpty(aka.NameSuffix))
                    akaNameEl.Add(new XElement(Nc + "PersonNameSuffixText", aka.NameSuffix));
                akaPersonEl.Add(akaNameEl);
                if (!string.IsNullOrEmpty(aka.Type))
                {
                    akaPersonEl.Add(new XElement(Nc + "PersonOtherIdentification",
                        new XElement(Nc + "IdentificationCategoryText", aka.Type)));
                }
                cpvEl.Add(akaPersonEl);
            }
        }

        // CaseParticipantRoleCode — always emit; role is meaningful (PLAIN/DEF/etc.).
        cpvEl.Add(new XElement(Ecf + "CaseParticipantRoleCode", party.RoleCode ?? string.Empty));

        // ContactInformation — optional, nested inside caseParticipantValue.
        if (party.Contact != null)
            cpvEl.Add(BuildContactInformation(party.Contact));

        // eService — baseline CIV-SUB-001 emits this explicitly as "false" for a new party
        // (opt-out default). Always emit so opt-in (true) and opt-out (false) are both
        // preserved round-trip.
        cpvEl.Add(new XElement(CpExt + "eService", party.EService ? "true" : "false"));

        return cpvEl;
    }

    // ─── Document Filing Metadata Item ───────────────────────────────

    private static XElement BuildMetadataItem(FilingMetadataValue mv)
    {
        var itemEl = new XElement(DfMeta + "documentFilingMetaDataItem");

        // docValueMetaDataItem (describes what this metadata is)
        var descEl = new XElement(DfMeta + "docValueMetaDataItem");
        descEl.Add(new XElement(DfValue + "code", mv.Code));
        descEl.Add(new XElement(DfValue + "classType", mv.ClassType));
        if (!string.IsNullOrEmpty(mv.SubType))
            descEl.Add(new XElement(DfValue + "subType", mv.SubType));
        if (!string.IsNullOrEmpty(mv.ValueRestriction))
            descEl.Add(new XElement(DfValue + "valueRestriction", mv.ValueRestriction));
        itemEl.Add(descEl);

        // Value field based on ClassType
        switch (mv.ClassType.ToLowerInvariant())
        {
            case "text":
                if (mv.TextValue != null)
                    itemEl.Add(new XElement(DfMeta + "textValue", mv.TextValue));
                break;
            case "boolean":
                if (mv.BooleanValue.HasValue)
                    itemEl.Add(new XElement(DfMeta + "booleanValue", mv.BooleanValue.Value.ToString().ToLowerInvariant()));
                break;
            case "codelist":
                if (mv.CodeValue != null)
                    itemEl.Add(new XElement(DfMeta + "codeValue", mv.CodeValue));
                break;
            case "currency":
                if (mv.CurrencyValue.HasValue)
                    itemEl.Add(new XElement(DfMeta + "currencyValue", mv.CurrencyValue.Value));
                break;
            case "crsreceiptnumber":
                // Step #46 — JTI HTML Layer A evidence promotes this from
                // T-8 stub. Per the "Document Metadata / Class Types" section of
                // `docs/fileing files/Document Metadata/Document Metadata _ EFM
                // Documentation.html` line 626: *"String. The calendar reservation
                // number generated by the CRS system."* WSDL wire shape is the scalar
                // <crsReceiptNumberValue> wrapper at FilingReview/Reference.cs:11474.
                // Same evidence tier as `text` / `currency` (HTML doc only; no baseline
                // sample). Emission is unconditional-on-null so an empty value is
                // dropped silently (consistent with text arm semantics).
                if (mv.CrsReceiptNumberValue != null)
                    itemEl.Add(new XElement(DfMeta + "crsReceiptNumberValue", mv.CrsReceiptNumberValue));
                break;
            case "date":
                // Audit D-1 fix — baseline wire evidence (CIV-SUB-016 / CIV-SUB-017 Proof of
                // Personal Service SERVICE_DATE) emits <nc:DateTime> inside <dateValue>, NOT
                // <nc:Date>. Both are NIEM Core standard elements (Date for date-only;
                // DateTime for timestamp), but baseline consistently uses the DateTime form
                // at midnight with the court's local timezone offset.
                //
                // Format choice — yyyy-MM-ddTHH:mm:ss without timezone:
                //   • Matches ISO 8601 "local date-time" form.
                //   • Avoids server-timezone dependency in test assertions (emitting with the
                //     server's local offset would produce different XML in PST vs EST CI runs).
                //   • Baseline wire has `-08:00` offset, so future work may want to append
                //     the court's configured timezone — defer until a timezone-configuration
                //     story exists (see change log 2026-04-22 D-1 entry, "Deferred"  bullet).
                if (mv.DateValue.HasValue)
                    itemEl.Add(new XElement(DfMeta + "dateValue",
                        new XElement(Nc + "DateTime", mv.DateValue.Value.ToString("yyyy-MM-ddTHH:mm:ss"))));
                break;
            case "caseparticipant":
                // Existing-data: reference by ID (with optional additionalInfoTags). Step #14
                // audit — silent-drop #10 fix. Helper preserves per-id tag fidelity when the
                // canonical TaggedReferences field is populated; legacy IdReferences +
                // flat-list AdditionalInfoTags fallback kept for back-compat (single-id
                // callers only — multi-id legacy callers with tags WILL cross-contaminate;
                // production paths go through the mapper which writes TaggedReferences).
                EmitIdReferencesWithTags(itemEl, mv);
                // New-data: inline party data. Two wire shapes exist, dispatched by whether
                // the FilingParty carries substantive identity (Name/Role/Org) vs. only Contact.
                //
                // Audit H-1 fix: Baseline shape for new parties on subsequent
                // filings is <caseParticipantValue> with nested EntityPerson + RoleCode +
                // ContactInformation + eService (see CIV-SUB-001 lines 63-97). Pre-fix, the
                // builder only handled the contact-only legacy path (emitting <contactValue>
                // sibling), silently dropping names/role/eService. Fix: when the party has
                // substantive identity data, emit the full caseParticipantValue wrapper. When
                // only Contact is set (legacy C-2 Bug B test fixtures), continue emitting the
                // flat <contactValue> shape.
                foreach (var newParty in mv.NewPartyValues)
                {
                    var hasSubstantiveIdentity =
                        !string.IsNullOrEmpty(newParty.FirstName)
                        || !string.IsNullOrEmpty(newParty.LastName)
                        || !string.IsNullOrEmpty(newParty.OrganizationName)
                        || !string.IsNullOrEmpty(newParty.RoleCode);

                    if (hasSubstantiveIdentity)
                    {
                        itemEl.Add(BuildCaseParticipantValue(newParty));
                    }
                    else if (newParty.Contact != null)
                    {
                        // Legacy C-2 Bug B path: flat <contactValue> sibling inside the
                        // caseParticipant metadata item. Preserved for backward compatibility
                        // with callers that use NewPartyValue solely as a contact carrier.
                        var cv = newParty.Contact;
                        var contactEl = new XElement(DfMeta + "contactValue");
                        if (cv.MailingAddress != null)
                        {
                            if (!string.IsNullOrEmpty(cv.MailingAddress.Address1))
                                contactEl.Add(new XElement(ContactValueNs + "address1", cv.MailingAddress.Address1));
                            if (!string.IsNullOrEmpty(cv.MailingAddress.Address2))
                                contactEl.Add(new XElement(ContactValueNs + "address2", cv.MailingAddress.Address2));
                            if (!string.IsNullOrEmpty(cv.MailingAddress.City))
                                contactEl.Add(new XElement(ContactValueNs + "city", cv.MailingAddress.City));
                            if (!string.IsNullOrEmpty(cv.MailingAddress.Zip))
                                contactEl.Add(new XElement(ContactValueNs + "zip", cv.MailingAddress.Zip));
                            if (!string.IsNullOrEmpty(cv.MailingAddress.State))
                                contactEl.Add(new XElement(ContactValueNs + "state", cv.MailingAddress.State));
                            if (!string.IsNullOrEmpty(cv.MailingAddress.Country))
                                contactEl.Add(new XElement(ContactValueNs + "country", cv.MailingAddress.Country));
                            if (!string.IsNullOrEmpty(cv.MailingAddress.AddressType))
                                contactEl.Add(new XElement(ContactValueNs + "addressType", cv.MailingAddress.AddressType));
                        }
                        if (!string.IsNullOrEmpty(cv.PhoneNumber))
                        {
                            contactEl.Add(new XElement(ContactValueNs + "telephoneType", cv.PhoneType ?? "W"));
                            contactEl.Add(new XElement(ContactValueNs + "telephoneNumber", cv.PhoneNumber));
                        }
                        if (!string.IsNullOrEmpty(cv.Email))
                            contactEl.Add(new XElement(ContactValueNs + "email", cv.Email));
                        itemEl.Add(contactEl);
                    }
                }
                break;
            case "attorney":
                // Catalog §3.14 + schema knownBugs — residual c fix.
                // Pre-fix, attorney shared a fall-through with caseparticipant and emitted
                // <caseParticipantValue> for new-data, which is the wrong wrapper element.
                // Per JtiClassTypeSchema.json attorney entry, the correct wrapper is
                // <attorneyValue> (same wrapperNamespaceUri as caseParticipantValue). Attorney
                // is V2 awaiting-evidence (no baseline sample), so the internal shape is a
                // hypothesis — catalog §3.14 proposes the caseAssignment-style children
                // (PersonOtherIdentification + EntityOrganization + AssignmentRole). We emit
                // the caseParticipant-style children here as a minimal fix; when a first
                // sample or Madera GetPolicy capture arrives, swap in a dedicated helper.
                //
                // Existing-data: idReferences (shared pattern with caseParticipant and
                // caseAssignment). Step #14 audit — silent-drop #10 fix via shared helper.
                EmitIdReferencesWithTags(itemEl, mv);
                // New-data: <attorneyValue> wrapper. Reuses BuildCaseParticipantValue to build
                // the children tree, then renames the root element. XElement.Name is mutable so
                // this is safe; the children's namespaces stay intact.
                foreach (var newParty in mv.NewPartyValues)
                {
                    var hasAttorneyIdentity =
                        !string.IsNullOrEmpty(newParty.FirstName)
                        || !string.IsNullOrEmpty(newParty.LastName)
                        || !string.IsNullOrEmpty(newParty.OrganizationName)
                        || !string.IsNullOrEmpty(newParty.RoleCode);

                    if (hasAttorneyIdentity)
                    {
                        var atv = BuildCaseParticipantValue(newParty);
                        atv.Name = DfMeta + "attorneyValue";
                        itemEl.Add(atv);
                    }
                }
                break;
            case "caseassignment":
                // Existing-data: reference by ID (same as caseParticipant). Step #14 audit —
                // silent-drop #10 fix via shared helper.
                EmitIdReferencesWithTags(itemEl, mv);
                // New-data: inline attorney (caseAssignmentValue)
                if (mv.CaseAssignmentValue != null)
                {
                    var ca = mv.CaseAssignmentValue;
                    var caEl = new XElement(DfMeta + "caseAssignmentValue");

                    // Audit H-2 fix: baseline wires emit EntityPerson and
                    // EntityOrganization in the ECF CommonTypes-4.0 namespace (NOT niem-core)
                    // when they appear INSIDE caseAssignmentValue. This is a documented
                    // divergence from the niem-core usage elsewhere (CaseParticipantExt parties
                    // use nc:EntityPerson). Verified against CIV-SUB-005, CIV-SUB-002, etc.
                    // The CHILDREN (PersonName, PersonOtherIdentification, OrganizationName)
                    // remain in niem-core — only the Entity* wrapper moves to ECF.
                    var caPersonEl = new XElement(Ecf + "EntityPerson");
                    var caNameEl = new XElement(Nc + "PersonName");
                    if (!string.IsNullOrEmpty(ca.FirstName))
                        caNameEl.Add(new XElement(Nc + "PersonGivenName", ca.FirstName));
                    if (!string.IsNullOrEmpty(ca.MiddleName))
                        caNameEl.Add(new XElement(Nc + "PersonMiddleName", ca.MiddleName));
                    if (!string.IsNullOrEmpty(ca.LastName))
                        caNameEl.Add(new XElement(Nc + "PersonSurName", ca.LastName));
                    if (!string.IsNullOrEmpty(ca.NameSuffix))
                        caNameEl.Add(new XElement(Nc + "PersonNameSuffixText", ca.NameSuffix));
                    caPersonEl.Add(caNameEl);
                    if (!string.IsNullOrEmpty(ca.BarNumber))
                        caPersonEl.Add(new XElement(Nc + "PersonOtherIdentification",
                            new XElement(Nc + "IdentificationID", ca.BarNumber),
                            new XElement(Nc + "IdentificationCategoryText", "BAR")));
                    caEl.Add(caPersonEl);

                    // EntityOrganization (firm name) — also ECF per H-2 fix.
                    var caOrgEl = new XElement(Ecf + "EntityOrganization");
                    if (!string.IsNullOrEmpty(ca.FirmName))
                        caOrgEl.Add(new XElement(Nc + "OrganizationName", ca.FirmName));
                    caEl.Add(caOrgEl);

                    // ContactInformation
                    if (ca.Contact != null)
                        caEl.Add(BuildContactInformation(ca.Contact));

                    // AssignmentRole
                    caEl.Add(new XElement(CaseAssign + "AssignmentRole", ca.AssignmentRole));

                    // eService
                    if (ca.EService)
                        caEl.Add(new XElement(CpExt + "eService", "true"));

                    itemEl.Add(caEl);
                }
                break;
            case "contact":
                // Wrapper in DfMeta; children in ContactValueNs per catalog §3.4 wire contract
                // (audit C-2 Bug B fix). Baseline samples: CIV-SUB-003, CIV-SUB-006, CIV-SUB-012,
                // FAM-SUB-004, FAM-SUB-005.
                if (mv.ContactValue != null)
                {
                    var cv2 = mv.ContactValue;
                    var contactEl2 = new XElement(DfMeta + "contactValue");
                    if (!string.IsNullOrEmpty(cv2.Address1))
                        contactEl2.Add(new XElement(ContactValueNs + "address1", cv2.Address1));
                    if (!string.IsNullOrEmpty(cv2.Address2))
                        contactEl2.Add(new XElement(ContactValueNs + "address2", cv2.Address2));
                    if (!string.IsNullOrEmpty(cv2.City))
                        contactEl2.Add(new XElement(ContactValueNs + "city", cv2.City));
                    if (!string.IsNullOrEmpty(cv2.Zip))
                        contactEl2.Add(new XElement(ContactValueNs + "zip", cv2.Zip));
                    if (!string.IsNullOrEmpty(cv2.State))
                        contactEl2.Add(new XElement(ContactValueNs + "state", cv2.State));
                    if (!string.IsNullOrEmpty(cv2.Country))
                        contactEl2.Add(new XElement(ContactValueNs + "country", cv2.Country));
                    // Emit each phone child independently — wire evidence (catalog §3.4, CIV-SUB-003)
                    // shows <telephoneType> alone without <telephoneNumber>. Gating both on
                    // PhoneNumber alone would drop the type-only case. The phantom-telephoneNumber
                    // side bug is deferred per §3.4 "Known bugs / audit findings".
                    if (!string.IsNullOrEmpty(cv2.PhoneType))
                        contactEl2.Add(new XElement(ContactValueNs + "telephoneType", cv2.PhoneType));
                    if (!string.IsNullOrEmpty(cv2.PhoneNumber))
                        contactEl2.Add(new XElement(ContactValueNs + "telephoneNumber", cv2.PhoneNumber));
                    if (!string.IsNullOrEmpty(cv2.Email))
                        contactEl2.Add(new XElement(ContactValueNs + "email", cv2.Email));
                    if (!string.IsNullOrEmpty(cv2.AddressType))
                        contactEl2.Add(new XElement(ContactValueNs + "addressType", cv2.AddressType));
                    itemEl.Add(contactEl2);
                }
                break;
            case "judgment":
                // Step #15 audit (Path C — see docs/STEP15_JUDGMENT_AUDIT.md §2 + §9):
                // wire shape per LASC vendor sample 'Example Filing a Writ of Return Sample.xml':
                //   <ns9:judgments>
                //     <ns10:judgmentId>{id}</ns10:judgmentId>
                //   </ns9:judgments>
                //
                // Wrapper <judgments> lives in DfMeta (ns9), but the <judgmentId> child lives
                // in CourtEventJudgmentNs (ns10) per WSDL CourtEventJudgmentType. Judgment is
                // the ONLY classType where wrapper + content namespaces split. Multiple
                // <judgmentId> children may appear inside one <judgments> wrapper (WSDL declares
                // CourtEventJudgmentType[] — array). Observed sample only has 1, but multi-id
                // emission is wire-spec valid.
                //
                // New-data path (filing a brand-new judgment with full JudgmentAwardType +
                // JudgmentAwardPartyType) is awaitingEvidence — NO observed sample. Throw with
                // a clear message naming the schema flag so a future implementer knows where
                // to find the spec.
                if (!string.Equals(mv.ValueRestriction, "existing-data", StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotImplementedException(
                        $"ReviewFilingXmlBuilder.BuildMetadataItem: classType 'judgment' new-data path " +
                        $"is not implemented. Schema marks newData.awaitingEvidence=true " +
                        $"(JtiClassTypeSchema.json #/classTypes/judgment/valueRestrictions/newData) " +
                        $"— the only observed wire shape is existing-data (LASC Writ of Return " +
                        $"Sample). Capture a baseline sample exercising JudgmentAwardType + " +
                        $"JudgmentAwardPartyType, then implement this path. " +
                        $"ValueRestriction='{mv.ValueRestriction ?? "<null>"}', " +
                        $"Code='{mv.Code}'.");
                }
                if (mv.JudgmentIds != null && mv.JudgmentIds.Count > 0)
                {
                    var judgmentsEl = new XElement(DfMeta + "judgments");
                    foreach (var jId in mv.JudgmentIds)
                    {
                        if (!string.IsNullOrEmpty(jId))
                            judgmentsEl.Add(new XElement(CourtEventJudgmentNs + "judgmentId", jId));
                    }
                    if (judgmentsEl.HasElements)
                        itemEl.Add(judgmentsEl);
                }
                break;
            default:
                // Catalog §3.0 observation #3 — fail-closed on unknown / unimplemented classTypes
                // (residual b fix + T-8 stub sweep, 2026-04-23 evening). Pre-fix: the switch had
                // no default arm, so any classType the builder didn't recognize produced a
                // docValueMetaDataItem with only the descriptor and no value child — silently
                // dropping the caller's data.
                //
                // Post-fix distinguishes two failure modes by consulting JtiClassTypeSchema.json:
                //
                //   (a) Schema-declared but not yet implemented. As of T-3a close,
                //       10 classTypes fall in this bucket: crsReceiptNumber, number, email,
                //       action, address, document, scheduledEvent, caseSpecialStatus, judgment,
                //       relatedCase. The caller gets a NotImplementedException naming the
                //       expected wire wrapper element + evidence level, so the next engineer
                //       can add a case arm without archaeology. This replaces the "10 stub
                //       arms" design — DRY, and auto-extends to any future classType the schema
                //       picks up before the builder does.
                //
                //   (b) Completely unknown (typo, controller misrouting, forgotten schema
                //       entry). Caller gets an InvalidOperationException directing them to
                //       fix the upstream caller or add the classType to the schema first.
                var schemaDef = JtiFieldSchemaProvider.GetClassTypeV2(mv.ClassType);
                if (schemaDef is not null)
                {
                    throw new NotImplementedException(
                        $"ReviewFilingXmlBuilder.BuildMetadataItem: classType '{mv.ClassType}' " +
                        $"(code '{mv.Code}') is declared in JtiClassTypeSchema.json but has no " +
                        $"builder arm yet. Expected wire wrapper: <{schemaDef.Wire?.WrapperElement ?? "?"}> " +
                        $"(evidence level '{schemaDef.Evidence?.Level ?? "?"}', " +
                        $"awaitingEvidence={schemaDef.AwaitingEvidence}). " +
                        $"Add a switch arm above emitting the wrapper element per the schema's " +
                        $"wire contract, then close the T-8 entry for this classType.");
                }
                throw new InvalidOperationException(
                    $"ReviewFilingXmlBuilder.BuildMetadataItem: unknown classType '{mv.ClassType}' " +
                    $"for metadata code '{mv.Code}'. This classType is NOT declared in " +
                    $"JtiClassTypeSchema.json (19 known entries as of T-3a). Either (a) fix the " +
                    $"upstream caller that created this FilingMetadataValue, or (b) add the " +
                    $"classType to the schema and implement a builder arm. See catalog §3.0 " +
                    $"observation #3 (fail-closed on unknowns).");
        }

        return itemEl;
    }

    // ─── Payment ─────────────────────────────────────────────────────

    private static XElement BuildPaymentMessage(FilingSubmission sub)
    {
        var payment = sub.Payment ?? new FilingPayment();

        var payEl = new XElement(Pay + "PaymentMessage",
            new XAttribute(Xsi + "type", "ns12:PaymentMessageTypeExt")
        );

        var authEl = new XElement(PayExt + "paymentAuthorizationInfo",
            new XElement(PayExt + "customerProfileId", payment.CustomerProfileId),
            new XElement(PayExt + "customerPaymentProfileId", payment.CustomerPaymentProfileId),
            new XElement(PayExt + "paymentType", payment.PaymentType)
        );

        if (!string.IsNullOrEmpty(payment.CustomerAchId))
            authEl.Add(new XElement(PayExt + "customerACHId", payment.CustomerAchId));

        // Transaction authorizations (for pre-authorized payments)
        foreach (var ta in payment.TransactionAuthorizations)
        {
            authEl.Add(new XElement(PayExt + "transactionAuthorization",
                new XElement(PayExt + "authorizationCode", ta.AuthorizationCode),
                new XElement(PayExt + "transactionId", ta.TransactionId),
                new XElement(PayExt + "authorizationType", ta.AuthorizationType),
                new XElement(PayExt + "externalId", ta.ExternalId),
                new XElement(PayExt + "amount", ta.Amount)
            ));
        }

        payEl.Add(authEl);

        if (!string.IsNullOrEmpty(payment.UserName))
            payEl.Add(new XElement(PayExt + "userName", payment.UserName));
        if (!string.IsNullOrEmpty(payment.Email))
            payEl.Add(new XElement(PayExt + "email", payment.Email));
        if (!string.IsNullOrEmpty(payment.PhoneNumber))
            payEl.Add(new XElement(PayExt + "phoneNumber", payment.PhoneNumber));

        return payEl;
    }
}
