using System.Xml.Linq;

namespace EFiling.Tests.SubsequentFilingRoundTrip;

/// <summary>
/// Tier A — wire-shape anchor tests for T-1.A V0 documentation.
///
/// <para>
/// Every wire-shape snippet quoted in <c>JTI_SUBSEQUENT_FILING_CATALOG.md</c> §3.2
/// (caseParticipant) and §3.3 (caseAssignment) is cited by scenario ID and line range. If
/// someone modifies the source sample XMLs, these tests force a deliberate update to the
/// catalog rather than silently drifting the documented wire shape away from ground truth.
/// </para>
///
/// <para>
/// These assertions are not structural-equality checks; they are "anchor" checks that confirm
/// the specific elements the catalog documents are present at the expected location. Byte-exact
/// round-trip testing is a separate discipline (T-2 Pass 2).
/// </para>
/// </summary>
public class TierA_WireShapeAnchorTests
{
    private static XNamespace NsDocValue => "urn:com.journaltech:ecourt:ecf:extension:DocumentValue";
    private static XNamespace NsDocMeta  => "urn:com.journaltech:ecourt:ecf:extension:DocumentFilingMetaData";

    /// <summary>
    /// Catalog §3.2 anchor — CIV-SUB-015 Notice of Appeal contains a caseParticipant + existing-data
    /// documentFilingMetaDataItem with subType=filed-by and idReferences child.
    /// </summary>
    [Fact]
    public void CIV_SUB_015_Contains_CaseParticipant_ExistingData_FilingParty()
    {
        var scenario = CanonicalScenarios.GetById("CIV-SUB-015");
        var doc = SampleLoader.LoadXDocument(scenario);

        var docValueMetaItem = doc
            .Descendants(NsDocValue + "classType")
            .Where(el => el.Value.Trim() == "caseParticipant")
            .Select(el => el.Parent)
            .FirstOrDefault(parent =>
                parent?.Element(NsDocValue + "subType")?.Value.Trim() == "filed-by"
                && parent.Element(NsDocValue + "valueRestriction")?.Value.Trim() == "existing-data");

        Assert.NotNull(docValueMetaItem);

        var code = docValueMetaItem!.Element(NsDocValue + "code")?.Value.Trim();
        Assert.Equal("FILING_PARTY", code);

        // The wrapping documentFilingMetaDataItem should have an idReferences sibling.
        var filingMetaDataItem = docValueMetaItem.Parent;
        Assert.NotNull(filingMetaDataItem);
        var idRefs = filingMetaDataItem!.Element(NsDocMeta + "idReferences");
        Assert.NotNull(idRefs);
        var id = idRefs!.Element(NsDocMeta + "id")?.Value.Trim();
        Assert.Equal("1493958", id);
    }

    /// <summary>
    /// Catalog §3.2 anchor — CIV-SUB-001 Amended Complaint contains a caseParticipant + new-data
    /// (NEW_PLAINTIFF) with inline caseParticipantValue containing EntityPerson + AKA sibling +
    /// CaseParticipantRoleCode=PLAIN + ContactInformation + ns10:eService extension element.
    /// </summary>
    [Fact]
    public void CIV_SUB_001_Contains_CaseParticipant_NewData_WithAka_AndEserviceExtension()
    {
        var scenario = CanonicalScenarios.GetById("CIV-SUB-001");
        var doc = SampleLoader.LoadXDocument(scenario);

        var docValueMetaItem = doc
            .Descendants(NsDocValue + "classType")
            .Where(el => el.Value.Trim() == "caseParticipant")
            .Select(el => el.Parent)
            .FirstOrDefault(parent =>
                parent?.Element(NsDocValue + "valueRestriction")?.Value.Trim() == "new-data"
                && parent.Element(NsDocValue + "code")?.Value.Trim() == "NEW_PLAINTIFF");

        Assert.NotNull(docValueMetaItem);

        // Assert the sibling caseParticipantValue exists.
        var filingMetaDataItem = docValueMetaItem!.Parent;
        Assert.NotNull(filingMetaDataItem);
        var caseParticipantValue = filingMetaDataItem!.Element(NsDocMeta + "caseParticipantValue");
        Assert.NotNull(caseParticipantValue);

        // Two EntityPerson children expected: primary name + AKA (with AFS identifier).
        var entityPersons = caseParticipantValue!.Elements().Where(e => e.Name.LocalName == "EntityPerson").ToList();
        Assert.Equal(2, entityPersons.Count);

        // Primary has GivenName=Steven, SurName=Allen.
        var primary = entityPersons[0];
        var given = primary.Descendants().First(e => e.Name.LocalName == "PersonGivenName").Value.Trim();
        var sur = primary.Descendants().First(e => e.Name.LocalName == "PersonSurName").Value.Trim();
        Assert.Equal("Steven", given);
        Assert.Equal("Allen", sur);

        // AKA has PersonOtherIdentification with IdentificationCategoryText=AFS.
        var aka = entityPersons[1];
        var akaCategory = aka
            .Descendants()
            .First(e => e.Name.LocalName == "IdentificationCategoryText")
            .Value.Trim();
        Assert.Equal("AFS", akaCategory);

        // CaseParticipantRoleCode child = PLAIN (plaintiff).
        var roleCode = caseParticipantValue
            .Descendants()
            .First(e => e.Name.LocalName == "CaseParticipantRoleCode")
            .Value.Trim();
        Assert.Equal("PLAIN", roleCode);

        // ns10:eService extension element must exist as a direct child of caseParticipantValue
        // (the new-data encoding of E_SERVICE consent — distinct from the existing-data tag form).
        // NOTE: ns10 is CaseParticipantExt namespace, but the eService element lives in that ns as well
        // per the sample. Match on local name to be namespace-robust.
        var eServiceExt = caseParticipantValue
            .Elements()
            .FirstOrDefault(e => e.Name.LocalName == "eService");
        Assert.NotNull(eServiceExt);
        Assert.Equal("false", eServiceExt!.Value.Trim());
    }

    /// <summary>
    /// Catalog §3.2 anchor — CIV-SUB-001 Amended Complaint has an E_SERVICE additionalInfoTag
    /// attached INSIDE the idReferences of the existing-data FILING_PARTY caseParticipant,
    /// with tagValue=0 (boolean-as-digit, opt-out). This is the existing-data eService encoding.
    /// </summary>
    [Fact]
    public void CIV_SUB_001_Contains_EService_Tag_AsAdditionalInfoTag_InsideIdReferences()
    {
        var scenario = CanonicalScenarios.GetById("CIV-SUB-001");
        var doc = SampleLoader.LoadXDocument(scenario);

        var existingDataFilingParty = doc
            .Descendants(NsDocValue + "classType")
            .Where(el => el.Value.Trim() == "caseParticipant")
            .Select(el => el.Parent)
            .FirstOrDefault(parent =>
                parent?.Element(NsDocValue + "valueRestriction")?.Value.Trim() == "existing-data"
                && parent.Element(NsDocValue + "subType")?.Value.Trim() == "filed-by");

        Assert.NotNull(existingDataFilingParty);

        var filingMetaDataItem = existingDataFilingParty!.Parent;
        Assert.NotNull(filingMetaDataItem);

        var idRefs = filingMetaDataItem!.Element(NsDocMeta + "idReferences");
        Assert.NotNull(idRefs);

        var additionalInfoTags = idRefs!.Element(NsDocMeta + "additionalInfoTags");
        Assert.NotNull(additionalInfoTags);

        var tagType = additionalInfoTags!.Element(NsDocMeta + "tagType")?.Value.Trim();
        var tagValue = additionalInfoTags.Element(NsDocMeta + "tagValue")?.Value.Trim();
        Assert.Equal("E_SERVICE", tagType);
        Assert.Equal("0", tagValue);
    }

    /// <summary>
    /// Catalog §3.3 anchor — CIV-SUB-005 Any first paper document with new representation contains
    /// a caseAssignment + new-data (NEW_ATTORNEY) with inline caseAssignmentValue containing
    /// EntityPerson + PersonOtherIdentification (bar number + BAR category) + EntityOrganization
    /// + ContactInformation + ns12:AssignmentRole=ATT.
    /// </summary>
    [Fact]
    public void CIV_SUB_005_Contains_CaseAssignment_NewData_Attorney_WithBar_Firm_Role()
    {
        var scenario = CanonicalScenarios.GetById("CIV-SUB-005");
        var doc = SampleLoader.LoadXDocument(scenario);

        var docValueMetaItem = doc
            .Descendants(NsDocValue + "classType")
            .Where(el => el.Value.Trim() == "caseAssignment")
            .Select(el => el.Parent)
            .FirstOrDefault(parent =>
                parent?.Element(NsDocValue + "valueRestriction")?.Value.Trim() == "new-data"
                && parent.Element(NsDocValue + "code")?.Value.Trim() == "NEW_ATTORNEY");

        Assert.NotNull(docValueMetaItem);

        var filingMetaDataItem = docValueMetaItem!.Parent;
        Assert.NotNull(filingMetaDataItem);

        var caseAssignmentValue = filingMetaDataItem!.Element(NsDocMeta + "caseAssignmentValue");
        Assert.NotNull(caseAssignmentValue);

        // EntityPerson with PersonOtherIdentification containing IdentificationID + IdentificationCategoryText=BAR.
        var entityPerson = caseAssignmentValue!.Elements().First(e => e.Name.LocalName == "EntityPerson");
        var otherId = entityPerson.Elements().First(e => e.Name.LocalName == "PersonOtherIdentification");
        var idValue = otherId.Elements().First(e => e.Name.LocalName == "IdentificationID").Value.Trim();
        var idCategory = otherId.Elements().First(e => e.Name.LocalName == "IdentificationCategoryText").Value.Trim();
        Assert.Equal("123426", idValue);
        Assert.Equal("BAR", idCategory);

        // EntityOrganization with OrganizationName.
        var entityOrg = caseAssignmentValue.Elements().First(e => e.Name.LocalName == "EntityOrganization");
        var orgName = entityOrg.Elements().First(e => e.Name.LocalName == "OrganizationName").Value.Trim();
        Assert.Equal("Skinner Law", orgName);

        // AssignmentRole=ATT (any namespace — JTI ext ns12 in sample, but match by local name).
        var assignmentRole = caseAssignmentValue
            .Elements()
            .First(e => e.Name.LocalName == "AssignmentRole")
            .Value.Trim();
        Assert.Equal("ATT", assignmentRole);
    }

    /// <summary>
    /// Catalog §3.3 anchor — CIV-SUB-001 Amended Complaint contains a caseAssignment + existing-data
    /// (FILING_ATTORNEY) with idReferences pointing to primaryId 875036.
    /// </summary>
    [Fact]
    public void CIV_SUB_001_Contains_CaseAssignment_ExistingData_FilingAttorney()
    {
        var scenario = CanonicalScenarios.GetById("CIV-SUB-001");
        var doc = SampleLoader.LoadXDocument(scenario);

        var docValueMetaItem = doc
            .Descendants(NsDocValue + "classType")
            .Where(el => el.Value.Trim() == "caseAssignment")
            .Select(el => el.Parent)
            .FirstOrDefault(parent =>
                parent?.Element(NsDocValue + "valueRestriction")?.Value.Trim() == "existing-data"
                && parent.Element(NsDocValue + "code")?.Value.Trim() == "FILING_ATTORNEY");

        Assert.NotNull(docValueMetaItem);

        var filingMetaDataItem = docValueMetaItem!.Parent;
        Assert.NotNull(filingMetaDataItem);

        var idRefs = filingMetaDataItem!.Element(NsDocMeta + "idReferences");
        Assert.NotNull(idRefs);
        var id = idRefs!.Element(NsDocMeta + "id")?.Value.Trim();
        Assert.Equal("875036", id);
    }

    // ─── V1 anchors (catalog §3.4 contact, §3.5 codeList, §3.6 date, §3.7 boolean) ────────

    /// <summary>
    /// Catalog §3.4 anchor — CIV-SUB-003 Fee Waiver request contains a contact classType
    /// with NO valueRestriction element and a ContactValue wrapper using flat JTI-extension fields
    /// (address1, city, state, zip, country, email, telephoneType, addressType).
    /// This is the primary regression gate for audit C-2 (silent drop of contact data).
    /// </summary>
    [Fact]
    public void CIV_SUB_003_Contains_Contact_ClassType_WithFlatContactValueFields_NoValueRestriction()
    {
        var scenario = CanonicalScenarios.GetById("CIV-SUB-003");
        var doc = SampleLoader.LoadXDocument(scenario);

        var docValueMetaItem = doc
            .Descendants(NsDocValue + "classType")
            .Where(el => el.Value.Trim() == "contact")
            .Select(el => el.Parent)
            .FirstOrDefault();

        Assert.NotNull(docValueMetaItem);

        // Audit C-2 invariant: contact classType does NOT emit a valueRestriction element.
        // If this ever becomes non-null in CIV-SUB-003, the catalog §3.4 description and our
        // builder-fix guidance both need revisiting.
        var valueRestriction = docValueMetaItem!.Element(NsDocValue + "valueRestriction");
        Assert.Null(valueRestriction);

        var code = docValueMetaItem.Element(NsDocValue + "code")?.Value.Trim();
        Assert.Equal("FILING_PARTY_ADDRESS", code);

        // The sibling contactValue must contain the 8 flat fields in ContactValue namespace.
        var filingMetaDataItem = docValueMetaItem.Parent;
        Assert.NotNull(filingMetaDataItem);
        var contactValue = filingMetaDataItem!.Element(NsDocMeta + "contactValue");
        Assert.NotNull(contactValue);

        XNamespace nsContactValue = "urn:com.journaltech:ecourt:ecf:extension:ContactValue";

        // Assert each expected flat field is present (local names per §3.4 field list).
        var expectedFields = new[] { "address1", "city", "zip", "state", "country", "telephoneType", "email", "addressType" };
        foreach (var fieldName in expectedFields)
        {
            var field = contactValue!.Element(nsContactValue + fieldName);
            Assert.True(field is not null,
                $"§3.4 wire-shape contract: <ns10:{fieldName}> (namespace={nsContactValue}) is missing from contactValue in CIV-SUB-003. "
                + "If the sample changed, update catalog §3.4 field list + ExpectedFields in this test.");
        }

        // Assert the values match catalog §3.4 quoted wire shape.
        Assert.Equal("12223 Davis St.", contactValue!.Element(nsContactValue + "address1")!.Value.Trim());
        Assert.Equal("Sacramento", contactValue.Element(nsContactValue + "city")!.Value.Trim());
        Assert.Equal("95818", contactValue.Element(nsContactValue + "zip")!.Value.Trim());
        Assert.Equal("CA", contactValue.Element(nsContactValue + "state")!.Value.Trim());
        Assert.Equal("US", contactValue.Element(nsContactValue + "country")!.Value.Trim());
        Assert.Equal("BUS", contactValue.Element(nsContactValue + "telephoneType")!.Value.Trim());
        Assert.Equal("BUS", contactValue.Element(nsContactValue + "addressType")!.Value.Trim());
    }

    /// <summary>
    /// Catalog §3.5 anchor — FAM-SUB-003 Motion Type scenario contains a codeList + existing-data
    /// metadata item (code=MOTION_TYPE) with codeValue=263210.
    /// </summary>
    [Fact]
    public void FAM_SUB_003_Contains_CodeList_ExistingData_MotionType()
    {
        var scenario = CanonicalScenarios.GetById("FAM-SUB-003");
        var doc = SampleLoader.LoadXDocument(scenario);

        var docValueMetaItem = doc
            .Descendants(NsDocValue + "classType")
            .Where(el => el.Value.Trim() == "codeList")
            .Select(el => el.Parent)
            .FirstOrDefault(parent =>
                parent?.Element(NsDocValue + "valueRestriction")?.Value.Trim() == "existing-data");

        Assert.NotNull(docValueMetaItem);

        var filingMetaDataItem = docValueMetaItem!.Parent;
        Assert.NotNull(filingMetaDataItem);
        var codeValue = filingMetaDataItem!.Element(NsDocMeta + "codeValue");
        Assert.NotNull(codeValue);
        Assert.Equal("263210", codeValue!.Value.Trim());
    }

    /// <summary>
    /// Catalog §3.5 anchor — FAM-SUB-005 dissolution on DV case contains a codeList + new-data
    /// metadata item with codeValue=211120. Confirms the new-data wire shape is structurally
    /// identical to the existing-data form — only the valueRestriction string differs.
    /// </summary>
    [Fact]
    public void FAM_SUB_005_Contains_CodeList_NewData_Dissolution()
    {
        var scenario = CanonicalScenarios.GetById("FAM-SUB-005");
        var doc = SampleLoader.LoadXDocument(scenario);

        var docValueMetaItem = doc
            .Descendants(NsDocValue + "classType")
            .Where(el => el.Value.Trim() == "codeList")
            .Select(el => el.Parent)
            .FirstOrDefault(parent =>
                parent?.Element(NsDocValue + "valueRestriction")?.Value.Trim() == "new-data");

        Assert.NotNull(docValueMetaItem);

        var filingMetaDataItem = docValueMetaItem!.Parent;
        Assert.NotNull(filingMetaDataItem);
        var codeValue = filingMetaDataItem!.Element(NsDocMeta + "codeValue");
        Assert.NotNull(codeValue);
        Assert.Equal("211120", codeValue!.Value.Trim());
    }

    /// <summary>
    /// Catalog §3.6 anchor — CIV-SUB-016 Proof of Personal Service contains a date classType
    /// with dateValue wrapping a niem-core DateTime element in ISO 8601 form.
    /// </summary>
    [Fact]
    public void CIV_SUB_016_Contains_Date_ClassType_WithIsoDateTime()
    {
        var scenario = CanonicalScenarios.GetById("CIV-SUB-016");
        var doc = SampleLoader.LoadXDocument(scenario);

        var docValueMetaItem = doc
            .Descendants(NsDocValue + "classType")
            .Where(el => el.Value.Trim() == "date")
            .Select(el => el.Parent)
            .FirstOrDefault();

        Assert.NotNull(docValueMetaItem);

        var filingMetaDataItem = docValueMetaItem!.Parent;
        Assert.NotNull(filingMetaDataItem);

        var dateValue = filingMetaDataItem!.Element(NsDocMeta + "dateValue");
        Assert.NotNull(dateValue);

        XNamespace nsNiemCore = "http://niem.gov/niem/niem-core/2.0";
        var dateTime = dateValue!.Element(nsNiemCore + "DateTime");
        Assert.NotNull(dateTime);

        // ISO 8601 with timezone offset per §3.6. Value from the sample is 2021-03-03T00:00:00-08:00.
        Assert.Equal("2021-03-03T00:00:00-08:00", dateTime!.Value.Trim());
    }

    /// <summary>
    /// Catalog §3.7 anchor — CIV-SUB-018 Self-Rep conversion contains a boolean classType with
    /// booleanValue=true (XML literal, NOT "1"). Critical to distinguish from the E_SERVICE tagValue
    /// which uses digit form "0"/"1".
    /// </summary>
    [Fact]
    public void CIV_SUB_018_Contains_Boolean_ClassType_WithXmlLiteralTrue()
    {
        var scenario = CanonicalScenarios.GetById("CIV-SUB-018");
        var doc = SampleLoader.LoadXDocument(scenario);

        var docValueMetaItem = doc
            .Descendants(NsDocValue + "classType")
            .Where(el => el.Value.Trim() == "boolean")
            .Select(el => el.Parent)
            .FirstOrDefault();

        Assert.NotNull(docValueMetaItem);

        var filingMetaDataItem = docValueMetaItem!.Parent;
        Assert.NotNull(filingMetaDataItem);
        var booleanValue = filingMetaDataItem!.Element(NsDocMeta + "booleanValue");
        Assert.NotNull(booleanValue);

        // §3.7 "CRITICAL distinction": boolean classType uses XML literal "true"/"false",
        // NOT "1"/"0" (the latter are used for E_SERVICE tagValue per §3.2 dual-encoding note).
        Assert.Equal("true", booleanValue!.Value.Trim());
        Assert.NotEqual("1", booleanValue.Value.Trim());
    }

    // ─── T-1.B tag registry anchors (catalog §4 — E_SERVICE digit-boolean, EFSP_FIRST_APPEARANCE_PAID, EFSP_EMAIL + E_SERVICE pairing) ──

    /// <summary>
    /// Catalog §4.1 anchor — FAM-SUB-004 emits E_SERVICE with tagValue "1" (opt-in, digit form).
    /// Confirms the digit-boolean value semantic with the opt-in polarity. CIV-SUB-001 covers the
    /// "0" opt-out polarity via CIV_SUB_001_Contains_EService_Tag_AsAdditionalInfoTag_InsideIdReferences.
    /// </summary>
    [Fact]
    public void FAM_SUB_004_Contains_EService_Tag_WithDigitOne_OptIn()
    {
        var scenario = CanonicalScenarios.GetById("FAM-SUB-004");
        var doc = SampleLoader.LoadXDocument(scenario);

        var tagType = doc
            .Descendants(NsDocMeta + "tagType")
            .FirstOrDefault(el => el.Value.Trim() == "E_SERVICE");

        Assert.NotNull(tagType);
        var tagValue = tagType!.Parent?.Element(NsDocMeta + "tagValue")?.Value.Trim();
        Assert.Equal("1", tagValue);
    }

    /// <summary>
    /// Catalog §4.3 anchor — CIV-SUB-004 emits EFSP_FIRST_APPEARANCE_PAID with tagValue "1"
    /// in a party's idReferences block. Confirms the digit-boolean value semantic.
    /// </summary>
    [Fact]
    public void CIV_SUB_004_Contains_EfspFirstAppearancePaid_Tag_WithDigitOne()
    {
        var scenario = CanonicalScenarios.GetById("CIV-SUB-004");
        var doc = SampleLoader.LoadXDocument(scenario);

        var tagType = doc
            .Descendants(NsDocMeta + "tagType")
            .FirstOrDefault(el => el.Value.Trim() == "EFSP_FIRST_APPEARANCE_PAID");

        Assert.NotNull(tagType);
        var tagValue = tagType!.Parent?.Element(NsDocMeta + "tagValue")?.Value.Trim();
        Assert.Equal("1", tagValue);
    }

    /// <summary>
    /// Catalog §4.4 anchor — FAM-SUB-004 pairs E_SERVICE and EFSP_EMAIL as SIBLING
    /// additionalInfoTags elements within the SAME idReferences block. This is a wire-shape
    /// invariant: builder must emit two wrappers (not children of one wrapper); parser must
    /// iterate all sibling additionalInfoTags under an idReferences.
    /// </summary>
    [Fact]
    public void FAM_SUB_004_EserviceAndEfspEmail_AreSiblingAdditionalInfoTags_InSameIdReferences()
    {
        var scenario = CanonicalScenarios.GetById("FAM-SUB-004");
        var doc = SampleLoader.LoadXDocument(scenario);

        // Find an idReferences block that contains BOTH E_SERVICE and EFSP_EMAIL tags.
        var idRefsWithBoth = doc
            .Descendants(NsDocMeta + "idReferences")
            .FirstOrDefault(idRefs =>
            {
                var tagTypes = idRefs
                    .Elements(NsDocMeta + "additionalInfoTags")
                    .Select(t => t.Element(NsDocMeta + "tagType")?.Value.Trim())
                    .Where(v => v is not null)
                    .ToList();
                return tagTypes.Contains("E_SERVICE") && tagTypes.Contains("EFSP_EMAIL");
            });

        Assert.True(idRefsWithBoth is not null,
            "§4.4 wire-shape contract: expected a single <idReferences> block containing two sibling "
            + "<additionalInfoTags> wrappers (E_SERVICE + EFSP_EMAIL). If this fails, check whether the "
            + "sample now uses a different pairing pattern and update catalog §4.4 accordingly.");

        // Assert there are AT LEAST 2 sibling additionalInfoTags children (not a single wrapper).
        var siblingTags = idRefsWithBoth!.Elements(NsDocMeta + "additionalInfoTags").ToList();
        Assert.True(siblingTags.Count >= 2,
            $"§4.4 sibling-wrapper invariant violated: found {siblingTags.Count} additionalInfoTags elements; "
            + "expected at least 2 as siblings (one for E_SERVICE, one for EFSP_EMAIL).");

        // Assert EFSP_EMAIL has free-text tagValue content (not a digit, not an enum).
        var efspEmailTagValue = siblingTags
            .First(t => t.Element(NsDocMeta + "tagType")?.Value.Trim() == "EFSP_EMAIL")
            .Element(NsDocMeta + "tagValue")?.Value.Trim();
        Assert.False(string.IsNullOrEmpty(efspEmailTagValue),
            "§4.4 EFSP_EMAIL tagValue should carry a free-text email payload (empty / null would break round-trip).");
    }

    /// <summary>
    /// Catalog §4 anchor + audit C-3 on the wire — CIV-SUB-002 (Gov Ent Exempt) and CIV-SUB-003
    /// (Fee Waiver) both emit the SAME tagType (FEE_EXEMPTION) with DIFFERENT tagValues
    /// ("GOVT_ENTITY" vs "FEE_WAIVER"). Confirms the tagType is a string enum key and tagValue
    /// carries the discriminating semantic — schema currently represents FEE_EXEMPTION as a
    /// boolean checkbox which is a bug to fix during T-3a.
    /// </summary>
    [Fact]
    public void FeeExemption_SharedTagType_DifferentTagValues_InCIV_SUB_002_vs_003()
    {
        var covSub002 = CanonicalScenarios.GetById("CIV-SUB-002");
        var covSub003 = CanonicalScenarios.GetById("CIV-SUB-003");

        var doc2 = SampleLoader.LoadXDocument(covSub002);
        var doc3 = SampleLoader.LoadXDocument(covSub003);

        string? ExtractFirstFeeExemptionTagValue(XDocument doc) =>
            doc.Descendants(NsDocMeta + "tagType")
                .Where(el => el.Value.Trim() == "FEE_EXEMPTION")
                .Select(el => el.Parent)
                .FirstOrDefault()
                ?.Element(NsDocMeta + "tagValue")
                ?.Value
                .Trim();

        var govtEntityValue = ExtractFirstFeeExemptionTagValue(doc2);
        var feeWaiverValue = ExtractFirstFeeExemptionTagValue(doc3);

        Assert.Equal("GOVT_ENTITY", govtEntityValue);
        Assert.Equal("FEE_WAIVER", feeWaiverValue);
        // And they differ — the whole point.
        Assert.NotEqual(govtEntityValue, feeWaiverValue);
    }
}
