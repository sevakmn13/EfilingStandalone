using EFiling.Core.Enums;

namespace EFiling.Core.Models;

/// <summary>
/// A filing entry returned by GetFilingList.
/// </summary>
public class FilingListItem
{
    public string? FilingId { get; set; }
    public string? CaseTitle { get; set; }
    public string? CaseTrackingId { get; set; }
    public string? CaseDocketId { get; set; }
    public string? SubmitterId { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public FilingStatus Status { get; set; } = FilingStatus.Unknown;
    public string? LeadDocumentDescription { get; set; }
}

/// <summary>
/// Criteria for filtering the filing list via GetFilingList.
/// </summary>
public class FilingListCriteria
{
    /// <summary>Filter by case docket ID.</summary>
    public string? CaseDocketId { get; set; }

    /// <summary>Filter by filing type (INITIAL, SUBSEQUENT).</summary>
    public FilingType? FilingType { get; set; }

    /// <summary>Filter by case type code.</summary>
    public string? CaseType { get; set; }

    /// <summary>Filter by filing status.</summary>
    public FilingStatus? Status { get; set; }

    /// <summary>Start date range filter.</summary>
    public DateTime? FromDate { get; set; }

    /// <summary>End date range filter.</summary>
    public DateTime? ToDate { get; set; }
}
