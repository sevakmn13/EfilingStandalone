using System.Text.Json;
using System.Xml.Linq;
using EFiling.Core.Models;
using EFiling.Nop.Controllers;
using EFiling.Nop.Models;
using EFiling.Providers.JTI.Builders;

namespace EFiling.Tests;

/// <summary>
/// Regression tests for <see cref="CourtFilingController.BuildSubmissionFromCreateModel"/>.
/// Covers Track D.post audit findings — silent-drop bugs and DTO → domain mapping correctness.
/// </summary>
public class CourtFilingControllerTests
{
    // ── Fix C-1: FirstAppearancePaid must propagate to FilingParty ──

    [Fact]
    public void BuildSubmission_FirstAppearancePaidTrue_PropagatesToFilingParty()
    {
        var model = NewMinimalModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto
            {
                Side = "filing",
                PartyType = "PLAIN",
                FirstName = "Alice",
                LastName = "Smith",
                FirstAppearancePaid = true
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        Assert.Single(sub.Parties);
        Assert.True(sub.Parties[0].FirstAppearancePaid,
            "Regression C-1: FirstAppearancePaid=true in DTO must be preserved on the built FilingParty.");
    }

    [Fact]
    public void BuildSubmission_FirstAppearancePaidFalse_StaysFalse()
    {
        var model = NewMinimalModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto { Side = "filing", PartyType = "PLAIN", FirstName = "Alice", LastName = "Smith" }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        Assert.False(sub.Parties[0].FirstAppearancePaid);
    }

    // ── Fix C-2: GovernmentExempt must propagate to FilingParty ──

    [Fact]
    public void BuildSubmission_GovernmentExemptTrue_PropagatesToFilingParty()
    {
        var model = NewMinimalModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto
            {
                Side = "filing",
                PartyType = "PLAIN",
                OrganizationName = "State of California",
                IsOrganization = true,
                GovernmentExempt = true
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        Assert.Single(sub.Parties);
        Assert.True(sub.Parties[0].GovernmentExempt,
            "Regression C-2: GovernmentExempt=true in DTO must be preserved on the built FilingParty.");
    }

    // ── Fix C-3: Self-rep phone/email must survive when address is empty ──

    [Fact]
    public void BuildSubmission_SelfRepWithPhoneEmailNoAddress_BuildsContactWithNullMailingAddress()
    {
        var model = NewMinimalModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto
            {
                Side = "filing",
                PartyType = "PLAIN",
                FirstName = "Bob",
                LastName = "Jones",
                SelfRepresented = true,
                // NO Address1 set
                PhoneType = "HOM",
                Phone = "559-555-1234",
                Email = "bob@example.com"
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        Assert.Single(sub.Parties);
        var party = sub.Parties[0];
        Assert.NotNull(party.Contact);
        Assert.Null(party.Contact!.MailingAddress);
        Assert.Equal("559-555-1234", party.Contact.PhoneNumber);
        Assert.Equal("HOM", party.Contact.PhoneType);
        Assert.Equal("bob@example.com", party.Contact.Email);
    }

    [Fact]
    public void BuildSubmission_SelfRepWithAddressOnly_BuildsContactWithMailingAddress()
    {
        var model = NewMinimalModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto
            {
                Side = "filing",
                PartyType = "PLAIN",
                FirstName = "Carol",
                LastName = "Adams",
                SelfRepresented = true,
                AddressType = "MAIL",
                Address1 = "123 Main St",
                City = "Madera",
                State = "CA",
                Zip = "93637",
                Country = "US"
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        var party = sub.Parties[0];
        Assert.NotNull(party.Contact);
        Assert.NotNull(party.Contact!.MailingAddress);
        Assert.Equal("123 Main St", party.Contact.MailingAddress!.Address1);
        Assert.Equal("Madera", party.Contact.MailingAddress.City);
        Assert.Equal("CA", party.Contact.MailingAddress.State);
        Assert.Equal("93637", party.Contact.MailingAddress.Zip);
    }

    [Fact]
    public void BuildSubmission_SelfRepWithAllContactFields_BuildsFullContact()
    {
        var model = NewMinimalModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto
            {
                Side = "filing",
                PartyType = "PLAIN",
                FirstName = "Dan",
                LastName = "Evans",
                SelfRepresented = true,
                AddressType = "MAIL",
                Address1 = "500 Elm",
                City = "Madera",
                State = "CA",
                Zip = "93637",
                Country = "US",
                PhoneType = "HOM",
                Phone = "559-555-9999",
                Email = "dan@example.com"
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        var party = sub.Parties[0];
        Assert.NotNull(party.Contact);
        Assert.NotNull(party.Contact!.MailingAddress);
        Assert.Equal("dan@example.com", party.Contact.Email);
        Assert.Equal("559-555-9999", party.Contact.PhoneNumber);
    }

    [Fact]
    public void BuildSubmission_SelfRepWithNoContactFields_ContactIsNull()
    {
        var model = NewMinimalModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto
            {
                Side = "filing",
                PartyType = "PLAIN",
                FirstName = "Eve",
                LastName = "Foo",
                SelfRepresented = true
                // no address, no phone, no email
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        Assert.Null(sub.Parties[0].Contact);
    }

    [Fact]
    public void BuildSubmission_NotSelfRep_ContactIsNullEvenWhenFieldsPresent()
    {
        // If a party is represented by an attorney, their contact info is conveyed via the
        // attorney's address. We intentionally drop the party's own contact fields in that case.
        var model = NewMinimalModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto
            {
                Side = "filing",
                PartyType = "PLAIN",
                FirstName = "Frank",
                LastName = "Gill",
                SelfRepresented = false,
                Phone = "559-555-7777",
                Email = "frank@example.com"
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        Assert.Null(sub.Parties[0].Contact);
    }

    // ── Sanity: existing known-good mappings don't regress ──

    [Fact]
    public void BuildSubmission_AllCoreFields_MapCorrectly()
    {
        var model = NewMinimalModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto
            {
                Side = "filing",
                PartyType = "PLAIN",
                IsOrganization = false,
                FirstName = "Grace",
                MiddleName = "Q",
                LastName = "Hopper",
                Suffix = "PhD",
                FeeExemptionType = "FEE_WAIVER",
                InterpreterLanguage = "SPA",
                EService = true
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        var party = sub.Parties[0];
        Assert.Equal("PLAIN", party.RoleCode);
        Assert.Equal("Grace", party.FirstName);
        Assert.Equal("Q", party.MiddleName);
        Assert.Equal("Hopper", party.LastName);
        Assert.Equal("PhD", party.NameSuffix);
        Assert.Equal("FEE_WAIVER", party.FeeExemptionRequestType);
        Assert.Equal("SPA", party.InterpreterLanguage);
        Assert.True(party.EService);
    }

    [Fact]
    public void BuildSubmission_FilingAndOpposingParties_AssignCorrectReferenceIds()
    {
        var model = NewMinimalModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto { Side = "filing", PartyType = "PLAIN", FirstName = "P1", LastName = "X" },
            new PartyEntryDto { Side = "filing", PartyType = "PLAIN", FirstName = "P2", LastName = "X" },
            new PartyEntryDto { Side = "opposing", PartyType = "DEF", FirstName = "D1", LastName = "Y" }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        Assert.Equal(3, sub.Parties.Count);
        Assert.Equal("filedBy0", sub.Parties[0].ReferenceId);
        Assert.Equal("filedBy1", sub.Parties[1].ReferenceId);
        Assert.Equal("filedAsTo0", sub.Parties[2].ReferenceId);
    }

    // ── Fix H-2: SubmitterUsername court-configurable ──

    [Fact]
    public void BuildSubmission_NoConfig_UsesDefaultSubmitterUsername()
    {
        var model = NewMinimalModel();
        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);
        // Default from DefaultSubmitterUsername const — "legalhub"
        Assert.Equal("legalhub", sub.SubmitterUsername);
    }

    [Fact]
    public void BuildSubmission_ConfigWithoutOverride_UsesDefaultSubmitterUsername()
    {
        var model = NewMinimalModel();
        var config = new CourtConfiguration { CourtId = "madera" };
        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model, config);
        Assert.Equal("legalhub", sub.SubmitterUsername);
    }

    [Fact]
    public void BuildSubmission_ConfigOverridesSubmitterUsername_UsesOverride()
    {
        var model = NewMinimalModel();
        var config = new CourtConfiguration
        {
            CourtId = "madera",
            ExtraFlags = { [CourtFilingController.ExtraFlagKey_SubmitterUsername] = "acme-efsp" }
        };
        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model, config);
        Assert.Equal("acme-efsp", sub.SubmitterUsername);
    }

    [Fact]
    public void BuildSubmission_ConfigWithEmptyOverride_FallsBackToDefault()
    {
        var model = NewMinimalModel();
        var config = new CourtConfiguration
        {
            CourtId = "madera",
            ExtraFlags = { [CourtFilingController.ExtraFlagKey_SubmitterUsername] = "   " }
        };
        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model, config);
        Assert.Equal("legalhub", sub.SubmitterUsername);
    }

    // ── Fix H-3: AttorneyRoleCode court-configurable ──

    [Fact]
    public void BuildSubmission_NoConfig_AttorneysGetDefaultRoleCode()
    {
        var model = NewMinimalModel();
        model.AttorneysJson = JsonSerializer.Serialize(new[]
        {
            new AttorneyEntryDto { FirstName = "A", LastName = "B", BarNumber = "12345" }
        });
        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);
        var attorney = Assert.Single(sub.Parties);
        Assert.Equal("ATT", attorney.RoleCode);
    }

    [Fact]
    public void BuildSubmission_ConfigOverridesAttorneyRoleCode_AttorneysUseOverride()
    {
        var model = NewMinimalModel();
        model.AttorneysJson = JsonSerializer.Serialize(new[]
        {
            new AttorneyEntryDto { FirstName = "A", LastName = "B", BarNumber = "12345" }
        });
        var config = new CourtConfiguration
        {
            CourtId = "placer",
            ExtraFlags = { [CourtFilingController.ExtraFlagKey_AttorneyRoleCode] = "ATTORNEY" }
        };
        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model, config);
        var attorney = Assert.Single(sub.Parties);
        Assert.Equal("ATTORNEY", attorney.RoleCode);
    }

    // ── Fix M-1: Attorney Contact conditional (symmetric with party) ──

    [Fact]
    public void BuildSubmission_AttorneyWithNoContact_ContactIsNull()
    {
        var model = NewMinimalModel();
        model.AttorneysJson = JsonSerializer.Serialize(new[]
        {
            new AttorneyEntryDto { FirstName = "Bare", LastName = "Attorney", BarNumber = "12345" }
            // no address/phone/email
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        var attorney = Assert.Single(sub.Parties);
        Assert.Null(attorney.Contact);
    }

    [Fact]
    public void BuildSubmission_AttorneyWithEmailOnly_BuildsContactWithNullAddress()
    {
        var model = NewMinimalModel();
        model.AttorneysJson = JsonSerializer.Serialize(new[]
        {
            new AttorneyEntryDto
            {
                FirstName = "E",
                LastName = "Mail",
                BarNumber = "12345",
                Email = "counsel@example.com"
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        var attorney = Assert.Single(sub.Parties);
        Assert.NotNull(attorney.Contact);
        Assert.Null(attorney.Contact!.MailingAddress);
        Assert.Equal("counsel@example.com", attorney.Contact.Email);
    }

    // ── Fix M-2: ClassType canonicalization ──

    [Theory]
    [InlineData("caseparticipant", "caseParticipant")]
    [InlineData("CASEPARTICIPANT", "caseParticipant")]
    [InlineData("CaseParticipant", "caseParticipant")]
    [InlineData("caseassignment", "caseAssignment")]
    [InlineData("codelist", "codeList")]
    [InlineData("CODELIST", "codeList")]
    [InlineData("text", "text")]
    [InlineData("STRING", "text")]
    [InlineData("boolean", "boolean")]
    [InlineData("currency", "currency")]
    [InlineData("date", "date")]
    [InlineData("number", "number")]
    [InlineData("attorney", "attorney")]
    [InlineData("contact", "contact")]
    [InlineData("document", "document")]
    [InlineData("judgment", "judgment")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("someUnknownFutureType", "someUnknownFutureType")]
    public void CanonicalizeClassType_ReturnsCanonicalForm(string? input, string expected)
    {
        Assert.Equal(expected, CourtFilingController.CanonicalizeClassType(input));
    }

    [Fact]
    public void BuildSubmission_DocumentMetadataClassTypeEmittedCanonically()
    {
        var model = NewMinimalModel();
        var docs = new[]
        {
            new DocumentEntryDto
            {
                Role = "lead",
                DocumentCode = "401011",
                BlobUrl = "https://blob.example.com/doc1.pdf",
                Metadata = new List<MetadataEntryDto>
                {
                    new() { Code = "FOO", ClassType = "CASEPARTICIPANT", Value = "someId" },
                    new() { Code = "BAR", ClassType = "codelist", Value = "someCode" },
                    new() { Code = "BAZ", ClassType = "string", Value = "hello" }
                }
            }
        };
        model.DocumentsJson = JsonSerializer.Serialize(docs);

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        Assert.NotNull(sub.LeadDocument);
        Assert.Equal(3, sub.LeadDocument!.MetadataValues.Count);
        Assert.Equal("caseParticipant", sub.LeadDocument.MetadataValues[0].ClassType);
        Assert.Equal("codeList", sub.LeadDocument.MetadataValues[1].ClassType);
        Assert.Equal("text", sub.LeadDocument.MetadataValues[2].ClassType);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Audit C-4 regression guard
    //
    // Background: The 2026-04-19 verification audit (EFILING_AUDIT_SUBSEQUENT_FILING_VERIFICATION.md
    // §3.5) identified that the controller was silently dropping per-doc metadata on
    // CONNECTED (non-lead) documents — only model.MetadataJson (lead-only) was processed,
    // and DocumentEntryDto.Metadata on each connected doc was never consumed. This broke
    // proposed-order, stipulation, and delayed-NFRC scenarios.
    //
    // The fix was landed earlier; these two tests LOCK the current correct behavior so a
    // future regression (e.g., adding `if (isLead)` inside the per-doc metadata loop) fails
    // the suite loudly. Tests cover both the controller→submission mapping and the full
    // controller→builder→wire path.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildSubmission_ConnectedDocumentMetadata_FlowsToConnectedDocument_AuditC4Regression()
    {
        // Scenario: one lead doc and one connected doc, each with metadata of different
        // codes so we can distinguish which bucket each item lands in. Assert the connected
        // doc's metadata does NOT merge into the lead doc, and vice versa.
        var model = NewMinimalModel();
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto
            {
                Role = "lead",
                DocumentCode = "401011",
                BlobUrl = "https://blob.example.com/lead.pdf",
                Metadata = new List<MetadataEntryDto>
                {
                    new() { Code = "LEAD_ONLY_CODE", ClassType = "codelist", Value = "L1" }
                }
            },
            new DocumentEntryDto
            {
                Role = "connected",
                DocumentCode = "401012",
                BlobUrl = "https://blob.example.com/connected.pdf",
                Metadata = new List<MetadataEntryDto>
                {
                    new() { Code = "CONNECTED_ONLY_CODE", ClassType = "codelist", Value = "C1" }
                }
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        // Lead-side assertions.
        Assert.NotNull(sub.LeadDocument);
        Assert.Single(sub.LeadDocument!.MetadataValues);
        Assert.Equal("LEAD_ONLY_CODE", sub.LeadDocument.MetadataValues[0].Code);
        Assert.DoesNotContain(sub.LeadDocument.MetadataValues, mv => mv.Code == "CONNECTED_ONLY_CODE");

        // Connected-side assertions — the regression this test guards.
        Assert.Single(sub.ConnectedDocuments);
        var connectedDoc = sub.ConnectedDocuments[0];
        Assert.Single(connectedDoc.MetadataValues);
        Assert.Equal("CONNECTED_ONLY_CODE", connectedDoc.MetadataValues[0].Code);
        Assert.DoesNotContain(connectedDoc.MetadataValues, mv => mv.Code == "LEAD_ONLY_CODE");
    }

    [Fact]
    public void BuildReviewFilingRequest_ConnectedDocumentMetadata_EmitsDocumentFilingMetaDataOnConnectedDoc_AuditC4Regression()
    {
        // End-to-end guard: run the controller mapping + full XML build, then assert the wire
        // XML places <DocumentFilingMetaData> under BOTH <FilingLeadDocument> and
        // <FilingConnectedDocument>. A naive builder regression that skipped metadata for
        // non-lead docs would leave <FilingConnectedDocument> without a metadata block.
        var model = NewMinimalModel();
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto
            {
                Role = "lead",
                DocumentCode = "401011",
                BlobUrl = "https://blob.example.com/lead.pdf",
                Metadata = new List<MetadataEntryDto>
                {
                    new() { Code = "LEAD_ONLY_CODE", ClassType = "codelist", Value = "L1" }
                }
            },
            new DocumentEntryDto
            {
                Role = "connected",
                DocumentCode = "401012",
                BlobUrl = "https://blob.example.com/connected.pdf",
                Metadata = new List<MetadataEntryDto>
                {
                    new() { Code = "CONNECTED_ONLY_CODE", ClassType = "codelist", Value = "C1" }
                }
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);
        var config = new CourtConfiguration { CourtId = "madera", DisplayName = "Madera" };
        var xml = ReviewFilingXmlBuilder.BuildReviewFilingRequest(sub, config);

        var doc = XDocument.Parse(xml);
        XNamespace cfm = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CoreFilingMessage-4.0";
        // <DocumentFilingMetaData> is a JTI-extension element, NOT OASIS LegalXML. The wire
        // baseline (e.g. Cross-Complaint Sample.xml) declares xmlns:ns9 to this extension URI;
        // the builder uses the matching SoapEnvelopeBuilder.NsJtiDocumentFilingMetaData
        // constant. A previous version of this test used the OASIS pattern
        // "...legalxml-courtfiling:schema:xsd:DocumentFilingMetaData-4.0" by analogy with
        // sibling messages — that namespace does not exist for this element.
        XNamespace dfm = "urn:com.journaltech:ecourt:ecf:extension:DocumentFilingMetaData";

        var leadDocEl = doc.Descendants(cfm + "FilingLeadDocument").Single();
        var connectedDocEl = doc.Descendants(cfm + "FilingConnectedDocument").Single();

        // Lead doc carries its metadata.
        var leadMeta = leadDocEl.Element(dfm + "DocumentFilingMetaData");
        Assert.NotNull(leadMeta);
        Assert.Contains("LEAD_ONLY_CODE", leadMeta!.ToString());
        Assert.DoesNotContain("CONNECTED_ONLY_CODE", leadMeta.ToString());

        // Connected doc carries its metadata — this is the C-4 regression.
        var connectedMeta = connectedDocEl.Element(dfm + "DocumentFilingMetaData");
        Assert.True(connectedMeta is not null,
            "Audit C-4 regression: connected document must emit DocumentFilingMetaData when the model provides per-doc metadata. " +
            "If this fails, the controller or builder has silently dropped non-lead-doc metadata — see " +
            "EFILING_AUDIT_SUBSEQUENT_FILING_VERIFICATION.md §3.5 for original finding.");
        Assert.Contains("CONNECTED_ONLY_CODE", connectedMeta!.ToString());
        Assert.DoesNotContain("LEAD_ONLY_CODE", connectedMeta.ToString());
    }

    // ── Fix M-4: Malformed JSON throws ArgumentException (not JsonException) ──

    [Fact]
    public void BuildSubmission_MalformedPartiesJson_ThrowsArgumentExceptionWithFieldName()
    {
        var model = NewMinimalModel();
        model.PartiesJson = "{not valid json[[";

        var ex = Assert.Throws<ArgumentException>(() =>
            CourtFilingController.BuildSubmissionFromCreateModel(model));

        Assert.Equal(nameof(model.PartiesJson), ex.ParamName);
        Assert.Contains("Invalid JSON", ex.Message);
    }

    [Fact]
    public void BuildSubmission_MalformedAttorneysJson_ThrowsArgumentExceptionWithFieldName()
    {
        var model = NewMinimalModel();
        model.AttorneysJson = "{totally invalid";

        var ex = Assert.Throws<ArgumentException>(() =>
            CourtFilingController.BuildSubmissionFromCreateModel(model));

        Assert.Equal(nameof(model.AttorneysJson), ex.ParamName);
    }

    [Fact]
    public void BuildSubmission_MalformedDocumentsJson_ThrowsArgumentExceptionWithFieldName()
    {
        var model = NewMinimalModel();
        model.DocumentsJson = "[{\"broken";

        var ex = Assert.Throws<ArgumentException>(() =>
            CourtFilingController.BuildSubmissionFromCreateModel(model));

        Assert.Equal(nameof(model.DocumentsJson), ex.ParamName);
    }

    [Fact]
    public void BuildDisplayName_MalformedPartiesJson_FallsBackToUnknownVsUnknown()
    {
        // BuildDisplayName is defensive — draft naming should never throw.
        var model = NewMinimalModel();
        model.PartiesJson = "@@not valid@@";

        var name = CourtFilingController.BuildDisplayName(model);

        Assert.Equal("Unknown v. Unknown", name);
    }

    [Fact]
    public void BuildDisplayName_FilingAndOpposing_RendersVersusCaption()
    {
        var model = NewMinimalModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto { Side = "filing", PartyType = "PLAIN", FirstName = "Pat", LastName = "Plaintiff" },
            new PartyEntryDto { Side = "opposing", PartyType = "DEF", FirstName = "Dana", LastName = "Defendant" }
        });

        Assert.Equal("Pat Plaintiff v. Dana Defendant", CourtFilingController.BuildDisplayName(model));
    }

    [Fact]
    public void BuildDisplayName_NoOpposingParty_RendersInReCaption()
    {
        // F-E1: Probate / single-party / ex-parte initiations have no opposing party — the label
        // must read "In re <party>", not the misleading "<party> v. Unknown".
        var model = NewMinimalModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto { Side = "filing", PartyType = "DECEDENT", FirstName = "Jane", LastName = "Estate" }
        });

        Assert.Equal("In re Jane Estate", CourtFilingController.BuildDisplayName(model));
    }

    [Fact]
    public void BuildDisplayName_OrganizationFilingNoOpposing_RendersInReOrgName()
    {
        var model = NewMinimalModel();
        model.PartiesJson = JsonSerializer.Serialize(new[]
        {
            new PartyEntryDto { Side = "filing", PartyType = "PET", IsOrganization = true, OrganizationName = "Acme Trust" }
        });

        Assert.Equal("In re Acme Trust", CourtFilingController.BuildDisplayName(model));
    }

    // ── Fix M-6: Size limits ──

    [Fact]
    public void BuildSubmission_PartiesJsonTooLarge_ThrowsArgumentException()
    {
        var model = NewMinimalModel();
        // Pad way past the 1 MB limit with a huge but still-valid-looking-ish payload.
        model.PartiesJson = "[" + new string('a', CourtFilingController.MaxJsonFieldChars + 10) + "]";

        var ex = Assert.Throws<ArgumentException>(() =>
            CourtFilingController.BuildSubmissionFromCreateModel(model));

        Assert.Equal(nameof(model.PartiesJson), ex.ParamName);
        Assert.Contains("too large", ex.Message);
    }

    [Fact]
    public void BuildSubmission_PartiesJsonAtLimit_Succeeds()
    {
        // A payload exactly at the limit should still be accepted (it just won't parse as real parties).
        var model = NewMinimalModel();
        // Valid empty-array JSON, well under the limit.
        model.PartiesJson = "[]";
        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);
        Assert.Empty(sub.Parties);
    }

    // ── Fix M-8: BinaryLocationUri placeholder removed; validation mode controls strictness ──

    [Fact]
    public void BuildSubmission_DocumentMissingBlobUrl_ThrowsWhenValidateForSubmission()
    {
        var model = NewMinimalModel();
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto
            {
                Role = "lead",
                DocumentCode = "401011",
                BlobUrl = null // missing
            }
        });

        var ex = Assert.Throws<ArgumentException>(() =>
            CourtFilingController.BuildSubmissionFromCreateModel(model, validateForSubmission: true));

        Assert.Equal(nameof(model.DocumentsJson), ex.ParamName);
        Assert.Contains("401011", ex.Message);
        Assert.Contains("missing a valid file URL", ex.Message);
    }

    [Fact]
    public void BuildSubmission_DocumentMissingBlobUrl_AllowedWhenValidateForSubmissionFalse()
    {
        // Draft save uses validateForSubmission: false so users can save incomplete drafts.
        var model = NewMinimalModel();
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto
            {
                Role = "lead",
                DocumentCode = "401011",
                BlobUrl = null
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model, validateForSubmission: false);

        Assert.NotNull(sub.LeadDocument);
        Assert.Equal(string.Empty, sub.LeadDocument!.BinaryLocationUri);
        // Key regression guard: placeholder.pdf is NEVER substituted.
        Assert.DoesNotContain("placeholder.pdf", sub.LeadDocument.BinaryLocationUri);
    }

    [Fact]
    public void BuildSubmission_DocumentWithBlobUrl_PropagatesToBinaryLocationUri()
    {
        var model = NewMinimalModel();
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto
            {
                Role = "lead",
                DocumentCode = "401011",
                BlobUrl = "https://blob.example.com/doc1.pdf"
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        Assert.Equal("https://blob.example.com/doc1.pdf", sub.LeadDocument!.BinaryLocationUri);
    }

    // ── Audit C-2 Bug A regression gate ──
    // Catalog §3.4 "Audit C-2 root cause (corrected)" — the subsequent-filing classType switch
    // in BuildSubmissionFromCreateModel must have a `contact` branch, otherwise any
    // MetadataEntryDto with ClassType="contact" (baseline: CIV-SUB-003 FILING_PARTY_ADDRESS) is
    // silently dropped before reaching the builder. This test exposes the missing branch by
    // asserting that a round-tripped contact metadata item survives DTO → FilingMetadataValue
    // mapping with its contact fields populated.

    [Fact]
    public void BuildSubmission_SubsequentFiling_ContactMetadata_IsPreservedThroughDTOMapping_AuditC2_BugA()
    {
        var model = NewMinimalModel();
        model.IsSubsequentFiling = true;
        model.CaseDocketId = "24CV001234";
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto
            {
                Role = "lead",
                DocumentCode = "401011",
                BlobUrl = "https://blob.example.com/filing.pdf"
            }
        });
        model.MetadataJson = JsonSerializer.Serialize(new[]
        {
            new MetadataEntryDto
            {
                Code = "FILING_PARTY_ADDRESS",
                ClassType = "contact",
                Values = new List<MetadataValueDto>
                {
                    new()
                    {
                        Address1 = "12223 Davis St.",
                        City = "Sacramento",
                        State = "CA",
                        Zip = "95818",
                        Email = "filer@example.com"
                    }
                }
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        Assert.NotNull(sub.LeadDocument);
        var contactMeta = sub.LeadDocument!.MetadataValues
            .FirstOrDefault(mv => string.Equals(mv.ClassType, "contact", StringComparison.OrdinalIgnoreCase));

        Assert.True(contactMeta is not null,
            "§3.4 / audit C-2 Bug A: MetadataEntryDto with ClassType=\"contact\" was silently dropped during "
            + "DTO → FilingMetadataValue mapping. The subsequent-filing classType switch in "
            + "BuildSubmissionFromCreateModel is missing a `contact` branch — add one that maps "
            + "MetadataValueDto contact fields (Address1, Address2, City, State, Zip, Phone, Email) to "
            + "ContactValueData and appends a FilingMetadataValue with ClassType=\"contact\" to "
            + "sub.LeadDocument.MetadataValues.");

        Assert.Equal("FILING_PARTY_ADDRESS", contactMeta!.Code);
        Assert.NotNull(contactMeta.ContactValue);
        Assert.Equal("12223 Davis St.", contactMeta.ContactValue!.Address1);
        Assert.Equal("Sacramento", contactMeta.ContactValue.City);
        Assert.Equal("CA", contactMeta.ContactValue.State);
        Assert.Equal("95818", contactMeta.ContactValue.Zip);
        Assert.Equal("filer@example.com", contactMeta.ContactValue.Email);
    }

    // ── Audit C-1 regression gate — new-attorney silent failure (corrected 2026-04-22) ──
    // Catalog §3.3 "Audit C-1 root cause (corrected)" — the subsequent-filing classType switch
    // in BuildSubmissionFromCreateModel must have a new-data loop in the caseassignment/attorney
    // branch, analogous to the caseparticipant new-data loop. Without it, any MetadataValueDto
    // with IsNew=true under a caseAssignment metadata item is silently dropped before the
    // builder's (already-correct) new-data emission path runs.
    // Baseline scenarios affected: CIV-SUB-005, CIV-SUB-007, CIV-SUB-013, CIV-SUB-019,
    // FAM-SUB-001, FAM-SUB-006, PRO-SUB-001 (Association/Substitution of Attorney).

    [Fact]
    public void BuildSubmission_SubsequentFiling_NewAttorney_IsPreservedThroughDTOMapping_AuditC1()
    {
        var model = NewMinimalModel();
        model.IsSubsequentFiling = true;
        model.CaseDocketId = "24CV001234";
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto
            {
                Role = "lead",
                DocumentCode = "401011",
                BlobUrl = "https://blob.example.com/filing.pdf"
            }
        });
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

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        Assert.NotNull(sub.LeadDocument);
        var attorneyMeta = sub.LeadDocument!.MetadataValues
            .FirstOrDefault(mv =>
                string.Equals(mv.ClassType, "caseAssignment", StringComparison.OrdinalIgnoreCase)
                && string.Equals(mv.ValueRestriction, "new-data", StringComparison.OrdinalIgnoreCase));

        Assert.True(attorneyMeta is not null,
            "§3.3 / audit C-1: MetadataValueDto with IsNew=true under ClassType=\"caseAssignment\" was silently "
            + "dropped during DTO → FilingMetadataValue mapping. The subsequent-filing classType switch in "
            + "BuildSubmissionFromCreateModel's caseassignment/attorney branch only handles existing-data (filters "
            + "out v.IsNew); add a new-data loop analogous to the caseparticipant one that constructs a "
            + "FilingMetadataValue with ClassType=\"caseAssignment\", ValueRestriction=\"new-data\", and populated "
            + "CaseAssignmentValue (FirstName/MiddleName/LastName/BarNumber/FirmName, plus Contact when any contact "
            + "field is set, and AssignmentRole defaulting to \"ATT\").");

        Assert.Equal("NEW_ATTORNEY", attorneyMeta!.Code);
        Assert.NotNull(attorneyMeta.CaseAssignmentValue);
        Assert.Equal("David", attorneyMeta.CaseAssignmentValue!.FirstName);
        Assert.Equal("V.", attorneyMeta.CaseAssignmentValue.MiddleName);
        Assert.Equal("Skinner", attorneyMeta.CaseAssignmentValue.LastName);
        Assert.Equal("123426", attorneyMeta.CaseAssignmentValue.BarNumber);
        Assert.Equal("Skinner Law", attorneyMeta.CaseAssignmentValue.FirmName);
        // §3.3 evidence: "ATT" is the only AssignmentRole observed in baseline samples.
        Assert.Equal("ATT", attorneyMeta.CaseAssignmentValue.AssignmentRole);
    }

    [Fact]
    public void BuildSubmission_SubsequentFiling_NewAttorney_WithContactFields_PopulatesCaseAssignmentContact_AuditC1()
    {
        var model = NewMinimalModel();
        model.IsSubsequentFiling = true;
        model.CaseDocketId = "24CV001234";
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto { Role = "lead", DocumentCode = "401011", BlobUrl = "https://blob.example.com/f.pdf" }
        });
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
                        FirstName = "Jane",
                        LastName = "Lawyer",
                        BarNumber = "555555",
                        FirmName = "Lawyer LLP",
                        Address1 = "100 Main St",
                        City = "Madera",
                        State = "CA",
                        Zip = "93637",
                        TelephoneNumber = "5551234567",
                        Email = "jane@lawyerllp.com",
                    }
                }
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        var attorneyMeta = sub.LeadDocument!.MetadataValues
            .First(mv => string.Equals(mv.ValueRestriction, "new-data", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(attorneyMeta.CaseAssignmentValue?.Contact);
        Assert.Equal("100 Main St", attorneyMeta.CaseAssignmentValue!.Contact!.MailingAddress?.Address1);
        Assert.Equal("Madera", attorneyMeta.CaseAssignmentValue.Contact.MailingAddress?.City);
        Assert.Equal("5551234567", attorneyMeta.CaseAssignmentValue.Contact.PhoneNumber);
        Assert.Equal("jane@lawyerllp.com", attorneyMeta.CaseAssignmentValue.Contact.Email);
    }

    [Fact]
    public void BuildSubmission_SubsequentFiling_MixedExistingAndNewAttorneys_EmitsBothMetadataItems_AuditC1()
    {
        // Catalog §3.3 samples CIV-SUB-005 / CIV-SUB-019 routinely combine existing + new attorneys
        // in the same filing. The fix must preserve existing-data emission (unchanged) AND add
        // new-data emission alongside — not replace one with the other.
        var model = NewMinimalModel();
        model.IsSubsequentFiling = true;
        model.CaseDocketId = "24CV001234";
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto { Role = "lead", DocumentCode = "401011", BlobUrl = "https://blob.example.com/f.pdf" }
        });
        model.MetadataJson = JsonSerializer.Serialize(new[]
        {
            new MetadataEntryDto
            {
                Code = "FILING_ATTORNEY",
                ClassType = "caseAssignment",
                Values = new List<MetadataValueDto>
                {
                    new() { Id = "ATT-789", IsNew = false },
                    new() { IsNew = true, FirstName = "New", LastName = "Counsel", BarNumber = "999999" },
                }
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        var caseAssignmentMetas = sub.LeadDocument!.MetadataValues
            .Where(mv => string.Equals(mv.ClassType, "caseAssignment", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(2, caseAssignmentMetas.Count);
        var existing = caseAssignmentMetas.Single(mv => mv.ValueRestriction == "existing-data");
        var newData = caseAssignmentMetas.Single(mv => mv.ValueRestriction == "new-data");
        Assert.Contains("ATT-789", existing.IdReferences);
        Assert.Equal("999999", newData.CaseAssignmentValue?.BarNumber);
    }

    // ── Audit C-3 regression gate — tag emission + tagValue semantics (expanded 2026-04-22) ──
    // Catalog §4.0 "AUDIT C-3 SCOPE EXPANSION" documents three controller-side tag-handling bugs:
    //   C-3a — existing-data caseparticipant silently drops tags (meta.Values[*].Tags never read)
    //   C-3b — existing-data caseAssignment silently drops tags (same pattern)
    //   C-3c — new-data caseparticipant hardcodes TagValue="true" for every tag (wrong for all
    //           4 observed tag semantics: digit-boolean E_SERVICE/EFSP_FIRST_APPEARANCE_PAID
    //           should be "1"; string-enum FEE_EXEMPTION should be "GOVT_ENTITY"/"FEE_WAIVER";
    //           free-text EFSP_EMAIL should be an email address)
    // The builder is correct; bug is controller-side DTO → FilingMetadataValue mapping.

    [Fact]
    public void BuildSubmission_SubsequentFiling_ExistingCaseparticipant_WithFeeExemptionTag_EmitsTagWithEnumValue_AuditC3a()
    {
        // Baseline evidence: CIV-SUB-002 (GOVT_ENTITY) / CIV-SUB-003 (FEE_WAIVER) — single
        // existing-data caseParticipant with FEE_EXEMPTION tag carrying the enum value.
        var model = NewMinimalModel();
        model.IsSubsequentFiling = true;
        model.CaseDocketId = "24CV001234";
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto { Role = "lead", DocumentCode = "401011", BlobUrl = "https://blob.example.com/f.pdf" }
        });
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

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        var meta = sub.LeadDocument!.MetadataValues
            .First(mv => mv.ValueRestriction == "existing-data" && mv.ClassType.Equals("caseParticipant", StringComparison.OrdinalIgnoreCase));
        var tag = meta.AdditionalInfoTags.FirstOrDefault(t => t.TagType == "FEE_EXEMPTION");
        Assert.True(tag is not null,
            "§4.0 / audit C-3a: existing-data caseParticipant silently drops the FEE_EXEMPTION tag. "
            + "The controller's existing-data branch must read meta.Values[*].Tags and emit "
            + "AdditionalInfoTag entries with the correct per-tag-type tagValue (string-enum for "
            + "FEE_EXEMPTION, sourced from MetadataValueDto.FeeExemptionType).");
        Assert.Equal("GOVT_ENTITY", tag!.TagValue);
    }

    [Fact]
    public void BuildSubmission_SubsequentFiling_ExistingCaseparticipant_WithEServiceTag_EmitsDigitBooleanTagValue_AuditC3a()
    {
        // Baseline evidence: CIV-SUB-001 E_SERVICE="0" (opt-out) on existing-data caseparticipant.
        // Catalog §4.1: digit-boolean ("0"/"1"), NOT "true"/"false".
        var model = NewMinimalModel();
        model.IsSubsequentFiling = true;
        model.CaseDocketId = "24CV001234";
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto { Role = "lead", DocumentCode = "401011", BlobUrl = "https://blob.example.com/f.pdf" }
        });
        model.MetadataJson = JsonSerializer.Serialize(new[]
        {
            new MetadataEntryDto
            {
                Code = "FILING_PARTY",
                ClassType = "caseParticipant",
                Values = new List<MetadataValueDto>
                {
                    new() { Id = "P1", IsNew = false, Tags = new List<string> { "E_SERVICE" } }
                }
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        var meta = sub.LeadDocument!.MetadataValues
            .First(mv => mv.ValueRestriction == "existing-data");
        var tag = meta.AdditionalInfoTags.FirstOrDefault(t => t.TagType == "E_SERVICE");
        Assert.NotNull(tag);
        Assert.Equal("1", tag!.TagValue);
        Assert.NotEqual("true", tag.TagValue);
    }

    [Fact]
    public void BuildSubmission_SubsequentFiling_ExistingCaseparticipant_WithEfspEmailTag_EmitsEmailAsTagValue_AuditC3a()
    {
        // Baseline evidence: FAM-SUB-004 EFSP_EMAIL paired with E_SERVICE on existing-data
        // caseparticipant. Catalog §4.4: free-text (email address as tagValue).
        var model = NewMinimalModel();
        model.IsSubsequentFiling = true;
        model.CaseDocketId = "24FL1234";
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto { Role = "lead", DocumentCode = "401011", BlobUrl = "https://blob.example.com/f.pdf" }
        });
        model.MetadataJson = JsonSerializer.Serialize(new[]
        {
            new MetadataEntryDto
            {
                Code = "FILING_PARTY",
                ClassType = "caseParticipant",
                Values = new List<MetadataValueDto>
                {
                    new()
                    {
                        Id = "P1",
                        IsNew = false,
                        Tags = new List<string> { "EFSP_EMAIL" },
                        Email = "filer@example.com",
                    }
                }
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        var meta = sub.LeadDocument!.MetadataValues.First(mv => mv.ValueRestriction == "existing-data");
        var tag = meta.AdditionalInfoTags.FirstOrDefault(t => t.TagType == "EFSP_EMAIL");
        Assert.NotNull(tag);
        Assert.Equal("filer@example.com", tag!.TagValue);
    }

    [Fact]
    public void BuildSubmission_SubsequentFiling_NewParty_WithEServiceTag_EmitsDigitBooleanTagValue_AuditC3c()
    {
        // Pre-fix: controller hardcoded TagValue="true" for all tags. Post-fix: per-tag dispatch
        // gives "1" for E_SERVICE (digit-boolean per §4.1).
        var model = NewMinimalModel();
        model.IsSubsequentFiling = true;
        model.CaseDocketId = "24CV1234";
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto { Role = "lead", DocumentCode = "401011", BlobUrl = "https://blob.example.com/f.pdf" }
        });
        model.MetadataJson = JsonSerializer.Serialize(new[]
        {
            new MetadataEntryDto
            {
                Code = "FILING_PARTY",
                ClassType = "caseParticipant",
                Values = new List<MetadataValueDto>
                {
                    new()
                    {
                        IsNew = true,
                        PartyType = "PLAIN",
                        FirstName = "Alice",
                        LastName = "Smith",
                        Tags = new List<string> { "E_SERVICE" },
                    }
                }
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        var meta = sub.LeadDocument!.MetadataValues.First(mv => mv.ValueRestriction == "new-data");
        var tag = meta.AdditionalInfoTags.Single(t => t.TagType == "E_SERVICE");
        Assert.Equal("1", tag.TagValue);
        Assert.NotEqual("true", tag.TagValue);
    }

    [Fact]
    public void BuildSubmission_SubsequentFiling_NewParty_WithFeeExemptionTag_EmitsStringEnumTagValue_AuditC3c()
    {
        // New-party FEE_EXEMPTION with GOVT_ENTITY enum per §4.2.
        var model = NewMinimalModel();
        model.IsSubsequentFiling = true;
        model.CaseDocketId = "24CV1234";
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto { Role = "lead", DocumentCode = "401011", BlobUrl = "https://blob.example.com/f.pdf" }
        });
        model.MetadataJson = JsonSerializer.Serialize(new[]
        {
            new MetadataEntryDto
            {
                Code = "FILING_PARTY",
                ClassType = "caseParticipant",
                Values = new List<MetadataValueDto>
                {
                    new()
                    {
                        IsNew = true,
                        IsOrganization = true,
                        OrganizationName = "State of California",
                        FeeExemptionType = "GOVT_ENTITY",
                        Tags = new List<string> { "FEE_EXEMPTION" },
                    }
                }
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        var meta = sub.LeadDocument!.MetadataValues.First(mv => mv.ValueRestriction == "new-data");
        var tag = meta.AdditionalInfoTags.Single(t => t.TagType == "FEE_EXEMPTION");
        Assert.Equal("GOVT_ENTITY", tag.TagValue);
    }

    [Fact]
    public void BuildSubmission_SubsequentFiling_NewParty_WithFeeExemptionTagButNoExemptionValue_SkipsEmission_AuditC3c()
    {
        // Defensive: if the UI passes FEE_EXEMPTION but no FeeExemptionType, the controller
        // cannot determine GOVT_ENTITY vs FEE_WAIVER. Per §2.6.2 fail-closed stance, skip
        // emission rather than emitting a malformed tag (previously would have emitted
        // tagValue="true" which JTI would reject).
        var model = NewMinimalModel();
        model.IsSubsequentFiling = true;
        model.CaseDocketId = "24CV1234";
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto { Role = "lead", DocumentCode = "401011", BlobUrl = "https://blob.example.com/f.pdf" }
        });
        model.MetadataJson = JsonSerializer.Serialize(new[]
        {
            new MetadataEntryDto
            {
                Code = "FILING_PARTY",
                ClassType = "caseParticipant",
                Values = new List<MetadataValueDto>
                {
                    new()
                    {
                        IsNew = true,
                        FirstName = "Alice",
                        LastName = "Smith",
                        Tags = new List<string> { "FEE_EXEMPTION" },
                        // FeeExemptionType intentionally not set.
                    }
                }
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        var meta = sub.LeadDocument!.MetadataValues.First(mv => mv.ValueRestriction == "new-data");
        Assert.DoesNotContain(meta.AdditionalInfoTags, t => t.TagType == "FEE_EXEMPTION");
    }

    [Fact]
    public void BuildSubmission_SubsequentFiling_ExistingCaseAssignment_WithEServiceTag_EmitsTagAtAll_AuditC3b()
    {
        // C-3b consistency fix — catalog §3.3 notes tags aren't observed on caseAssignment in
        // baseline (tags route through caseParticipant), but the controller's existing-data
        // caseAssignment branch still silently drops tags if the UI passes them, which is a
        // forward-compatibility bug (per §2.6.2, unknown-in-baseline doesn't mean forbidden).
        var model = NewMinimalModel();
        model.IsSubsequentFiling = true;
        model.CaseDocketId = "24CV1234";
        model.DocumentsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentEntryDto { Role = "lead", DocumentCode = "401011", BlobUrl = "https://blob.example.com/f.pdf" }
        });
        model.MetadataJson = JsonSerializer.Serialize(new[]
        {
            new MetadataEntryDto
            {
                Code = "FILING_ATTORNEY",
                ClassType = "caseAssignment",
                Values = new List<MetadataValueDto>
                {
                    new() { Id = "ATT-001", IsNew = false, Tags = new List<string> { "E_SERVICE" } }
                }
            }
        });

        var sub = CourtFilingController.BuildSubmissionFromCreateModel(model);

        var meta = sub.LeadDocument!.MetadataValues
            .First(mv => mv.ValueRestriction == "existing-data" && mv.ClassType.Equals("caseAssignment", StringComparison.OrdinalIgnoreCase));
        var tag = meta.AdditionalInfoTags.FirstOrDefault(t => t.TagType == "E_SERVICE");
        Assert.True(tag is not null,
            "§4.0 / audit C-3b: existing-data caseAssignment silently drops tags. Consistency fix "
            + "symmetric with C-3a — the controller must read meta.Values[*].Tags in the "
            + "caseassignment/attorney branch too.");
        Assert.Equal("1", tag!.TagValue);
    }

    // ── Test helpers ──

    private static CreateCaseModel NewMinimalModel() => new()
    {
        CourtId = "test-court",
        CaseTypeCode = "CU",
        CaseCategoryCode = "CIVIL"
    };
}
