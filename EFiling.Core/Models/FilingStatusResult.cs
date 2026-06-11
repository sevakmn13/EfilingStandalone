using EFiling.Core.Enums;

namespace EFiling.Core.Models;

/// <summary>
/// Result of a GetFilingStatus or NFRC notification.
/// </summary>
public class FilingStatusResult
{
    /// <summary>EFM-assigned filing reference ID.</summary>
    public string? EfmReferenceId { get; set; }

    /// <summary>EFSP's own filing reference ID.</summary>
    public string? EfspReferenceId { get; set; }

    /// <summary>Overall filing status.</summary>
    public FilingStatus FilingStatus { get; set; } = FilingStatus.Unknown;

    /// <summary>Case docket ID (available after acceptance).</summary>
    public string? CaseDocketId { get; set; }

    /// <summary>Case tracking ID (available after acceptance).</summary>
    public string? CaseTrackingId { get; set; }

    /// <summary>
    /// Case name / caption (e.g., "Smith v. Doe"). Madera returns "TBD" until the clerk
    /// fills in the caption. Populated from <c>&lt;rsrm:CaseName&gt;</c> in GetRecordingStatus
    /// responses. Prior to Track B.2.post, this value was incorrectly overloaded into
    /// <see cref="CaseTrackingId"/>.
    /// </summary>
    public string? CaseName { get; set; }

    /// <summary>Per-document statuses.</summary>
    public List<DocumentStatusItem> Documents { get; set; } = new();

    /// <summary>Filing status reasons (for rejections).</summary>
    public List<FilingStatusReason> Reasons { get; set; } = new();

    /// <summary>Raw XML for debugging.</summary>
    public string? RawXml { get; set; }
}

/// <summary>
/// Status of an individual document within a filing.
/// </summary>
public class DocumentStatusItem
{
    /// <summary>Document description / code.</summary>
    public string? DocumentDescription { get; set; }

    /// <summary>Document status.</summary>
    public DocumentStatus Status { get; set; } = DocumentStatus.Unknown;

    /// <summary>Document disposition type (GRA, DEN, ORD, OAI).</summary>
    public string? DispositionType { get; set; }

    /// <summary>URL to download conformed copy (7-day expiry).</summary>
    public string? DownloadUrl { get; set; }

    /// <summary>Status reasons for this document.</summary>
    public List<FilingStatusReason> Reasons { get; set; } = new();
}

/// <summary>
/// A reason for a filing/document status (typically rejection reasons).
/// </summary>
public class FilingStatusReason
{
    public string? ReasonCode { get; set; }
    public string? ReasonText { get; set; }
    public string? Memo { get; set; }
}
