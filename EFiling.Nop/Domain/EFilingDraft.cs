using Nop.Core;

namespace EFiling.Nop.Domain;

/// <summary>
/// Draft filing entity stored in nopCommerce DB.
/// Serializes the full FilingSubmission as JSON for easy resume/edit/submit.
/// </summary>
public class EFilingDraft : BaseEntity
{
    /// <summary>nopCommerce Customer ID (FK to Customer table).</summary>
    public int CustomerId { get; set; }

    /// <summary>Court ID this draft is for.</summary>
    public string CourtId { get; set; } = string.Empty;

    /// <summary>Existing case docket ID (null for case initiation, set for subsequent).</summary>
    public string? CaseDocketId { get; set; }

    /// <summary>Filing type: "Initial" or "Subsequent".</summary>
    public string FilingType { get; set; } = "Initial";

    /// <summary>Serialized FilingSubmission as JSON.</summary>
    public string SubmissionJson { get; set; } = "{}";

    /// <summary>User-friendly display name (e.g., "Smith v. Acme - Civil Complaint").</summary>
    public string? DisplayName { get; set; }

    /// <summary>Schema version for future JSON migration if model changes.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>When the draft was created (UTC).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When the draft was last updated (UTC).</summary>
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Whether this draft has been submitted (soft-delete flag).</summary>
    public bool IsSubmitted { get; set; }

    /// <summary>EFM reference ID after successful submission.</summary>
    public string? EfmReferenceId { get; set; }
}
