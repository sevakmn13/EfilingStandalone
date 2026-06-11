using EFiling.Core.Models;

namespace EFiling.Nop.Models;

/// <summary>
/// View model for the Filings list page.
/// </summary>
public class FilingListModel
{
    public string CourtId { get; set; } = string.Empty;
    public string? CaseDocketId { get; set; }
    public string? FilingStatus { get; set; }
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public List<FilingListItem> Filings { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
