using EFiling.Core.Enums;

namespace EFiling.Core.Models;

/// <summary>
/// Complete filing submission data used by ReviewFiling and GetFeesCalculation.
/// </summary>
public class FilingSubmission
{
    /// <summary>Whether this is a case initiation or subsequent filing.</summary>
    public FilingType FilingType { get; set; }

    /// <summary>EFSP's own unique filing reference ID.</summary>
    public string EfspReferenceId { get; set; } = string.Empty;

    /// <summary>Optional username of the submitting user (audit trail).</summary>
    public string? SubmitterUsername { get; set; }

    /// <summary>Optional message to the clerk.</summary>
    public string? MessageToClerk { get; set; }

    // ─── Case Initiation Fields ─────────────────────────────────────

    /// <summary>Case type code (e.g., "CU", "LC", "SC"). Required for case initiation.</summary>
    public string? CaseTypeCode { get; set; }

    /// <summary>Case category code (e.g., "3201", "0901"). Required for case initiation.</summary>
    public string? CaseCategoryCode { get; set; }

    /// <summary>Jurisdictional grounds code (e.g., "U10K", "O10K", "O25K").</summary>
    public string? JurisdictionalGroundsCode { get; set; }

    /// <summary>Amount in controversy (optional).</summary>
    public decimal? AmountInControversy { get; set; }

    /// <summary>Court location code (ParentLocationCode).</summary>
    public string? LocationCode { get; set; }

    /// <summary>Court location name.</summary>
    public string? LocationName { get; set; }

    /// <summary>Incident/premise zip code for court location lookup.</summary>
    public string? IncidentZipCode { get; set; }

    /// <summary>Whether this is a complex litigation case.</summary>
    public bool ComplexLitigation { get; set; }

    /// <summary>Whether this is a class action case (base CivilCaseType ClassActionIndicator).</summary>
    public bool ClassAction { get; set; }

    /// <summary>Whether this is an asbestos case.</summary>
    public bool Asbestos { get; set; }

    /// <summary>Whether this case involves the California Environmental Quality Act.</summary>
    public bool CaliforniaEnvironmentalQualityAct { get; set; }

    /// <summary>
    /// Whether the case is conditionally sealed. Tri-state semantics (Audit F-1 2026-04-22):
    /// <list type="bullet">
    ///   <item><c>null</c> — element was not set by the producer; builder OMITS the
    ///         <c>&lt;civext:conditionallySealed&gt;</c> element entirely.</item>
    ///   <item><c>false</c> — element was explicitly set to false; builder EMITS
    ///         <c>&lt;civext:conditionallySealed&gt;false&lt;/&gt;</c>.</item>
    ///   <item><c>true</c> — element was explicitly set to true; builder EMITS
    ///         <c>&lt;civext:conditionallySealed&gt;true&lt;/&gt;</c>.</item>
    /// </list>
    /// The distinction matters because baseline samples are inconsistent: CIV-INI-001 omits
    /// the element entirely while CIV-INI-007 emits it with value "false". Round-tripping
    /// both baselines requires preserving the source-was-set bit. The <c>FilingConfidentiality</c>
    /// Indicator element separately always emits (defaulting null → false).
    /// </summary>
    public bool? ConditionallySealed { get; set; }

    /// <summary>Parties on the case (case initiation only).</summary>
    public List<FilingParty> Parties { get; set; } = new();

    /// <summary>Party-to-party associations (e.g., REPRESENTEDBY).</summary>
    public List<PartyAssociation> PartyAssociations { get; set; } = new();

    /// <summary>Party-to-document associations (FILEDBY, REFERS_TO).</summary>
    public List<PartyDocumentAssociation> PartyDocumentAssociations { get; set; } = new();

    // ─── Subsequent Filing Fields ───────────────────────────────────

    /// <summary>Existing case docket ID (subsequent filing only).</summary>
    public string? CaseDocketId { get; set; }

    /// <summary>Case tracking ID from GetCase (subsequent filing only).</summary>
    public string? CaseTrackingId { get; set; }

    /// <summary>Complaint/sub-case ID (subsequent filing only).</summary>
    public string? ComplaintId { get; set; }

    // ─── Documents ──────────────────────────────────────────────────

    /// <summary>Lead document (required — exactly one).</summary>
    public FilingDocument? LeadDocument { get; set; }

    /// <summary>Connected/supporting documents (zero or more).</summary>
    public List<FilingDocument> ConnectedDocuments { get; set; } = new();

    // ─── Payment ────────────────────────────────────────────────────

    /// <summary>Payment information for the filing.</summary>
    public FilingPayment? Payment { get; set; }

    // ─── Court-Specific Extensions ──────────────────────────────────

    /// <summary>Premise address (Unlawful Detainer cases).</summary>
    public StructuredAddress? PremiseAddress { get; set; }

    /// <summary>Citation info (Parking Appeals / Admin Hearings).</summary>
    public CitationInfo? Citation { get; set; }

    /// <summary>Whether this is a no-fee case.</summary>
    public bool NoFeeCase { get; set; }

    /// <summary>No-fee case section text.</summary>
    public string? NoFeeCaseSection { get; set; }

    /// <summary>Number of parcels (Eminent Domain).</summary>
    public int? NumberOfParcels { get; set; }

    /// <summary>Case special status codes (e.g., "UDCOV19").</summary>
    public List<string> SpecialStatusCodes { get; set; } = new();
}

/// <summary>
/// A party included in a case initiation filing.
/// </summary>
public class FilingParty
{
    /// <summary>Local reference ID for associations (e.g., "filedBy0", "attorney0").</summary>
    public string ReferenceId { get; set; } = string.Empty;

    /// <summary>Existing party primary ID from GetCase (subsequent filing only).</summary>
    public string? PrimaryId { get; set; }

    /// <summary>Party role code (e.g., "PLAIN", "DEF", "PET", "ATT").</summary>
    public string RoleCode { get; set; } = string.Empty;

    /// <summary>True if party is an organization.</summary>
    public bool IsOrganization { get; set; }

    /// <summary>Person's first name.</summary>
    public string? FirstName { get; set; }

    /// <summary>Person's middle name.</summary>
    public string? MiddleName { get; set; }

    /// <summary>Person's last name.</summary>
    public string? LastName { get; set; }

    /// <summary>Person's name suffix.</summary>
    public string? NameSuffix { get; set; }

    /// <summary>Organization name.</summary>
    public string? OrganizationName { get; set; }

    /// <summary>Attorney bar number (if role is ATT).</summary>
    public string? BarNumber { get; set; }

    /// <summary>Contact information.</summary>
    public ContactInfo? Contact { get; set; }

    /// <summary>Fee exemption request type (FEE_WAIVER, GOVT_ENTITY).</summary>
    public string? FeeExemptionRequestType { get; set; }

    /// <summary>Whether first appearance fees have been paid for this party.</summary>
    public bool FirstAppearancePaid { get; set; }

    /// <summary>Whether this party is government-exempt from fees.</summary>
    public bool GovernmentExempt { get; set; }

    /// <summary>Interpreter language code.</summary>
    public string? InterpreterLanguage { get; set; }

    /// <summary>Whether the party consents to electronic service.</summary>
    public bool EService { get; set; }

    /// <summary>Party sub-types (e.g., "GAL", "IP" for Minor, Guardian Ad Litem).</summary>
    public List<string> PartySubTypes { get; set; } = new();

    /// <summary>AKA / alternate names.</summary>
    public List<AlternateName> AlternateNames { get; set; } = new();

    /// <summary>Date of birth (required for some case types).</summary>
    public DateTime? DateOfBirth { get; set; }

    /// <summary>Gender (required for some case types).</summary>
    public string? Gender { get; set; }
}

/// <summary>
/// An alternate name (AKA, DBA, FKA, etc.) for a party.
/// </summary>
public class AlternateName
{
    /// <summary>Type: AKA, Alias, DBA, FDBA, FKA.</summary>
    public string Type { get; set; } = "AKA";

    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }

    /// <summary>Person name suffix (Jr., Sr., III, etc.) for person-shaped AKAs.</summary>
    /// <remarks>
    /// Pre-2026-05-17 this field didn't exist on the wire model, so AKA suffix was silently
    /// dropped at every layer (JS captured it via .aka-suffix → DTO held it on
    /// AlternateNameEntryDto.Suffix → both mappers ignored it → wire model + builder had no
    /// hole to put it in). Added now to close the silent-drop chain end-to-end. Schema-aligned
    /// with caseParticipant.person.personNameSuffixText.
    /// </remarks>
    public string? NameSuffix { get; set; }

    /// <summary>Organization name for org AKAs.</summary>
    public string? OrganizationName { get; set; }
}

/// <summary>
/// Contact information for a party.
/// </summary>
public class ContactInfo
{
    public StructuredAddress? MailingAddress { get; set; }
    public string? PhoneNumber { get; set; }
    public string? PhoneType { get; set; }
    public string? Email { get; set; }
}

/// <summary>
/// Structured address.
/// </summary>
public class StructuredAddress
{
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
    public string? AddressType { get; set; }
}

/// <summary>
/// Citation information (Parking Appeals / Admin Hearings).
/// </summary>
public class CitationInfo
{
    public string? CitationId { get; set; }
    public DateTime? ActivityDate { get; set; }
}

/// <summary>
/// Association between two parties (e.g., party represented by attorney).
/// </summary>
public class PartyAssociation
{
    /// <summary>Association type (e.g., "REPRESENTEDBY").</summary>
    public string AssociationType { get; set; } = string.Empty;

    /// <summary>Source party reference ID.</summary>
    public string ParticipantRef { get; set; } = string.Empty;

    /// <summary>Related party reference ID.</summary>
    public string RelatedParticipantRef { get; set; } = string.Empty;
}

/// <summary>
/// Association between a party and a document (FILEDBY, REFERS_TO).
/// </summary>
public class PartyDocumentAssociation
{
    /// <summary>Association type: "FILEDBY" or "REFERS_TO".</summary>
    public string AssociationType { get; set; } = string.Empty;

    /// <summary>Party reference ID.</summary>
    public string ParticipantRef { get; set; } = string.Empty;

    /// <summary>Document reference ID.</summary>
    public string DocumentRef { get; set; } = string.Empty;
}
