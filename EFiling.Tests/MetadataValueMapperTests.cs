using EFiling.Core.Models;
using EFiling.Nop.Mapping;
using EFiling.Nop.Models;

namespace EFiling.Tests;

/// <summary>
/// Direct unit tests for <see cref="MetadataValueMapper"/> — the single source of truth for
/// MetadataEntryDto → FilingMetadataValue mapping shared between
/// <see cref="EFiling.Nop.Controllers.CourtFilingController.BuildSubmissionFromCreateModel"/>
/// (submit path) and <c>EFilingMvcController.BuildQuickSubmission</c> (fee-calc preview path).
///
/// Scope: these tests exercise the mapper's direct API surface (<c>FromDto</c>,
/// <c>HasAnyContactField</c>, <c>DetermineTagValue</c>, <c>BuildAdditionalInfoTags</c>) with
/// focus on edge cases NOT reached from <c>CourtFilingControllerTests</c>'s wider-scoped
/// controller-level tests. Full audit C-1/C-2/C-3/D-2 regressions are covered end-to-end in
/// <c>CourtFilingControllerTests.cs</c>; this file adds mapper-level unit pinning so the
/// consolidation cannot silently regress even if a future caller bypasses the controller.
/// T-5 consolidation.
/// </summary>
public class MetadataValueMapperTests
{
    // ─────────────────────────────────────────────────────────────────────────────
    // HasAnyContactField — contact presence detection (audit C-2 Bug A helper)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HasAnyContactField_AllNull_ReturnsFalse()
    {
        var dto = new MetadataValueDto();
        Assert.False(MetadataValueMapper.HasAnyContactField(dto));
    }

    [Fact]
    public void HasAnyContactField_AllEmptyStrings_ReturnsFalse()
    {
        var dto = new MetadataValueDto
        {
            Address1 = "", Address2 = "", City = "", State = "", Zip = "",
            Country = "", AddressType = "", TelephoneNumber = "", TelephoneType = "", Email = ""
        };
        Assert.False(MetadataValueMapper.HasAnyContactField(dto));
    }

    [Theory]
    [InlineData("Address1")]
    [InlineData("Address2")]
    [InlineData("City")]
    [InlineData("State")]
    [InlineData("Zip")]
    [InlineData("Country")]
    [InlineData("AddressType")]
    [InlineData("TelephoneNumber")]
    [InlineData("TelephoneType")]
    [InlineData("Email")]
    public void HasAnyContactField_SinglePopulatedField_ReturnsTrue(string fieldName)
    {
        var dto = new MetadataValueDto();
        typeof(MetadataValueDto).GetProperty(fieldName)!.SetValue(dto, "x");
        Assert.True(MetadataValueMapper.HasAnyContactField(dto),
            $"HasAnyContactField must return true when only {fieldName} is populated "
            + "— all 10 contact fields participate in the presence check (per JTI schema).");
    }

    [Fact]
    public void HasAnyContactField_NonContactFieldsOnly_ReturnsFalse()
    {
        // Name/bar-number/firm/organization fields are not contact fields — they must not
        // trigger a false positive that would cause an empty <contactValue/> to be emitted.
        var dto = new MetadataValueDto
        {
            FirstName = "Alice",
            LastName = "Smith",
            BarNumber = "123",
            FirmName = "Acme LLP",
            OrganizationName = "Acme Inc."
        };
        Assert.False(MetadataValueMapper.HasAnyContactField(dto));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // DetermineTagValue — tagValue dispatch (audit C-3 root-cause helper)
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("E_SERVICE")]
    [InlineData("e_service")]
    [InlineData("E_Service")]
    public void DetermineTagValue_EService_ReturnsDigitBooleanOne_CaseInsensitive(string tagType)
    {
        // Catalog §4.1: digit-boolean, NOT "true". Case-insensitive dispatch.
        Assert.Equal("1", MetadataValueMapper.DetermineTagValue(tagType, new MetadataValueDto()));
    }

    [Fact]
    public void DetermineTagValue_EfspFirstAppearancePaid_ReturnsDigitBooleanOne()
    {
        // Catalog §4.3.
        Assert.Equal("1", MetadataValueMapper.DetermineTagValue("EFSP_FIRST_APPEARANCE_PAID", new MetadataValueDto()));
    }

    [Fact]
    public void DetermineTagValue_FeeExemption_WithType_ReturnsEnumToken()
    {
        // Catalog §4.2: string-enum. Value sourced from dto.FeeExemptionType.
        var dto = new MetadataValueDto { FeeExemptionType = "GOVT_ENTITY" };
        Assert.Equal("GOVT_ENTITY", MetadataValueMapper.DetermineTagValue("FEE_EXEMPTION", dto));
    }

    [Fact]
    public void DetermineTagValue_FeeExemption_WithoutType_ReturnsNullForFailClosedSkip()
    {
        // Catalog §2.6.2 fail-closed: emitting FEE_EXEMPTION without an enum would be malformed.
        // Null signals "skip emission" to BuildAdditionalInfoTags.
        var dto = new MetadataValueDto();
        Assert.Null(MetadataValueMapper.DetermineTagValue("FEE_EXEMPTION", dto));
    }

    [Fact]
    public void DetermineTagValue_EfspEmail_WithEmail_ReturnsEmailAddress()
    {
        // Catalog §4.4: free-text. Value sourced from dto.Email.
        var dto = new MetadataValueDto { Email = "filer@example.com" };
        Assert.Equal("filer@example.com", MetadataValueMapper.DetermineTagValue("EFSP_EMAIL", dto));
    }

    [Fact]
    public void DetermineTagValue_EfspEmail_WithoutEmail_ReturnsNullForFailClosedSkip()
    {
        // Same fail-closed discipline as FEE_EXEMPTION — don't emit a malformed tag.
        Assert.Null(MetadataValueMapper.DetermineTagValue("EFSP_EMAIL", new MetadataValueDto()));
    }

    [Fact]
    public void DetermineTagValue_UnknownTag_DefaultsToDigitBooleanOne()
    {
        // Catalog §4.5 unverified-hypothesis tags — preserve the historical pre-fix behavior
        // (emit "1") to avoid regressing per-court variant tags that rely on presence-only
        // semantics. Tighten to null once §4.5 resolves with live evidence.
        Assert.Equal("1", MetadataValueMapper.DetermineTagValue("SOME_UNKNOWN_TAG", new MetadataValueDto()));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // BuildAdditionalInfoTags — flat aggregation across values
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildAdditionalInfoTags_EmptyValues_ReturnsEmptyList()
    {
        var result = MetadataValueMapper.BuildAdditionalInfoTags(Array.Empty<MetadataValueDto>());
        Assert.Empty(result);
    }

    [Fact]
    public void BuildAdditionalInfoTags_ValuesWithNullTags_AreSkipped()
    {
        var values = new[]
        {
            new MetadataValueDto { Id = "v1", Tags = null },
            new MetadataValueDto { Id = "v2", Tags = new List<string>() },
        };
        Assert.Empty(MetadataValueMapper.BuildAdditionalInfoTags(values));
    }

    [Fact]
    public void BuildAdditionalInfoTags_WhitespaceTagType_IsSkipped()
    {
        var values = new[]
        {
            new MetadataValueDto { Tags = new List<string> { "", " ", "\t", "E_SERVICE" } },
        };
        var result = MetadataValueMapper.BuildAdditionalInfoTags(values);
        var tag = Assert.Single(result);
        Assert.Equal("E_SERVICE", tag.TagType);
        Assert.Equal("1", tag.TagValue);
    }

    [Fact]
    public void BuildAdditionalInfoTags_FeeExemptionWithoutType_IsSkippedSilently()
    {
        // DetermineTagValue returns null → BuildAdditionalInfoTags skips without throwing.
        // Other valid tags in the same value still come through.
        var values = new[]
        {
            new MetadataValueDto { Tags = new List<string> { "FEE_EXEMPTION", "E_SERVICE" } },
        };
        var result = MetadataValueMapper.BuildAdditionalInfoTags(values);
        var tag = Assert.Single(result);
        Assert.Equal("E_SERVICE", tag.TagType);
    }

    [Fact]
    public void BuildAdditionalInfoTags_MultipleValuesContributeFlatAggregate()
    {
        // Cross-value aggregation: result is a flat list, not grouped per-value.
        var values = new[]
        {
            new MetadataValueDto { Id = "p1", Tags = new List<string> { "E_SERVICE" } },
            new MetadataValueDto { Id = "p2", Email = "a@b.c", Tags = new List<string> { "EFSP_EMAIL" } },
        };
        var result = MetadataValueMapper.BuildAdditionalInfoTags(values);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, t => t.TagType == "E_SERVICE" && t.TagValue == "1");
        Assert.Contains(result, t => t.TagType == "EFSP_EMAIL" && t.TagValue == "a@b.c");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // FromDto — top-level dispatch guards and wire-casing normalization
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FromDto_EmptyCode_YieldsNothing()
    {
        var entry = new MetadataEntryDto
        {
            Code = "",
            ClassType = "text",
            Values = new List<MetadataValueDto> { new() { Value = "hello" } },
        };
        Assert.Empty(MetadataValueMapper.FromDto(entry));
    }

    [Fact]
    public void FromDto_NullValues_YieldsNothing()
    {
        var entry = new MetadataEntryDto { Code = "FOO", ClassType = "text", Values = null! };
        Assert.Empty(MetadataValueMapper.FromDto(entry));
    }

    [Fact]
    public void FromDto_EmptyValues_YieldsNothing()
    {
        var entry = new MetadataEntryDto
        {
            Code = "FOO",
            ClassType = "text",
            Values = new List<MetadataValueDto>(),
        };
        Assert.Empty(MetadataValueMapper.FromDto(entry));
    }

    [Fact]
    public void FromDto_UnknownClassType_YieldsNothing()
    {
        // Defensive: unrecognized classType strings must not silently produce mis-shaped output.
        // The caller's responsibility is to match the catalog §3 classType taxonomy.
        var entry = new MetadataEntryDto
        {
            Code = "FOO",
            ClassType = "madeUpClassType",
            Values = new List<MetadataValueDto> { new() { Value = "x" } },
        };
        Assert.Empty(MetadataValueMapper.FromDto(entry));
    }

    [Theory]
    [InlineData("caseParticipant")]
    [InlineData("CASEPARTICIPANT")]
    [InlineData("caseparticipant")]
    [InlineData("CaseParticipant")]
    public void FromDto_ClassTypeDispatch_IsCaseInsensitive(string inputClassType)
    {
        // Input can arrive in any casing (UI/JSON not guaranteed camelCase). The mapper
        // lower-cases for dispatch but preserves the canonical wire casing on output.
        var entry = new MetadataEntryDto
        {
            Code = "CODE",
            ClassType = inputClassType,
            Values = new List<MetadataValueDto> { new() { Id = "p1", IsNew = false } },
        };

        var result = MetadataValueMapper.FromDto(entry).ToList();

        var mv = Assert.Single(result);
        Assert.Equal("caseParticipant", mv.ClassType); // wire casing, not input casing
    }

    [Theory]
    [InlineData("caseAssignment")]
    [InlineData("attorney")]
    [InlineData("ATTORNEY")]
    public void FromDto_AttorneyAlias_NormalizesToCaseAssignment(string inputClassType)
    {
        // §3.3: UI emits "attorney" or "caseAssignment" interchangeably; output must normalize
        // to the wire classType "caseAssignment".
        var entry = new MetadataEntryDto
        {
            Code = "CODE",
            ClassType = inputClassType,
            Values = new List<MetadataValueDto> { new() { Id = "att1", IsNew = false } },
        };

        var mv = Assert.Single(MetadataValueMapper.FromDto(entry));
        Assert.Equal("caseAssignment", mv.ClassType);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("string")]
    public void FromDto_TextAndStringAliases_NormalizeToText(string inputClassType)
    {
        // Both "text" and "string" map to the wire classType "text".
        var entry = new MetadataEntryDto
        {
            Code = "CODE",
            ClassType = inputClassType,
            Values = new List<MetadataValueDto> { new() { Value = "hello" } },
        };

        var mv = Assert.Single(MetadataValueMapper.FromDto(entry));
        Assert.Equal("text", mv.ClassType);
        Assert.Equal("hello", mv.TextValue);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // FromDto — D-2 regression gate (simple-value ValueRestriction propagation)
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("codeList", "MOTION_OSC_DETAIL")]
    [InlineData("currency", "100.50")]
    [InlineData("date", "2024-01-15")]
    [InlineData("boolean", "true")]
    [InlineData("text", "some text")]
    public void FromDto_SimpleValueClassTypes_PropagateValueRestriction_AuditD2(string classType, string value)
    {
        // Catalog audit D-2: simple-value arms historically dropped meta.ValueRestriction.
        // Post-fix they must forward it verbatim.
        var entry = new MetadataEntryDto
        {
            Code = "FOO",
            ClassType = classType,
            ValueRestriction = "existing-data",
            Values = new List<MetadataValueDto> { new() { Value = value } },
        };

        var mv = Assert.Single(MetadataValueMapper.FromDto(entry));
        Assert.Equal("existing-data", mv.ValueRestriction);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // FromDto — caseParticipant new-data contact branch (only when SelfRepresented)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FromDto_NewParty_SelfRepresentedTrue_PopulatesContact()
    {
        var entry = new MetadataEntryDto
        {
            Code = "PARTY",
            ClassType = "caseParticipant",
            SubType = "filed-by",
            Values = new List<MetadataValueDto>
            {
                new()
                {
                    IsNew = true,
                    FirstName = "Alice",
                    LastName = "Smith",
                    PartyType = "PLAIN",
                    SelfRepresented = true,
                    Address1 = "1 Main St",
                    City = "Springfield",
                    State = "CA",
                    Zip = "94000",
                    TelephoneNumber = "555-0100",
                    Email = "alice@example.com",
                },
            },
        };

        var mv = Assert.Single(MetadataValueMapper.FromDto(entry));
        Assert.Equal("new-data", mv.ValueRestriction);
        Assert.NotNull(mv.NewPartyValue);
        Assert.NotNull(mv.NewPartyValue!.Contact);
        Assert.Equal("1 Main St", mv.NewPartyValue.Contact!.MailingAddress?.Address1);
        Assert.Equal("555-0100", mv.NewPartyValue.Contact.PhoneNumber);
        Assert.Equal("alice@example.com", mv.NewPartyValue.Contact.Email);
    }

    [Fact]
    public void FromDto_NewParty_SelfRepresentedTrue_PropagatesAllTenContactFields()
    {
        // T-4 cleanup pre-requisite: new-party SelfRep flow is a second
        // contact-mapping site (different from the standalone-contact branch). It must
        // also propagate Country/AddressType/PhoneType — these were silently dropped
        // in the new-party branch until the cleanup pass added them.
        // Pinning the full 10-field round-trip from DTO → ContactInfo wire shape.
        var entry = new MetadataEntryDto
        {
            Code = "PARTY",
            ClassType = "caseParticipant",
            SubType = "filed-by",
            Values = new List<MetadataValueDto>
            {
                new()
                {
                    IsNew = true,
                    FirstName = "Alice",
                    LastName = "Smith",
                    PartyType = "PLAIN",
                    SelfRepresented = true,
                    Address1 = "1 Main St",
                    Address2 = "Suite 5",
                    City = "Madera",
                    State = "CA",
                    Zip = "93637",
                    Country = "US",                  // pre-cleanup silent-drop
                    AddressType = "ML",              // pre-cleanup silent-drop
                    TelephoneNumber = "555-0100",    // pre-cleanup was 'Phone' on DTO
                    TelephoneType = "W",             // pre-cleanup silent-drop
                    Email = "alice@example.com",
                },
            },
        };

        var mv = Assert.Single(MetadataValueMapper.FromDto(entry));
        Assert.NotNull(mv.NewPartyValue);
        Assert.NotNull(mv.NewPartyValue!.Contact);
        var c = mv.NewPartyValue.Contact!;
        Assert.NotNull(c.MailingAddress);
        Assert.Equal("1 Main St", c.MailingAddress!.Address1);
        Assert.Equal("Suite 5", c.MailingAddress.Address2);
        Assert.Equal("Madera", c.MailingAddress.City);
        Assert.Equal("CA", c.MailingAddress.State);
        Assert.Equal("93637", c.MailingAddress.Zip);
        Assert.Equal("US", c.MailingAddress.Country);
        Assert.Equal("ML", c.MailingAddress.AddressType);
        Assert.Equal("555-0100", c.PhoneNumber);     // DTO.TelephoneNumber → wire.PhoneNumber
        Assert.Equal("W", c.PhoneType);              // DTO.TelephoneType → wire.PhoneType
        Assert.Equal("alice@example.com", c.Email);
    }

    [Fact]
    public void FromDto_NewParty_AkasPropagateThroughToAlternateNames()
    {
        // T-4 cleanup audit finding: pre-fix the SF flow silently dropped Akas
        // because MetadataValueDto didn't declare the field, while CC has supported it since
        // the AKA card landed (CourtFilingController.BuildSubmissionFromCreateModel ~line 499).
        // This pin closes that asymmetry: SF MetadataJson Akas now flow through to the wire
        // FilingParty.AlternateNames same as CC PartiesJson Akas.
        var entry = new MetadataEntryDto
        {
            Code = "PARTY",
            ClassType = "caseParticipant",
            Values = new List<MetadataValueDto>
            {
                new()
                {
                    IsNew = true,
                    FirstName = "Alice",
                    LastName = "Smith",
                    PartyType = "PLAIN",
                    SelfRepresented = true,
                    Akas = new List<AlternateNameEntryDto>
                    {
                        new() { Type = "AKA", IsOrganization = false, FirstName = "Ali", LastName = "Smyth", Suffix = "Jr." },
                        new() { Type = "DBA", IsOrganization = true,  OrganizationName = "Smith Holdings LLC" },
                        new() { Type = "",    IsOrganization = false, FirstName = "Skip" }, // empty Type → skipped per CC parity
                    },
                },
            },
        };

        var mv = Assert.Single(MetadataValueMapper.FromDto(entry));
        Assert.NotNull(mv.NewPartyValue);
        Assert.Equal(2, mv.NewPartyValue!.AlternateNames.Count);

        var aka = mv.NewPartyValue.AlternateNames[0];
        Assert.Equal("AKA", aka.Type);
        Assert.Equal("Ali", aka.FirstName);
        Assert.Equal("Smyth", aka.LastName);
        Assert.Equal("Jr.", aka.NameSuffix); // 2026-05-17 silent-drop fix
        Assert.Null(aka.OrganizationName);

        var dba = mv.NewPartyValue.AlternateNames[1];
        Assert.Equal("DBA", dba.Type);
        Assert.Null(dba.FirstName);
        Assert.Null(dba.NameSuffix); // org-shaped AKA must NOT carry person suffix
        Assert.Equal("Smith Holdings LLC", dba.OrganizationName);
    }

    [Fact]
    public void FromDto_NewParty_NoAkas_LeavesAlternateNamesEmpty()
    {
        // Fail-open: no Akas key on the DTO must produce an empty AlternateNames list,
        // not throw or emit a single empty AKA entry.
        var entry = new MetadataEntryDto
        {
            Code = "PARTY",
            ClassType = "caseParticipant",
            Values = new List<MetadataValueDto>
            {
                new() { IsNew = true, FirstName = "Bob", LastName = "Jones", PartyType = "PLAIN" }
            },
        };

        var mv = Assert.Single(MetadataValueMapper.FromDto(entry));
        Assert.NotNull(mv.NewPartyValue);
        Assert.Empty(mv.NewPartyValue!.AlternateNames);
    }

    [Fact]
    public void FromDto_NewAttorney_PropagatesAllTenContactFields()
    {
        // T-4 cleanup pre-requisite: new-attorney flow is a third
        // contact-mapping site (alongside standalone-contact + new-party-self-rep).
        // Same audit pattern: Country/AddressType/PhoneType were silently dropped
        // until the cleanup pass added them. Plus the JS-side regression fix:
        // form-submit JSON keys 'phone'/'phoneType' (legacy) → 'telephoneNumber'/
        // 'telephoneType' (schema-aligned, matching renamed DTO properties).
        var entry = new MetadataEntryDto
        {
            Code = "NEW_ATTORNEY",
            ClassType = "caseAssignment",
            Values = new List<MetadataValueDto>
            {
                new()
                {
                    IsNew = true,
                    FirstName = "Jane",
                    LastName = "Lawyer",
                    BarNumber = "555555",
                    FirmName = "Lawyer LLP",
                    Suffix = "Esq.",                  // pre-cleanup silent-drop (parallel to AKA Suffix)
                    Address1 = "100 Main St",
                    Address2 = "Floor 9",
                    City = "Madera",
                    State = "CA",
                    Zip = "93637",
                    Country = "US",                  // pre-cleanup silent-drop
                    AddressType = "BUS",             // pre-cleanup silent-drop
                    TelephoneNumber = "555-1234",    // pre-cleanup was 'Phone' on DTO
                    TelephoneType = "W",             // pre-cleanup silent-drop
                    Email = "jane@lawyerllp.com",
                },
            },
        };

        var mv = Assert.Single(MetadataValueMapper.FromDto(entry));
        Assert.NotNull(mv.CaseAssignmentValue);
        Assert.Equal("Esq.", mv.CaseAssignmentValue!.NameSuffix); // 2026-05-17 silent-drop fix
        Assert.NotNull(mv.CaseAssignmentValue.Contact);
        var c = mv.CaseAssignmentValue.Contact!;
        Assert.NotNull(c.MailingAddress);
        Assert.Equal("100 Main St", c.MailingAddress!.Address1);
        Assert.Equal("Floor 9", c.MailingAddress.Address2);
        Assert.Equal("Madera", c.MailingAddress.City);
        Assert.Equal("CA", c.MailingAddress.State);
        Assert.Equal("93637", c.MailingAddress.Zip);
        Assert.Equal("US", c.MailingAddress.Country);
        Assert.Equal("BUS", c.MailingAddress.AddressType);
        Assert.Equal("555-1234", c.PhoneNumber);     // DTO.TelephoneNumber → wire.PhoneNumber
        Assert.Equal("W", c.PhoneType);              // DTO.TelephoneType → wire.PhoneType
        Assert.Equal("jane@lawyerllp.com", c.Email);
    }

    [Fact]
    public void FromDto_NewParty_SelfRepresentedFalse_LeavesContactNull_UsesLeadAttorneyId()
    {
        var entry = new MetadataEntryDto
        {
            Code = "PARTY",
            ClassType = "caseParticipant",
            SubType = "filed-by",
            Values = new List<MetadataValueDto>
            {
                new()
                {
                    IsNew = true,
                    FirstName = "Alice",
                    LastName = "Smith",
                    PartyType = "PLAIN",
                    SelfRepresented = false,
                    LeadAttorneyId = "att-42",
                    // Address/phone/email provided but must be ignored when not self-represented
                    Address1 = "ignored",
                    Email = "ignored@example.com",
                },
            },
        };

        var mv = Assert.Single(MetadataValueMapper.FromDto(entry));
        Assert.Null(mv.NewPartyValue?.Contact);
        Assert.Contains("att-42", mv.IdReferences);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // FromDto — caseParticipant split between existing-data + new-data in one entry
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FromDto_CaseParticipant_MixedExistingAndNew_EmitsSeparateFilingMetadataValues()
    {
        // Baseline pattern (CIV-SUB-005 / CIV-SUB-019): a single MetadataEntryDto carries both
        // existing-data (selected from roster) and new-data (freshly keyed) participants. The
        // mapper must emit one `existing-data` FilingMetadataValue (with IdReferences) AND one
        // `new-data` FilingMetadataValue per new participant.
        var entry = new MetadataEntryDto
        {
            Code = "PARTY",
            ClassType = "caseParticipant",
            SubType = "filed-by",
            Values = new List<MetadataValueDto>
            {
                new() { Id = "p1", IsNew = false },
                new() { Id = "p2", IsNew = false },
                new() { IsNew = true, FirstName = "Alice", LastName = "Smith", PartyType = "PLAIN" },
            },
        };

        var result = MetadataValueMapper.FromDto(entry).ToList();

        Assert.Equal(2, result.Count);
        var existing = result.Single(r => r.ValueRestriction == "existing-data");
        Assert.Equal(new[] { "p1", "p2" }, existing.IdReferences);
        var newData = result.Single(r => r.ValueRestriction == "new-data");
        Assert.Equal("Alice", newData.NewPartyValue?.FirstName);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Unknown-input anchor tests
    //
    // Partner tests to ReviewFilingXmlBuilderTests.BuildReviewFiling_UnknownClassType_*
    // (builder-side fail-closed) and .BuildReviewFiling_SchemaDeclaredButUnimplementedClassType_*
    // (T-8 stub sweep). This file anchors the MAPPER-side policy for two additional
    // unknown-input axes: unknown tagType and unknown valueRestriction.
    //
    // ▸ Unknown classType → silent drop at mapper, throws InvalidOperationException at builder.
    //   (existing: FromDto_UnknownClassType_YieldsNothing + residual-b builder test)
    // ▸ Unknown tagType  → INTENTIONALLY LOOSE at mapper (emits "1"). See anchor test below.
    // ▸ Unknown valueRestriction → passthrough at mapper + builder (no validation).
    //   See anchor test below.
    //
    // These anchor tests lock the deliberate decisions so a future "tighten" pass doesn't
    // silently flip behavior. To tighten either axis, update BOTH the production code AND
    // these anchor tests — the test rename from "AnchorsIntentional*" to "FailsClosed*" is a
    // visible review signal.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void DetermineTagValue_UnknownTagType_EmitsOne_AnchorsIntentionalLooseBehavior()
    {
        // Catalog §3.0 observation #3 fail-closed discipline does NOT extend to unknown
        // tagTypes at the mapper layer today. MetadataValueMapper.DetermineTagValue (fallback
        // arm, line ~392) deliberately emits "1" for any unrecognized tagType to avoid
        // regressing per-court variant tags that rely on presence-only semantics. When
        // §4.5 hypotheses resolve with live wire evidence per §2.6.2, flip this test to
        // assert `Assert.Null(val)` and update the mapper fallback to return null — both
        // changes together turn unknown tagTypes into a hard fail at builder time.
        var dto = new MetadataValueDto();
        var value = MetadataValueMapper.DetermineTagValue("TOTALLY_MADE_UP_TAG_v0", dto);
        Assert.Equal("1", value);
    }

    [Fact]
    public void BuildAdditionalInfoTags_MixesKnownAndUnknownTagTypes_EmitsBothWithoutDropping()
    {
        // Downstream consequence of the DetermineTagValue loose fallback: an unknown tag
        // alongside known tags is emitted verbatim as a TagType entry with TagValue="1".
        // This anchor prevents an accidental partial tightening where the loop starts
        // silently dropping unknowns even though DetermineTagValue still returns "1".
        var values = new List<MetadataValueDto>
        {
            new() { Tags = new List<string> { "E_SERVICE", "MYSTERY_COURT_SPECIFIC_TAG" } }
        };

        var tags = MetadataValueMapper.BuildAdditionalInfoTags(values);

        Assert.Equal(2, tags.Count);
        Assert.Contains(tags, t => t.TagType == "E_SERVICE" && t.TagValue == "1");
        Assert.Contains(tags, t => t.TagType == "MYSTERY_COURT_SPECIFIC_TAG" && t.TagValue == "1");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T-4 composite-swap pre-requisite: standalone contact branch FromDto
    // tests pinning the full 10-field round-trip (DTO → ContactValueData wire shape).
    //
    // Why these tests matter: pre-2026-05-17 the DTO had only 7 of the 10 schema-declared
    // contact fields (Address1/Address2/City/State/Zip/Phone/Email). Country, AddressType,
    // and TelephoneType were silently dropped at deserialization despite (a) being declared
    // in JtiClassTypeSchema.json#/classTypes/contact and (b) being supported on the wire
    // ContactValueData. The frontend incumbent had matching silent-drop bugs (rendered only
    // 7 of the 10 fields). The T-4 contact composite swap surfaced this and fixed it
    // end-to-end. These tests pin the corrected round-trip so a future schema/DTO drift fails
    // immediately at the mapper layer.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FromDto_StandaloneContact_AllTenSchemaFields_RoundTripToWireContactValue()
    {
        // Pins the full 10-field schema-aligned DTO → wire ContactValue round-trip.
        // DTO property names match schema field names; mapper handles the wire-shape rename.
        var entry = new MetadataEntryDto
        {
            Code = "FILING_PARTY_ADDRESS",
            ClassType = "contact",
            ValueRestriction = "new-data",
            Values = new List<MetadataValueDto>
            {
                new()
                {
                    Address1 = "123 Main St",
                    Address2 = "Suite 4",
                    City = "Madera",
                    State = "CA",
                    Zip = "93637",
                    Country = "US",
                    AddressType = "ML",            // ML = Mailing Address (per ADDRESS_TYPE codelist)
                    TelephoneNumber = "555-0100",
                    TelephoneType = "W",           // W = Work (per TELEPHONE_TYPE codelist)
                    Email = "alice@example.com",
                },
            },
        };

        var mv = Assert.Single(MetadataValueMapper.FromDto(entry));
        Assert.Equal("contact", mv.ClassType);
        Assert.Equal("FILING_PARTY_ADDRESS", mv.Code);
        Assert.NotNull(mv.ContactValue);

        var c = mv.ContactValue!;
        Assert.Equal("123 Main St", c.Address1);
        Assert.Equal("Suite 4", c.Address2);
        Assert.Equal("Madera", c.City);
        Assert.Equal("CA", c.State);
        Assert.Equal("93637", c.Zip);
        Assert.Equal("US", c.Country);
        Assert.Equal("ML", c.AddressType);
        Assert.Equal("555-0100", c.PhoneNumber);  // DTO.TelephoneNumber → wire.PhoneNumber
        Assert.Equal("W", c.PhoneType);            // DTO.TelephoneType → wire.PhoneType
        Assert.Equal("alice@example.com", c.Email);
    }

    [Fact]
    public void FromDto_StandaloneContact_OnlyCountryPopulated_StillEmits()
    {
        // Pinning the new HasAnyContactField gate: a contact DTO with ONLY country populated
        // (no address1/email/phone) must still emit a wire ContactValue. Pre-2026-05-17 this
        // would have failed silently because Country wasn't on the DTO at all (deserialized
        // as null → dropped at HasAnyContactField gate).
        var entry = new MetadataEntryDto
        {
            Code = "FOREIGN_PARTY_COUNTRY",
            ClassType = "contact",
            ValueRestriction = "new-data",
            Values = new List<MetadataValueDto>
            {
                new() { Country = "FR" },
            },
        };

        var mv = Assert.Single(MetadataValueMapper.FromDto(entry));
        Assert.NotNull(mv.ContactValue);
        Assert.Equal("FR", mv.ContactValue!.Country);
    }

    [Fact]
    public void FromDto_StandaloneContact_PartialFieldsOnly_StillRoundTripsUnchanged()
    {
        // Pins fail-open behavior: a contact DTO with only some fields populated must still
        // round-trip correctly. The unset schema fields stay null on the wire (matching
        // the wire-builder's expected element-omission semantics) and HasAnyContactField
        // still returns true so the entry is emitted.
        var entry = new MetadataEntryDto
        {
            Code = "FILING_PARTY_ADDRESS",
            ClassType = "contact",
            ValueRestriction = "new-data",
            Values = new List<MetadataValueDto>
            {
                new()
                {
                    Address1 = "1 Old St",
                    City = "Fresno",
                    State = "CA",
                    Zip = "93720",
                    TelephoneNumber = "555-9999",
                    Email = "partial@example.com",
                },
            },
        };

        var mv = Assert.Single(MetadataValueMapper.FromDto(entry));
        Assert.NotNull(mv.ContactValue);
        var c = mv.ContactValue!;
        Assert.Equal("1 Old St", c.Address1);
        Assert.Equal("555-9999", c.PhoneNumber);
        Assert.Null(c.Country);
        Assert.Null(c.AddressType);
        Assert.Null(c.PhoneType);
    }
}
