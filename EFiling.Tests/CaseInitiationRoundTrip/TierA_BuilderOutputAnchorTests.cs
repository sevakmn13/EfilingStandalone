using System.Text.Json;
using System.Xml.Linq;
using EFiling.Core.Models;
using EFiling.Nop.Controllers;
using EFiling.Nop.Models;
using EFiling.Providers.JTI.Builders;
using EFiling.Providers.JTI.Soap;

namespace EFiling.Tests.CaseInitiationRoundTrip;

/// <summary>
/// Tier A — Forward-direction wire-shape anchor tests for Case Initiation.
///
/// <para>
/// Mirrors <see cref="EFiling.Tests.SubsequentFilingRoundTrip.TierA_BuilderOutputAnchorTests"/>
/// but exercises the Case Initiation path (new case creation). Runs the full forward pipeline
/// <c>CreateCaseModel → CourtFilingController.BuildSubmissionFromCreateModel → ReviewFilingXmlBuilder</c>
/// and asserts the emitted XML contains wire elements matching baseline CI samples.
/// </para>
///
/// <para>
/// These tests are DEFENSIVE — the 2026-04-22 CI audit scan found no silent-drop or wrong-element
/// bugs in the CI code path (prior "Track D.post C-1/C-2/C-3" and "Track A #4/#6" fix rounds
/// already hardened it). These tests LOCK IN the current-correct wire shapes so any future
/// regression — intentional refactor or accidental change — produces a clear red signal tied
/// to a named baseline scenario. See catalog entry 2026-04-22 'Case Initiation audit scan'.
/// </para>
///
/// <para>
/// Scenario coverage picked for maximum wire-feature diversity:
/// <list type="bullet">
///   <item><c>CIV-INI-001</c> — basic plaintiff person + organization defendant (EntityPerson + EntityOrganization variants).</item>
///   <item><c>CIV-INI-004</c> — FEE_WAIVER fee exemption (efmFeeExemptionRequestType wire shape).</item>
///   <item><c>CIV-INI-006</c> — GOVT_ENTITY fee exemption + org plaintiff (combined wire features).</item>
///   <item><c>CIV-INI-010</c> — Self-Rep with eService + contact (party-level eService element + ContactInformation).</item>
///   <item><c>CIV-INI-011</c> — Interpreter language + attorney (efmInterpreterLanguage + PersonOtherIdentification/BAR).</item>
/// </list>
/// </para>
///
/// <para>
/// Forward-direction only. Round-trip (parse → rebuild → bit-equivalent diff) remains deferred
/// until a reverse parser exists, same as for Subsequent Filing.
/// </para>
/// </summary>
public class TierA_BuilderOutputAnchorTests
{
    private static readonly XNamespace NsNiemCore = SoapEnvelopeBuilder.NsNiemCore;
    private static readonly XNamespace NsCommonTypes = SoapEnvelopeBuilder.NsCommonTypes;
    private static readonly XNamespace NsCpExt = SoapEnvelopeBuilder.NsJtiCaseParticipantExt;
    private static readonly XNamespace NsCivExt = SoapEnvelopeBuilder.NsJtiCivilCaseExt;
    private static readonly XNamespace NsStructures = SoapEnvelopeBuilder.NsStructures;

    /// <summary>
    /// Test court config matching the one used in sibling anchor tests / ReviewFilingXmlBuilderTests.
    /// URLs need not resolve; they only serialize into envelope-level fields these tests don't assert on.
    /// </summary>
    private static CourtConfiguration TestConfig => new()
    {
        CourtId = "madera",
        DisplayName = "Madera Superior Court",
        SoapEndpoint = "https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/FilingReview/",
        RestBaseUrl = "https://aux-pub-efm-madera-ca.ecourt.com/ws/rest/ecourt",
        NfrcCallbackUrl = "https://staging.legalhub.com/api/efiling/nfrc"
    };

    /// <summary>
    /// Minimal Case-Initiation <see cref="CreateCaseModel"/> base. Tests extend it with
    /// scenario-specific parties / attorneys / metadata. <c>IsSubsequentFiling</c> defaults
    /// false, so the controller routes through the initial-filing path.
    /// </summary>
    private static CreateCaseModel NewInitModel()
    {
        var model = new CreateCaseModel
        {
            CourtId = "madera",
            IsSubsequentFiling = false,
            CaseTypeCode = "421110",
            CaseCategoryCode = "411900",
            JurisdictionalGroundsCode = "L10",
            AmountInControversy = 0m,
        };
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto
            {
                Role = "lead",
                DocumentCode = "401068",
                BlobUrl = "https://files.example.com/complaint.pdf",
            }
        });
        return model;
    }

    /// <summary>
    /// Locates the <c>CaseParticipantExt</c> element (JTI extension namespace) with the given
    /// structures-id. Returns null if not found. Tests use this to target assertions at a
    /// specific party (filedBy0 / filedAsTo0 / attorney0).
    /// </summary>
    private static XElement? FindParticipantById(XDocument doc, string id)
    {
        return doc.Descendants(NsCpExt + "CaseParticipantExt")
            .FirstOrDefault(p => p.Attribute(NsStructures + "id")?.Value == id);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-001 — basic two-party case (plaintiff person + organization defendant).
    // Baseline: @.../Civil & Small Claims/New Case (Civil Limited) Sample.xml
    // Wire features exercised:
    //   • EntityPerson with PersonName (plaintiff)
    //   • EntityOrganization with OrganizationName (defendant)
    //   • CaseParticipantRoleCode = PLAIN / DEF
    //   • No special extension fields (baseline sentinel — simplest scenario)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_001_BuilderEmits_BasicPlaintiffPerson_AndOrganizationDefendant()
    {
        var model = NewInitModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto
            {
                Side = "filing",
                PartyType = "PLAIN",
                FirstName = "Mark",
                LastName = "Smith",
            },
            new PartyEntryDto
            {
                Side = "opposing",
                PartyType = "DEF",
                IsOrganization = true,
                OrganizationName = "Acme Corp",
            },
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model, TestConfig);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        var plaintiff = FindParticipantById(doc, "filedBy0");
        Assert.NotNull(plaintiff);
        Assert.Equal("PLAIN", plaintiff!.Element(NsCommonTypes + "CaseParticipantRoleCode")?.Value);
        var plaintiffPerson = plaintiff.Element(NsNiemCore + "EntityPerson");
        Assert.NotNull(plaintiffPerson);
        Assert.Equal("Mark", plaintiffPerson!.Descendants(NsNiemCore + "PersonGivenName").FirstOrDefault()?.Value);
        Assert.Equal("Smith", plaintiffPerson.Descendants(NsNiemCore + "PersonSurName").FirstOrDefault()?.Value);

        var defendant = FindParticipantById(doc, "filedAsTo0");
        Assert.NotNull(defendant);
        Assert.Equal("DEF", defendant!.Element(NsCommonTypes + "CaseParticipantRoleCode")?.Value);
        var defendantOrg = defendant.Element(NsNiemCore + "EntityOrganization");
        Assert.NotNull(defendantOrg);
        Assert.Equal("Acme Corp", defendantOrg!.Element(NsNiemCore + "OrganizationName")?.Value);

        // Defensive — no stray efm* extension fields on a party that shouldn't have any.
        Assert.Null(plaintiff.Element(NsCpExt + "efmFeeExemptionRequestType"));
        Assert.Null(plaintiff.Element(NsCpExt + "efmInterpreterLanguage"));
        Assert.Null(plaintiff.Element(NsCpExt + "eService"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-004 — fee waiver on filing party.
    // Baseline: @.../Civil & Small Claims/New Case (Fee Waiver) Sample.xml
    // Wire evidence:
    //   <p:CaseParticipantExt st:id="filedBy0">
    //     <p:efmFeeExemptionRequestType>FEE_WAIVER</p:efmFeeExemptionRequestType>
    //     <p1:CaseParticipantRoleCode>PLAIN</p1:CaseParticipantRoleCode>
    //     ...
    // Note: wire places efmFeeExemptionRequestType BEFORE CaseParticipantRoleCode (per baseline).
    // Our builder emits CaseParticipantRoleCode first, then efmFeeExemptionRequestType (per XSD
    // extension order). See catalog 2026-04-22 'Case Initiation audit scan' — this ordering
    // ambiguity was investigated and deferred. The tests assert PRESENCE and VALUE of the
    // element, not its sibling order, so they pass for either wire ordering.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_004_BuilderEmits_EfmFeeExemptionRequestType_AsFeeWaiver()
    {
        var model = NewInitModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto
            {
                Side = "filing",
                PartyType = "PLAIN",
                FirstName = "Stephen",
                LastName = "Smith",
                FeeExemptionType = "FEE_WAIVER",
            },
            new PartyEntryDto
            {
                Side = "opposing",
                PartyType = "DEF",
                FirstName = "Jack",
                LastName = "Jackson",
            },
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model, TestConfig);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        var plaintiff = FindParticipantById(doc, "filedBy0");
        Assert.NotNull(plaintiff);

        // Audit C-3c parallel — the fee-exemption TAG semantic in subsequent filing and the
        // fee-exemption REQUEST-TYPE element in case initiation are semantically equivalent
        // but use entirely different wire shapes. This asserts the CI form.
        var exemptEl = plaintiff!.Element(NsCpExt + "efmFeeExemptionRequestType");
        Assert.NotNull(exemptEl);
        Assert.Equal("FEE_WAIVER", exemptEl!.Value);

        // Defendant has no exemption → no extension element.
        var defendant = FindParticipantById(doc, "filedAsTo0");
        Assert.NotNull(defendant);
        Assert.Null(defendant!.Element(NsCpExt + "efmFeeExemptionRequestType"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-006 — government entity exempt + organization plaintiff.
    // Baseline: @.../Civil & Small Claims/New Case filed by Gov Ent Exempt party Sample.xml
    // Wire evidence:
    //   <p:CaseParticipantExt st:id="filedBy0">
    //     <p:efmFeeExemptionRequestType>GOVT_ENTITY</p:efmFeeExemptionRequestType>
    //     <p1:CaseParticipantRoleCode>PLAIN</p1:CaseParticipantRoleCode>
    //     <p3:EntityOrganization xsi:type="ps1:OrganizationType">
    //       <p3:OrganizationName>County of Placer</p3:OrganizationName>
    //     </p3:EntityOrganization>
    // Exercises combined wire features: string-enum exemption + EntityOrganization variant.
    // Critical for confirming that an organization plaintiff with a fee exemption produces
    // BOTH elements correctly — distinct from CIV-INI-001 (org defendant, no exemption).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_006_BuilderEmits_GovtEntityExemption_OnOrganizationPlaintiff()
    {
        var model = NewInitModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto
            {
                Side = "filing",
                PartyType = "PLAIN",
                IsOrganization = true,
                OrganizationName = "County of Madera",
                FeeExemptionType = "GOVT_ENTITY",
            },
            new PartyEntryDto
            {
                Side = "opposing",
                PartyType = "DEF",
                FirstName = "Allen",
                LastName = "Smith",
            },
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model, TestConfig);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        var plaintiff = FindParticipantById(doc, "filedBy0");
        Assert.NotNull(plaintiff);

        var exemptEl = plaintiff!.Element(NsCpExt + "efmFeeExemptionRequestType");
        Assert.NotNull(exemptEl);
        Assert.Equal("GOVT_ENTITY", exemptEl!.Value);

        // Organization plaintiff — EntityOrganization present, no EntityPerson.
        var orgEl = plaintiff.Element(NsNiemCore + "EntityOrganization");
        Assert.NotNull(orgEl);
        Assert.Equal("County of Madera", orgEl!.Element(NsNiemCore + "OrganizationName")?.Value);
        Assert.Null(plaintiff.Element(NsNiemCore + "EntityPerson"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-010 — Self-Rep plaintiff with eService consent + contact info.
    // Baseline: @.../Civil & Small Claims/New Case with Consent to eService Self Represented Sample.xml
    // Wire evidence:
    //   <p:CaseParticipantExt st:id="filedBy0">
    //     <p1:CaseParticipantRoleCode>PLAIN</p1:CaseParticipantRoleCode>
    //     <p2:EntityPerson>...</p2:EntityPerson>
    //     <ci1:ContactInformation>
    //       <ci1:ContactMailingAddress>...</ci1:ContactMailingAddress>
    //       <ci1:ContactEmailID>user@example.com</ci1:ContactEmailID>
    //     </ci1:ContactInformation>
    //     <p:eService>true</p:eService>
    //   </p:CaseParticipantExt>
    // Self-Rep contact-info population is controlled by the SelfRepresented flag in controller
    // (Track D.post fix C-3 — partyContact built only when self-rep + any address/phone/email).
    // eService is emitted as an extension element appearing AFTER ContactInformation.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_010_BuilderEmits_EService_AndContact_OnSelfRepPlaintiff()
    {
        var model = NewInitModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto
            {
                Side = "filing",
                PartyType = "PLAIN",
                FirstName = "Mark",
                LastName = "Davis",
                SelfRepresented = true,
                EService = true,
                AddressType = "HM",
                Address1 = "1222 South Davis St",
                City = "Sacramento",
                State = "CA",
                Zip = "95747",
                Country = "US",
                Email = "mark.davis@example.com",
            },
            new PartyEntryDto
            {
                Side = "opposing",
                PartyType = "DEF",
                FirstName = "Ron",
                LastName = "Jackson",
            },
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model, TestConfig);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        var plaintiff = FindParticipantById(doc, "filedBy0");
        Assert.NotNull(plaintiff);

        // eService — extension element asserting party consented to electronic service.
        var eServiceEl = plaintiff!.Element(NsCpExt + "eService");
        Assert.NotNull(eServiceEl);
        Assert.Equal("true", eServiceEl!.Value);

        // ContactInformation populated for self-rep (Track D.post fix C-3 regression gate).
        var contactInfo = plaintiff.Element(NsNiemCore + "ContactInformation");
        Assert.NotNull(contactInfo);
        Assert.Equal("mark.davis@example.com",
            contactInfo!.Element(NsNiemCore + "ContactEmailID")?.Value);

        // Address present inside ContactMailingAddress → StructuredAddress (ExtAddr namespace).
        var mailingAddr = contactInfo.Element(NsNiemCore + "ContactMailingAddress");
        Assert.NotNull(mailingAddr);
        var structuredAddr = mailingAddr!.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "StructuredAddress");
        Assert.NotNull(structuredAddr);
        Assert.Equal("Sacramento",
            structuredAddr!.Element(NsNiemCore + "LocationCityName")?.Value);

        // Defendant (non-self-rep) has no contact info propagated.
        var defendant = FindParticipantById(doc, "filedAsTo0");
        Assert.NotNull(defendant);
        Assert.Null(defendant!.Element(NsNiemCore + "ContactInformation"));
        Assert.Null(defendant.Element(NsCpExt + "eService"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-INI-011 — plaintiff with interpreter language + attorney representation.
    // Baseline: @.../Civil & Small Claims/New Case with Interpreter language requested, add existing attorney Sample.xml
    // Wire evidence:
    //   <p:CaseParticipantExt st:id="filedBy0">
    //     <p:efmInterpreterLanguage>109</p:efmInterpreterLanguage>
    //     <p1:CaseParticipantRoleCode>PLAIN</p1:CaseParticipantRoleCode>
    //     ...
    //   <p:CaseParticipantExt st:id="attorney0">
    //     <p1:CaseParticipantRoleCode>ATT</p1:CaseParticipantRoleCode>
    //     <p2:EntityPerson>
    //       <p2:PersonName>...</p2:PersonName>
    //       <poi1:PersonOtherIdentification>
    //         <poi1:IdentificationID>712345</poi1:IdentificationID>
    //         <poi1:IdentificationCategoryText>BAR</poi1:IdentificationCategoryText>
    //       </poi1:PersonOtherIdentification>
    //     </p2:EntityPerson>
    //     <p3:EntityOrganization xsi:type="ps1:OrganizationType"/>   ← firm placeholder
    //     <ci1:ContactInformation>...</ci1:ContactInformation>
    //   </p:CaseParticipantExt>
    //
    // Exercises multiple wire features simultaneously:
    //   • efmInterpreterLanguage extension field
    //   • Attorney party with BAR identification
    //   • Attorney firm as EntityOrganization sibling of EntityPerson
    //   • Attorney contact information
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_INI_011_BuilderEmits_InterpreterLanguage_AndAttorney_WithBarNumber()
    {
        var model = NewInitModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto
            {
                Side = "filing",
                PartyType = "PLAIN",
                FirstName = "Jack",
                LastName = "Williams",
                InterpreterLanguage = "109",
            },
            new PartyEntryDto
            {
                Side = "opposing",
                PartyType = "DEF",
                FirstName = "William",
                LastName = "King",
            },
        });
        model.AttorneysJson = JsonSerializer.Serialize(new[]
        {
            new AttorneyEntryDto
            {
                Side = "filing",
                FirstName = "Sheldon",
                MiddleName = "Dee",
                LastName = "Cooper",
                BarNumber = "712345",
                FirmName = "Cooper & Hofstadter LLP",
                AddressType = "W",
                Address1 = "500 Main St",
                City = "Pasadena",
                State = "CA",
                Zip = "91101",
                Country = "US",
                Email = "sheldon@example.com",
            }
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model, TestConfig);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        // Plaintiff with interpreter language ────────────────────────────────
        var plaintiff = FindParticipantById(doc, "filedBy0");
        Assert.NotNull(plaintiff);

        var interpreterEl = plaintiff!.Element(NsCpExt + "efmInterpreterLanguage");
        Assert.NotNull(interpreterEl);
        Assert.Equal("109", interpreterEl!.Value);

        // Attorney party with BAR identification ──────────────────────────────
        var attorney = FindParticipantById(doc, "attorney0");
        Assert.NotNull(attorney);
        Assert.Equal("ATT", attorney!.Element(NsCommonTypes + "CaseParticipantRoleCode")?.Value);

        var attorneyPerson = attorney.Element(NsNiemCore + "EntityPerson");
        Assert.NotNull(attorneyPerson);

        var personOtherId = attorneyPerson!.Element(NsNiemCore + "PersonOtherIdentification");
        Assert.NotNull(personOtherId);
        Assert.Equal("712345", personOtherId!.Element(NsNiemCore + "IdentificationID")?.Value);
        Assert.Equal("BAR", personOtherId.Element(NsNiemCore + "IdentificationCategoryText")?.Value);

        // Attorney's firm as EntityOrganization sibling. Audit E-1 fix (discovered via
        // CIV-INI-003 round-trip): builder previously emitted <EntityOrganization/> empty for
        // ALL ATT parties, silently dropping the firm name. Now emits OrganizationName when set.
        var firm = attorney.Element(NsNiemCore + "EntityOrganization");
        Assert.NotNull(firm);
        Assert.Equal("Cooper & Hofstadter LLP",
            firm!.Element(NsNiemCore + "OrganizationName")?.Value);

        // Attorney contact present.
        var attorneyContact = attorney.Element(NsNiemCore + "ContactInformation");
        Assert.NotNull(attorneyContact);
        Assert.Equal("sheldon@example.com",
            attorneyContact!.Element(NsNiemCore + "ContactEmailID")?.Value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // FAM-INI-001 — Family Law Dissolution with REPRESENTEDBY attorney association.
    // Baseline: @.../Family Law/New Case (Dissolution) Sample.xml
    // Wire evidence (lines 100-104):
    //   <ns6:relatedParticipants>
    //     <ns6:associationType>REPRESENTEDBY</ns6:associationType>
    //     <ns6:participant st:ref="filedBy0"/>
    //     <ns6:relatedParticipant st:ref="attorney0"/>
    //   </ns6:relatedParticipants>
    //
    // Exercises code paths NOT hit by any prior anchor test:
    //   • Controller's LeadAttorneyIdx → PartyAssociations pipeline (CourtFilingController.cs:608-625).
    //   • Builder's PartyAssociations → <relatedParticipants> emission (ReviewFilingXmlBuilder.cs:378-385).
    //   • Family-specific role codes PET / RES (instead of Civil's PLAIN / DEF).
    //
    // Note: Family Law CI uses the SAME <ns1:Case xsi:type="ns6:CivilCaseTypeExt"> wire shape as
    // Civil CI — not a separate DomesticCase namespace. Only the role codes differ. This is why
    // the single REPRESENTEDBY test is sufficient for Family coverage; adding 3 more Family
    // anchor tests would test the same code paths with different string values. See catalog
    // entry 2026-04-22 'CI forward-direction anchor tests landed'.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FAM_INI_001_BuilderEmits_RepresentedByAssociation_ForAttorneyOnFilingParty()
    {
        var model = NewInitModel();
        // Family-law-specific case codes (Dissolution baseline).
        model.CaseCategoryCode = "211120";
        model.CaseTypeCode = "211110";
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto
            {
                Side = "filing",
                PartyType = "PET",
                FirstName = "Jessica",
                LastName = "Williams",
                // Link to attorney[0] — triggers REPRESENTEDBY emission in controller pipeline.
                LeadAttorneyIdx = 0,
            },
            new PartyEntryDto
            {
                Side = "opposing",
                PartyType = "RES",
                FirstName = "Mark",
                LastName = "Williams",
            },
        });
        model.AttorneysJson = JsonSerializer.Serialize(new[]
        {
            new AttorneyEntryDto
            {
                Side = "filing",
                FirstName = "William",
                LastName = "Donnelly",
                BarNumber = "123418",
            }
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model, TestConfig);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        // Assert — REPRESENTEDBY association emitted in CaseAugmentationExt.
        var relatedParts = doc.Descendants(NsCivExt + "relatedParticipants").ToList();
        Assert.NotEmpty(relatedParts);

        var representedBy = relatedParts
            .FirstOrDefault(rp => rp.Element(NsCivExt + "associationType")?.Value == "REPRESENTEDBY");
        Assert.NotNull(representedBy);

        // Participant references — filing party points to attorney (catalog §3.3 wire evidence).
        var participantRef = representedBy!.Element(NsCivExt + "participant")
            ?.Attribute(NsStructures + "ref")?.Value;
        var relatedRef = representedBy.Element(NsCivExt + "relatedParticipant")
            ?.Attribute(NsStructures + "ref")?.Value;
        Assert.Equal("filedBy0", participantRef);
        Assert.Equal("attorney0", relatedRef);

        // Family-specific role codes wired correctly (sanity check — builder is role-code-agnostic).
        var petitioner = FindParticipantById(doc, "filedBy0");
        Assert.NotNull(petitioner);
        Assert.Equal("PET", petitioner!.Element(NsCommonTypes + "CaseParticipantRoleCode")?.Value);

        var respondent = FindParticipantById(doc, "filedAsTo0");
        Assert.NotNull(respondent);
        Assert.Equal("RES", respondent!.Element(NsCommonTypes + "CaseParticipantRoleCode")?.Value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // FAM-INI-003 — DCSS Support Case with Gov Entity exemption on PET role.
    // Baseline: @.../Family Law/New DCSS Support Case with Govt. Exemption Sample.xml
    // Wire evidence (lines 26-32):
    //   <p:CaseParticipantExt st:id="filedBy0">
    //     <p:efmFeeExemptionRequestType>GOVT_ENTITY</p:efmFeeExemptionRequestType>
    //     <p1:CaseParticipantRoleCode>PET</p1:CaseParticipantRoleCode>   ← PET, not PLAIN
    //     <p3:EntityOrganization xsi:type="ps1:OrganizationType">
    //       <p3:OrganizationName>Placer County Family Support</p3:OrganizationName>
    //     </p3:EntityOrganization>
    //
    // Complements CIV-INI-006 (GOVT_ENTITY on PLAIN role) — verifies the GOVT_ENTITY exemption
    // wire shape is emitted correctly regardless of role code. Defensive against a hypothetical
    // regression that special-cases the PLAIN role code.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FAM_INI_003_BuilderEmits_GovtEntityExemption_OnPetRoleCode_WithOrganizationPlaintiff()
    {
        var model = NewInitModel();
        model.CaseCategoryCode = "241110";
        model.CaseTypeCode = "211110";
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto
            {
                Side = "filing",
                PartyType = "PET",  // Family role code — distinct from CIV-INI-006's PLAIN.
                IsOrganization = true,
                OrganizationName = "County Family Support",
                FeeExemptionType = "GOVT_ENTITY",
            },
            new PartyEntryDto
            {
                Side = "opposing",
                PartyType = "RES",
                FirstName = "Mark",
                LastName = "Smith",
            },
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model, TestConfig);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        var petitioner = FindParticipantById(doc, "filedBy0");
        Assert.NotNull(petitioner);

        // Role code propagated correctly (role-code-agnostic builder).
        Assert.Equal("PET", petitioner!.Element(NsCommonTypes + "CaseParticipantRoleCode")?.Value);

        // GOVT_ENTITY exemption emitted regardless of role code.
        var exemptEl = petitioner.Element(NsCpExt + "efmFeeExemptionRequestType");
        Assert.NotNull(exemptEl);
        Assert.Equal("GOVT_ENTITY", exemptEl!.Value);

        // Organization plaintiff correctly emitted (sanity check for Family + Org combo).
        var orgEl = petitioner.Element(NsNiemCore + "EntityOrganization");
        Assert.NotNull(orgEl);
        Assert.Equal("County Family Support", orgEl!.Element(NsNiemCore + "OrganizationName")?.Value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PRO-INI-001 — Probate Conservatorship with CONTE (conservatee) role code.
    // Baseline: @.../Probate/Case Initiation/New Case (Conservatorship) Sample.xml
    // Wire evidence (line 36):
    //   <p1:CaseParticipantRoleCode>CONTE</p1:CaseParticipantRoleCode>
    //
    // CONTE = "Conservatee" — the person whose conservatorship is being established. This is
    // a Probate-specific role code not used in Civil or Family. Tests that the builder's
    // role-code handling is fully string-value-agnostic and doesn't special-case known codes.
    // Defensive regression guard — a future change that accidentally restricts role codes to
    // an enum would fail this test.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PRO_INI_001_BuilderEmits_ConteRoleCode_ForConservateeParty()
    {
        var model = NewInitModel();
        model.CaseCategoryCode = "531110";
        model.CaseTypeCode = "511110";
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto
            {
                Side = "filing",
                PartyType = "PET",
                FirstName = "Jason",
                LastName = "Stabell",
            },
            new PartyEntryDto
            {
                Side = "opposing",
                PartyType = "CONTE",  // Probate-specific — builder must not reject non-standard codes.
                FirstName = "Barbara",
                LastName = "Stabell",
            },
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model, TestConfig);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        var conservatee = FindParticipantById(doc, "filedAsTo0");
        Assert.NotNull(conservatee);
        Assert.Equal("CONTE", conservatee!.Element(NsCommonTypes + "CaseParticipantRoleCode")?.Value);

        // Entity present, name populated — no data loss on non-standard role code.
        var person = conservatee.Element(NsNiemCore + "EntityPerson");
        Assert.NotNull(person);
        Assert.Equal("Barbara", person!.Descendants(NsNiemCore + "PersonGivenName").FirstOrDefault()?.Value);
        Assert.Equal("Stabell", person.Descendants(NsNiemCore + "PersonSurName").FirstOrDefault()?.Value);
    }
}
