namespace EFiling.Nop.Models;

/// <summary>
/// View model for the Create Case (case initiation) form.
/// </summary>
public class CreateCaseModel
{
    public string CourtId { get; set; } = string.Empty;
    public string? CaseTypeCode { get; set; }
    public string? CaseCategoryCode { get; set; }
    public string? JurisdictionalGroundsCode { get; set; }
    public string? LocationCode { get; set; }
    public string? LocationName { get; set; }
    public string? IncidentZipCode { get; set; }

    // Parties (JSON-serialized from dynamic UI — filing and opposing)
    public string? PartiesJson { get; set; }

    // Attorneys (JSON-serialized from dynamic UI)
    public string? AttorneysJson { get; set; }

    // All metadata sections (JSON-serialized, includes all classTypes with actual codes)
    public string? MetadataJson { get; set; }

    // Documents (JSON-serialized from dynamic UI — lead + connected)
    public string? DocumentsJson { get; set; }

    // Legacy (kept for backward compat; prefer DocumentsJson)
    public string? LeadDocumentCode { get; set; }
    public string? LeadDocumentUrl { get; set; }

    // Optional
    public decimal? AmountInControversy { get; set; }
    public decimal? DemandAmount { get; set; }
    public string? MessageToClerk { get; set; }
    public string? CaseSubType { get; set; }
    public bool ComplexLitigation { get; set; }
    public bool ClassAction { get; set; }
    public bool Asbestos { get; set; }
    public bool CaliforniaEnvironmentalQualityAct { get; set; }
    public bool ConditionallySealed { get; set; }

    // ─── Subsequent Filing Fields ────────────────────────────────────
    /// <summary>True if this is a subsequent filing on an existing case.</summary>
    public bool IsSubsequentFiling { get; set; }

    /// <summary>Case docket ID (case number) for subsequent filings.</summary>
    public string? CaseDocketId { get; set; }

    /// <summary>Case tracking ID (internal court ID) for subsequent filings.</summary>
    public string? CaseTrackingId { get; set; }

    /// <summary>Complaint/sub-case ID for subsequent filings.</summary>
    public string? ComplaintId { get; set; }

    /// <summary>Case title (e.g., "Smith v. Doe") for subsequent filings.</summary>
    public string? CaseTitle { get; set; }

    /// <summary>Draft ID if resuming from a saved draft.</summary>
    public int? DraftId { get; set; }

    // ─── Payment ─────────────────────────────────────────────────────
    /// <summary>
    /// nopCommerce stored-payment-method ID (Braintree vault token), selected by the
    /// user from their saved cards. Posted by the SF form (and, post-P4, by the unified
    /// CC form-post path); the CC AJAX path passes the same value via the
    /// <c>savedPaymentMethodId</c> JSON field instead.
    /// </summary>
    public int? SelectedPaymentMethodId { get; set; }
}
