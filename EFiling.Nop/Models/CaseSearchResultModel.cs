using EFiling.Core.Models;

namespace EFiling.Nop.Models;

/// <summary>
/// View model for case search results page.
/// </summary>
public class CaseSearchResultModel
{
    public CaseSearchModel Search { get; set; } = new();
    public List<CaseInfo> Cases { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
