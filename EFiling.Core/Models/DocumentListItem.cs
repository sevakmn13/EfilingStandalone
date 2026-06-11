namespace EFiling.Core.Models;

/// <summary>
/// A document type from the court's document list endpoint.
/// Contains metadata describing what fields the UI must render for this document type.
/// </summary>
public class DocumentListItem
{
    /// <summary>Document code (e.g., "COM040", "RES010").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name (e.g., "Complaint").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this document requires a sub-case/complaint selection.</summary>
    public bool EfmRequiresSubCase { get; set; }

    /// <summary>Metadata items that define what fields the EFSP must collect for this document.</summary>
    public List<DocumentMetadataItem> MetadataItems { get; set; } = new();

    /// <summary>Case types this document is available for.</summary>
    public List<string> CaseTypes { get; set; } = new();

    /// <summary>Case sub-types this document is available for.</summary>
    public List<string> CaseSubTypes { get; set; } = new();

    /// <summary>Case categories this document is available for.</summary>
    public List<string> CaseCategories { get; set; } = new();

    /// <summary>Form groups this document belongs to (EFCI_LEAD, EFCI, EF_LEAD, etc.).</summary>
    public List<string> FormGroups { get; set; } = new();
}

/// <summary>
/// A metadata field that a document type requires.
/// Drives dynamic UI rendering (dropdowns, text fields, party selectors, etc.).
/// </summary>
public class DocumentMetadataItem
{
    /// <summary>Metadata code (e.g., "FILED_BY", "AS_TO", "PAYMENT_OPTION").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name for the UI.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description / help text.</summary>
    public string? Description { get; set; }

    /// <summary>Whether this metadata is required.</summary>
    public bool Required { get; set; }

    /// <summary>Whether multiple values can be selected.</summary>
    public bool Multiple { get; set; }

    /// <summary>
    /// Data type: text, number, currency, boolean, date, email, action,
    /// caseParticipant, attorney, address, codeList, document, scheduledEvent,
    /// judgment, caseSpecialStatus, crsReceiptNumber, contact, caseAssignment, relatedCase.
    /// </summary>
    public string ClassType { get; set; } = string.Empty;

    /// <summary>Filter to apply (e.g., "caseParticipant-all-parties-except-attorney").</summary>
    public string? Filter { get; set; }

    /// <summary>Sub-type (e.g., "filed-by", "refers-to").</summary>
    public string? SubType { get; set; }

    /// <summary>Value restriction: "new-data" (user creates) or "existing-data" (from GetCase).</summary>
    public string? ValueRestriction { get; set; }

    /// <summary>Additional info tags (boolean questions like fee waiver, sealed, etc.).</summary>
    public List<string> AdditionalInfoTags { get; set; } = new();

    /// <summary>Code list name to use for codeList classType (e.g., "PAYMENT_OPTION").</summary>
    public string? CodeList { get; set; }

    /// <summary>Party types this metadata applies to.</summary>
    public List<string> PartyTypes { get; set; } = new();
}
