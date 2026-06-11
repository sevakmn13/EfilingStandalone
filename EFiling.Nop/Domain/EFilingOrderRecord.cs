using Nop.Core;

namespace EFiling.Nop.Domain;

/// <summary>
/// Links a nopCommerce Order to court filing data.
/// One record per submitted filing. Updated by NFRC callbacks.
/// </summary>
public class EFilingOrderRecord : BaseEntity
{
    /// <summary>FK to nopCommerce Order.Id.</summary>
    public int OrderId { get; set; }

    /// <summary>Our internal filing reference ID (GUID, sent as FILING_ASSEMBLY_MDE).</summary>
    public string EfspReferenceId { get; set; } = string.Empty;

    /// <summary>EFM-assigned filing reference ID (from MessageReceipt / NFRC).</summary>
    public string? EfmReferenceId { get; set; }

    /// <summary>Court ID this filing was submitted to (e.g., "madera").</summary>
    public string CourtId { get; set; } = string.Empty;

    /// <summary>Court-assigned case number (from NFRC, null until accepted).</summary>
    public string? CaseNumber { get; set; }

    /// <summary>Case title (e.g., "Smith v. Doe") from NFRC.</summary>
    public string? CaseTitle { get; set; }

    /// <summary>Case docket ID from NFRC (public case number).</summary>
    public string? CaseDocketId { get; set; }

    /// <summary>
    /// Court filing status string: RECEIVED_UNDER_REVIEW, ACCEPTED, PARTIALLY_ACCEPTED, REJECTED.
    /// This is the user-facing status. nopCommerce OrderStatus is for internal pipeline only.
    /// </summary>
    public string FilingStatus { get; set; } = "RECEIVED_UNDER_REVIEW";

    /// <summary>Filing type: "Initial" or "Subsequent".</summary>
    public string FilingType { get; set; } = "Initial";

    /// <summary>Human-readable case category (e.g., "Contractual Fraud"). Populated at submission.</summary>
    public string? CaseCategoryText { get; set; }

    /// <summary>Human-readable case type (e.g., "Civil Unlimited"). Populated at submission.</summary>
    public string? CaseTypeText { get; set; }

    /// <summary>Timestamp of the last NFRC received.</summary>
    public DateTime? LastNfrcDateUtc { get; set; }

    /// <summary>Error text from submission or NFRC rejection.</summary>
    public string? ErrorText { get; set; }

    /// <summary>Snapshot of the submitted FilingSubmission as JSON (for refile support).</summary>
    public string? SubmissionJson { get; set; }

    /// <summary>Number of NFRCs received for this filing.</summary>
    public int NfrcCount { get; set; }

    /// <summary>URL to the receipt PDF (from NFRC #2).</summary>
    public string? ReceiptUrl { get; set; }

    /// <summary>Comma-separated list of additional email addresses to notify on status changes.</summary>
    public string? NotificationEmails { get; set; }

    /// <summary>When this record was created (UTC).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When this record was last updated (UTC).</summary>
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
