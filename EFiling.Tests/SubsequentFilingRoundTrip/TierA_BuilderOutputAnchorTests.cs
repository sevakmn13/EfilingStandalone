using System.Text.Json;
using System.Xml.Linq;
using EFiling.Core.Models;
using EFiling.Nop.Controllers;
using EFiling.Nop.Models;
using EFiling.Providers.JTI.Builders;
using EFiling.Providers.JTI.Soap;

namespace EFiling.Tests.SubsequentFilingRoundTrip;

/// <summary>
/// Tier A — Forward-direction wire-shape anchor tests.
///
/// <para>
/// These tests complement <see cref="TierA_WireShapeAnchorTests"/>. Where that class loads the
/// canonical baseline XML and asserts it contains specific elements (to prevent documentation
/// drift), this class exercises OUR forward pipeline
/// <c>CreateCaseModel → CourtFilingController.BuildSubmissionFromCreateModel → ReviewFilingXmlBuilder</c>
/// and asserts the emitted XML contains the SAME key wire elements.
/// </para>
///
/// <para>
/// Why this class exists: the T-8 audit sessions on 2026-04-22 landed three controller-side
/// fixes (C-1 new-data caseAssignment loop, C-2 contact classType + namespace, C-3 tag value
/// semantics). Each fix was individually regression-gated with a controller unit test. These
/// forward-direction tests verify the fixes COMPOSE correctly to produce realistic baseline
/// wire shapes — the empirical check that the unit tests alone can't provide.
/// </para>
///
/// <para>
/// This is forward-direction only (build path). Byte-level round-trip testing (parse → rebuild
/// → diff) remains deferred to T-2 Pass 2 per <see cref="TierA_RoundTripTests"/>'s skip-reason,
/// which requires a parser that doesn't exist yet.
/// </para>
/// </summary>
public class TierA_BuilderOutputAnchorTests
{
    private static readonly XNamespace NsDocMeta = SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData;
    private static readonly XNamespace NsDocValue = SoapEnvelopeBuilder.NsJtiDocumentValue;
    private static readonly XNamespace NsContactValue = SoapEnvelopeBuilder.NsJtiContactValue;
    private static readonly XNamespace NsCaseAssign = SoapEnvelopeBuilder.NsJtiCaseAssignmentType;
    private static readonly XNamespace NsNiemCore = SoapEnvelopeBuilder.NsNiemCore;
    // ECF CommonTypes-4.0 namespace — Audit H-2 wire contract for EntityPerson /
    // EntityOrganization inside caseAssignmentValue (and caseParticipantValue).
    private static readonly XNamespace NsCommonTypes = SoapEnvelopeBuilder.NsCommonTypes;

    /// <summary>
    /// Realistic TestConfig matching the baseline "madera" samples. The URLs don't need to
    /// resolve; they're only serialized into the envelope structure which we don't assert on
    /// in these tests. Mirrors the one in <see cref="ReviewFilingXmlBuilderTests"/>.
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
    /// Minimal subsequent filing CreateCaseModel — adds <c>IsSubsequentFiling</c>, a case docket,
    /// and a lead document. Tests extend this with scenario-specific metadata JSON.
    /// </summary>
    private static CreateCaseModel NewSubsequentModel()
    {
        var model = new CreateCaseModel
        {
            CourtId = "madera",
            IsSubsequentFiling = true,
            CaseDocketId = "24CV00123",
            CaseTrackingId = "99999",
            ComplaintId = "1",
            CaseTypeCode = "CV",
            CaseCategoryCode = "3701",
        };
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto
            {
                Role = "lead",
                DocumentCode = "401011",
                BlobUrl = "https://files.example.com/answer.pdf"
            }
        });
        return model;
    }

    /// <summary>
    /// Locates the single <c>documentFilingMetaDataItem</c> whose <c>docValueMetaDataItem</c>
    /// matches the requested <paramref name="code"/> + <paramref name="classType"/>
    /// + <paramref name="valueRestriction"/>. Returns null if not found; tests usually
    /// <c>Assert.NotNull</c> immediately after to produce a readable failure.
    /// </summary>
    private static XElement? FindMetadataItem(
        XDocument doc, string code, string classType, string valueRestriction)
    {
        return doc
            .Descendants(NsDocMeta + "documentFilingMetaDataItem")
            .FirstOrDefault(item =>
            {
                var desc = item.Element(NsDocMeta + "docValueMetaDataItem");
                if (desc is null) return false;
                var itemCode = desc.Element(NsDocValue + "code")?.Value?.Trim();
                var itemClass = desc.Element(NsDocValue + "classType")?.Value?.Trim();
                var itemRestriction = desc.Element(NsDocValue + "valueRestriction")?.Value?.Trim();
                return itemCode == code
                    && string.Equals(itemClass, classType, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(itemRestriction, valueRestriction, StringComparison.OrdinalIgnoreCase);
            });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-SUB-002 — Gov Entity Exempt party (exercises audit C-3a)
    // Wire evidence: existing-data caseParticipant with FEE_EXEMPTION tag carrying
    // GOVT_ENTITY enum as tagValue. Pre-fix, the tag was silently dropped; post-fix,
    // the tag round-trips through DTO → FilingMetadataValue → XML.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_SUB_002_BuilderEmits_FeeExemption_GovtEntity_Tag_OnExistingCaseParticipant()
    {
        var model = NewSubsequentModel();
        model.MetadataJson = JsonSerializer.Serialize(new[]
        {
            new MetadataEntryDto
            {
                Code = "FILING_PARTY",
                ClassType = "caseParticipant",
                SubType = "filedBy",
                Values = new List<MetadataValueDto>
                {
                    new()
                    {
                        Id = "1493955",
                        IsNew = false,
                        Tags = new List<string> { "FEE_EXEMPTION" },
                        FeeExemptionType = "GOVT_ENTITY",
                    }
                }
            }
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        var item = FindMetadataItem(doc, "FILING_PARTY", "caseParticipant", "existing-data");
        Assert.NotNull(item);

        var idRef = item!.Element(NsDocMeta + "idReferences");
        Assert.NotNull(idRef);
        Assert.Equal("1493955", idRef!.Element(NsDocMeta + "id")?.Value);

        // The FEE_EXEMPTION tag must be emitted as a child of <idReferences> with the
        // GOVT_ENTITY string-enum value. Pre-audit-C-3a, this tag didn't exist in the output
        // (controller silently dropped meta.Values[*].Tags). Pre-audit-C-3c, the tagValue
        // would have been the literal "true" instead of the enum token.
        var tag = idRef.Elements(NsDocMeta + "additionalInfoTags")
            .FirstOrDefault(t => t.Element(NsDocMeta + "tagType")?.Value == "FEE_EXEMPTION");
        Assert.NotNull(tag);
        Assert.Equal("GOVT_ENTITY", tag!.Element(NsDocMeta + "tagValue")?.Value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-SUB-003 — Fee Waiver request with companion contact (exercises C-2 Bug A,
    // C-2 Bug B, AND C-3a in a single end-to-end scenario).
    // Wire evidence: existing-data caseParticipant with FEE_EXEMPTION/FEE_WAIVER tag +
    // a companion contact classType metadata item (FILING_PARTY_ADDRESS) with the
    // party's current mailing address. The contactValue wrapper stays in DfMeta
    // namespace but children live in the ContactValue extension namespace.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_SUB_003_BuilderEmits_FeeWaiver_Tag_AndContactValue_Together()
    {
        var model = NewSubsequentModel();
        model.MetadataJson = JsonSerializer.Serialize(new object[]
        {
            new MetadataEntryDto
            {
                Code = "FILING_PARTY",
                ClassType = "caseParticipant",
                SubType = "filedBy",
                Values = new List<MetadataValueDto>
                {
                    new()
                    {
                        Id = "1493521",
                        IsNew = false,
                        Tags = new List<string> { "FEE_EXEMPTION" },
                        FeeExemptionType = "FEE_WAIVER",
                    }
                }
            },
            new MetadataEntryDto
            {
                Code = "FILING_PARTY_ADDRESS",
                ClassType = "contact",
                Values = new List<MetadataValueDto>
                {
                    new()
                    {
                        IsNew = true,
                        Address1 = "12223 Davis St.",
                        City = "Sacramento",
                        State = "CA",
                        Zip = "95818",
                        Email = "filer@example.com",
                    }
                }
            }
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        // Assertion 1 — FEE_WAIVER tag on existing-data party (audit C-3a + C-3c post-fix).
        var partyItem = FindMetadataItem(doc, "FILING_PARTY", "caseParticipant", "existing-data");
        Assert.NotNull(partyItem);
        var partyTag = partyItem!.Element(NsDocMeta + "idReferences")!
            .Elements(NsDocMeta + "additionalInfoTags")
            .FirstOrDefault(t => t.Element(NsDocMeta + "tagType")?.Value == "FEE_EXEMPTION");
        Assert.NotNull(partyTag);
        Assert.Equal("FEE_WAIVER", partyTag!.Element(NsDocMeta + "tagValue")?.Value);

        // Assertion 2 — contact classType metadata item emitted at all (audit C-2 Bug A
        // post-fix; pre-fix the controller had no "contact" classType branch and dropped it).
        var contactItem = FindMetadataItem(doc, "FILING_PARTY_ADDRESS", "contact", null!)
            ?? doc.Descendants(NsDocMeta + "documentFilingMetaDataItem")
                .FirstOrDefault(i =>
                    i.Element(NsDocMeta + "docValueMetaDataItem")?.Element(NsDocValue + "code")?.Value == "FILING_PARTY_ADDRESS"
                    && i.Element(NsDocMeta + "docValueMetaDataItem")?.Element(NsDocValue + "classType")?.Value == "contact");
        Assert.NotNull(contactItem);

        var contactValue = contactItem!.Element(NsDocMeta + "contactValue");
        Assert.NotNull(contactValue);

        // Assertion 3 — contactValue children are in the ContactValue extension namespace
        // (audit C-2 Bug B post-fix; pre-fix they were mis-namespaced to DocumentFilingMetaData).
        var address1 = contactValue!.Element(NsContactValue + "address1");
        var city = contactValue.Element(NsContactValue + "city");
        var email = contactValue.Element(NsContactValue + "email");
        Assert.NotNull(address1);
        Assert.Equal("12223 Davis St.", address1!.Value);
        Assert.NotNull(city);
        Assert.Equal("Sacramento", city!.Value);
        Assert.NotNull(email);
        Assert.Equal("filer@example.com", email!.Value);

        // Defensive check — no children of contactValue should exist in the DfMeta namespace,
        // which was the pre-C-2-Bug-B regression mode.
        var dfMetaChildren = contactValue.Elements(NsDocMeta + "address1")
            .Concat(contactValue.Elements(NsDocMeta + "city"))
            .Concat(contactValue.Elements(NsDocMeta + "email"))
            .ToList();
        Assert.Empty(dfMetaChildren);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-SUB-005 — New representation (exercises audit C-1)
    // Wire evidence: new-data caseAssignment with inline caseAssignmentValue containing
    // EntityPerson (name + bar) + EntityOrganization (firm) + AssignmentRole=ATT.
    // Pre-fix, the entire new-data input was silently dropped at the controller's
    // caseassignment/attorney branch (no new-data loop existed).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_SUB_005_BuilderEmits_CaseAssignmentValue_ForNewAttorney()
    {
        var model = NewSubsequentModel();
        model.MetadataJson = JsonSerializer.Serialize(new[]
        {
            new MetadataEntryDto
            {
                Code = "NEW_ATTORNEY",
                ClassType = "caseAssignment",
                Values = new List<MetadataValueDto>
                {
                    new()
                    {
                        IsNew = true,
                        FirstName = "David",
                        MiddleName = "V.",
                        LastName = "Skinner",
                        BarNumber = "123426",
                        FirmName = "Skinner Law",
                    }
                }
            }
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        var item = FindMetadataItem(doc, "NEW_ATTORNEY", "caseAssignment", "new-data");
        Assert.NotNull(item);

        var caValue = item!.Element(NsDocMeta + "caseAssignmentValue");
        Assert.NotNull(caValue);

        // Audit H-2 fix: EntityPerson / EntityOrganization inside
        // caseAssignmentValue live in ECF CommonTypes-4.0 namespace (not niem-core).
        // Children (PersonName, PersonOtherIdentification, OrganizationName) remain in
        // niem-core. See ReviewFilingXmlBuilder.cs:821-846 and catalog 2026-04-22 H-2.
        var person = caValue!.Element(NsCommonTypes + "EntityPerson");
        Assert.NotNull(person);
        var name = person!.Element(NsNiemCore + "PersonName")!;
        Assert.Equal("David", name.Element(NsNiemCore + "PersonGivenName")?.Value);
        Assert.Equal("V.", name.Element(NsNiemCore + "PersonMiddleName")?.Value);
        Assert.Equal("Skinner", name.Element(NsNiemCore + "PersonSurName")?.Value);

        var barId = person.Element(NsNiemCore + "PersonOtherIdentification");
        Assert.NotNull(barId);
        Assert.Equal("123426", barId!.Element(NsNiemCore + "IdentificationID")?.Value);
        // BAR is the literal JTI expects for attorney bar numbers — wire evidence (catalog §3.3).
        Assert.Equal("BAR", barId.Element(NsNiemCore + "IdentificationCategoryText")?.Value);

        // EntityOrganization with firm name — also ECF per H-2 fix.
        var org = caValue.Element(NsCommonTypes + "EntityOrganization");
        Assert.NotNull(org);
        Assert.Equal("Skinner Law", org!.Element(NsNiemCore + "OrganizationName")?.Value);

        // AssignmentRole must be present in the CaseAssignmentType namespace (not DfMeta).
        // Defaulted to "ATT" per catalog §3.3 observation — only role value in baseline samples.
        var role = caValue.Element(NsCaseAssign + "AssignmentRole");
        Assert.NotNull(role);
        Assert.Equal("ATT", role!.Value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-SUB-004 — EFSP_FIRST_APPEARANCE_PAID self-certification flag (exercises C-3a + C-3c
    // for a tag type not covered by the other tests).
    // Wire evidence (verified): existing-data caseParticipant with
    // <tagType>EFSP_FIRST_APPEARANCE_PAID</tagType><tagValue>1</tagValue> — digit-boolean
    // per catalog §4.3, same semantic as E_SERVICE.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_SUB_004_BuilderEmits_EfspFirstAppearancePaid_AsDigitBoolean()
    {
        var model = NewSubsequentModel();
        model.MetadataJson = JsonSerializer.Serialize(new[]
        {
            new MetadataEntryDto
            {
                Code = "FILING_PARTY",
                ClassType = "caseParticipant",
                SubType = "filedBy",
                Values = new List<MetadataValueDto>
                {
                    new()
                    {
                        Id = "1490099",
                        IsNew = false,
                        Tags = new List<string> { "EFSP_FIRST_APPEARANCE_PAID" },
                    }
                }
            }
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        var item = FindMetadataItem(doc, "FILING_PARTY", "caseParticipant", "existing-data");
        Assert.NotNull(item);
        var tag = item!.Element(NsDocMeta + "idReferences")!
            .Elements(NsDocMeta + "additionalInfoTags")
            .FirstOrDefault(t => t.Element(NsDocMeta + "tagType")?.Value == "EFSP_FIRST_APPEARANCE_PAID");
        Assert.NotNull(tag);
        // Digit-boolean per §4.3; pre-fix this would have been "true" (literal bool).
        Assert.Equal("1", tag!.Element(NsDocMeta + "tagValue")?.Value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // FAM-SUB-004 — Response with custody/visitation flag (exercises combined-tags case).
    // Wire evidence (verified): single <idReferences> contains TWO <additionalInfoTags>
    // siblings — one with tagType=E_SERVICE / tagValue=1, another with tagType=EFSP_EMAIL
    // / tagValue=<email>. This is the most structurally interesting tag pattern in the
    // baseline — proves our helper correctly emits multiple distinct-semantic tags on the
    // same party reference (digit-boolean + free-text combined).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FAM_SUB_004_BuilderEmits_EService_AndEfspEmail_OnSameParty()
    {
        var model = NewSubsequentModel();
        model.CaseDocketId = "24FL00456";
        model.CaseCategoryCode = "3702";
        model.MetadataJson = JsonSerializer.Serialize(new[]
        {
            new MetadataEntryDto
            {
                Code = "FILING_PARTY",
                ClassType = "caseParticipant",
                SubType = "filedBy",
                Values = new List<MetadataValueDto>
                {
                    new()
                    {
                        Id = "1494948",
                        IsNew = false,
                        Tags = new List<string> { "E_SERVICE", "EFSP_EMAIL" },
                        Email = "responder@example.com",
                    }
                }
            }
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        var item = FindMetadataItem(doc, "FILING_PARTY", "caseParticipant", "existing-data");
        Assert.NotNull(item);
        var idRef = item!.Element(NsDocMeta + "idReferences");
        Assert.NotNull(idRef);

        var allTags = idRef!.Elements(NsDocMeta + "additionalInfoTags").ToList();

        // Both tags must appear as distinct siblings under the same <idReferences>.
        Assert.Equal(2, allTags.Count);

        var eserviceTag = allTags.FirstOrDefault(t => t.Element(NsDocMeta + "tagType")?.Value == "E_SERVICE");
        Assert.NotNull(eserviceTag);
        Assert.Equal("1", eserviceTag!.Element(NsDocMeta + "tagValue")?.Value);

        var emailTag = allTags.FirstOrDefault(t => t.Element(NsDocMeta + "tagType")?.Value == "EFSP_EMAIL");
        Assert.NotNull(emailTag);
        // Free-text per §4.4 — tagValue is the email address, NOT "1" or "true".
        Assert.Equal("responder@example.com", emailTag!.Element(NsDocMeta + "tagValue")?.Value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // FAM-SUB-003 — codeList classType (motion type). Exercises the simple-value branch
    // of the metadata classType switch. Wire evidence:
    //     <ns8:code>MOTION_OSC_DETAIL</ns8:code>
    //     <ns8:classType>codeList</ns8:classType>
    //     <ns8:valueRestriction>existing-data</ns8:valueRestriction>
    //     <ns9:codeValue>263210</ns9:codeValue>
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FAM_SUB_003_BuilderEmits_CodeList_WithCodeValue()
    {
        var model = NewSubsequentModel();
        model.CaseCategoryCode = "3702";
        model.MetadataJson = JsonSerializer.Serialize(new[]
        {
            new MetadataEntryDto
            {
                Code = "MOTION_OSC_DETAIL",
                ClassType = "codeList",
                ValueRestriction = "existing-data",
                Values = new List<MetadataValueDto>
                {
                    new() { Value = "263210" }
                }
            }
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        var item = FindMetadataItem(doc, "MOTION_OSC_DETAIL", "codeList", "existing-data");
        Assert.NotNull(item);
        Assert.Equal("263210", item!.Element(NsDocMeta + "codeValue")?.Value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // FAM-SUB-004 — boolean classType (custody/visitation flag). Exercises the
    // boolean-value branch. Wire evidence:
    //     <ns8:code>CUSTODY_ISSUE</ns8:code>
    //     <ns8:classType>boolean</ns8:classType>
    //     <ns9:booleanValue>true</ns9:booleanValue>
    // Note: booleanValue uses XML-literal "true"/"false" form (contrast with tag-level
    // E_SERVICE which uses digit-boolean "0"/"1" per §4.0 "two conventions coexist").
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FAM_SUB_004_BuilderEmits_Boolean_AsXmlLiteralForm()
    {
        var model = NewSubsequentModel();
        model.CaseCategoryCode = "3702";
        model.MetadataJson = JsonSerializer.Serialize(new[]
        {
            new MetadataEntryDto
            {
                Code = "CUSTODY_ISSUE",
                ClassType = "boolean",
                Values = new List<MetadataValueDto>
                {
                    new() { Value = true }
                }
            }
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        var item = FindMetadataItem(doc, "CUSTODY_ISSUE", "boolean", null!)
            ?? doc.Descendants(NsDocMeta + "documentFilingMetaDataItem")
                .FirstOrDefault(i =>
                    i.Element(NsDocMeta + "docValueMetaDataItem")?.Element(NsDocValue + "code")?.Value == "CUSTODY_ISSUE"
                    && i.Element(NsDocMeta + "docValueMetaDataItem")?.Element(NsDocValue + "classType")?.Value == "boolean");
        Assert.NotNull(item);
        Assert.Equal("true", item!.Element(NsDocMeta + "booleanValue")?.Value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CIV-SUB-017 — date classType (Proof of Service SERVICE_DATE). Exercises the
    // date-value branch. Wire evidence (both CIV-SUB-016 AND CIV-SUB-017):
    //     <ns8:code>SERVICE_DATE</ns8:code>
    //     <ns8:classType>date</ns8:classType>
    //     <ns9:dateValue>
    //       <ns1:DateTime>2020-12-31T00:00:00-08:00</ns1:DateTime>  ← DateTime, not Date
    //     </ns9:dateValue>
    //
    // ⚠️ AUDIT D-1 — EXPECTED TO FAIL BEFORE FIX.
    // The builder currently emits <nc:Date>yyyy-MM-dd</nc:Date> inside <dateValue>, but
    // baseline wire consistently uses <nc:DateTime>yyyy-MM-ddTHH:mm:ss{offset}</nc:DateTime>.
    // This test asserts the wire-correct form; it will fail (red) until the builder is
    // updated to emit <nc:DateTime>. Running the test before the fix produces a clear
    // red signal; running after the fix turns green — the standard red-then-green TDD
    // pattern the catalog §4.0 "evidence discipline" demands.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_SUB_017_BuilderEmits_Date_AsDateTimeElement_AuditD1()
    {
        var model = NewSubsequentModel();
        model.MetadataJson = JsonSerializer.Serialize(new[]
        {
            new MetadataEntryDto
            {
                Code = "SERVICE_DATE",
                ClassType = "date",
                Values = new List<MetadataValueDto>
                {
                    new() { Value = "2020-12-31" }
                }
            }
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        var item = FindMetadataItem(doc, "SERVICE_DATE", "date", null!)
            ?? doc.Descendants(NsDocMeta + "documentFilingMetaDataItem")
                .FirstOrDefault(i =>
                    i.Element(NsDocMeta + "docValueMetaDataItem")?.Element(NsDocValue + "code")?.Value == "SERVICE_DATE"
                    && i.Element(NsDocMeta + "docValueMetaDataItem")?.Element(NsDocValue + "classType")?.Value == "date");
        Assert.NotNull(item);

        var dateValueEl = item!.Element(NsDocMeta + "dateValue");
        Assert.NotNull(dateValueEl);

        // ⚠️ Audit D-1 assertion — wire form is <nc:DateTime>, NOT <nc:Date>.
        var dateTimeChild = dateValueEl!.Element(NsNiemCore + "DateTime");
        Assert.True(dateTimeChild is not null,
            "Audit D-1 (see change log entry 2026-04-22 'Audits D-1 + D-2 fixed'): builder "
            + "emits <nc:Date> inside <dateValue>, but baseline wire evidence (CIV-SUB-016, "
            + "CIV-SUB-017 Proof of Service SERVICE_DATE) consistently uses <nc:DateTime>. "
            + "Fix the builder's case \"date\": branch to emit <nc:DateTime> with "
            + "yyyy-MM-ddTHH:mm:ss format (time component at 00:00:00 for date-only semantics).");

        // Assert the ISO 8601 content — the date portion must be preserved, and a time
        // component must be appended (even if 00:00:00).
        Assert.StartsWith("2020-12-31T", dateTimeChild!.Value);

        // Defensive: verify the OLD <nc:Date> form is NOT present (regression guard — if
        // someone later reverts or adds a parallel Date child, this catches it).
        var dateChild = dateValueEl.Element(NsNiemCore + "Date");
        Assert.Null(dateChild);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Composed scenario — combines C-1 + C-3a in one submission. Catalog §2.6.1 shows
    // CIV-SUB-005 and CIV-SUB-013 both mix existing caseparticipant-with-E_SERVICE and
    // new-data caseAssignment in one filing. This test empirically verifies our code
    // produces both correctly in the same output XML.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CIV_SUB_013_Style_BuilderEmits_BothExistingPartyTag_AndNewAttorney()
    {
        var model = NewSubsequentModel();
        model.MetadataJson = JsonSerializer.Serialize(new[]
        {
            new MetadataEntryDto
            {
                Code = "FILING_PARTY",
                ClassType = "caseParticipant",
                SubType = "filedBy",
                Values = new List<MetadataValueDto>
                {
                    new()
                    {
                        Id = "P42",
                        IsNew = false,
                        Tags = new List<string> { "E_SERVICE" },
                    }
                }
            },
            new MetadataEntryDto
            {
                Code = "NEW_ATTORNEY",
                ClassType = "caseAssignment",
                Values = new List<MetadataValueDto>
                {
                    new()
                    {
                        IsNew = true,
                        FirstName = "Jane",
                        LastName = "Counsel",
                        BarNumber = "999999",
                        FirmName = "Counsel & Co.",
                    }
                }
            }
        });

        var submission = CourtFilingController.BuildSubmissionFromCreateModel(model);
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(submission, TestConfig);
        var doc = XDocument.Parse(xml);

        // E_SERVICE tag present on the existing-data caseParticipant with digit-boolean "1"
        // (audit C-3a + C-3c post-fix).
        var partyItem = FindMetadataItem(doc, "FILING_PARTY", "caseParticipant", "existing-data");
        Assert.NotNull(partyItem);
        var eserviceTag = partyItem!.Element(NsDocMeta + "idReferences")!
            .Elements(NsDocMeta + "additionalInfoTags")
            .FirstOrDefault(t => t.Element(NsDocMeta + "tagType")?.Value == "E_SERVICE");
        Assert.NotNull(eserviceTag);
        Assert.Equal("1", eserviceTag!.Element(NsDocMeta + "tagValue")?.Value);

        // caseAssignmentValue for the new attorney emitted correctly (audit C-1 post-fix).
        // Audit H-2 fix: EntityOrganization is in ECF (NsCommonTypes), not NC.
        // OrganizationName child stays in NC. IdentificationID (BAR) stays in NC per H-2.
        var caItem = FindMetadataItem(doc, "NEW_ATTORNEY", "caseAssignment", "new-data");
        Assert.NotNull(caItem);
        var caValue = caItem!.Element(NsDocMeta + "caseAssignmentValue");
        Assert.NotNull(caValue);
        Assert.Equal("999999",
            caValue!.Descendants(NsNiemCore + "IdentificationID").FirstOrDefault()?.Value);
        Assert.Equal("Counsel & Co.",
            caValue.Element(NsCommonTypes + "EntityOrganization")?
                .Element(NsNiemCore + "OrganizationName")?.Value);
    }
}
