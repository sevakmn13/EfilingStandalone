using EFiling.Core.Models;

namespace EFiling.Nop.Models;

/// <summary>
/// View model for the main e-filing index page (tabs: Create Case, Search, Filings, Drafts).
/// </summary>
public class EFilingIndexModel
{
    public List<CourtConfiguration> Courts { get; set; } = new();
}
