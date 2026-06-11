namespace EFiling.Core.Models;

/// <summary>
/// Criteria for searching cases via GetCaseList.
/// </summary>
public class CaseSearchCriteria
{
    /// <summary>Case docket number to search for.</summary>
    public string? CaseDocketId { get; set; }

    /// <summary>Party name search term (full name, legacy — use FirstName/LastName for individual search).</summary>
    public string? PartySearchTerm { get; set; }

    /// <summary>Party first name for individual party search.</summary>
    public string? FirstName { get; set; }

    /// <summary>Party last name for individual party search.</summary>
    public string? LastName { get; set; }

    /// <summary>Organization name for business party search.</summary>
    public string? OrganizationName { get; set; }

    /// <summary>Case title to search for.</summary>
    public string? CaseTitle { get; set; }

    /// <summary>Case category code to filter by.</summary>
    public string? CaseCategoryCode { get; set; }

    /// <summary>Party role code filter.</summary>
    public string? PartyRoleCode { get; set; }

    /// <summary>Page size for paginated results.</summary>
    public int? PageSize { get; set; }

    /// <summary>Offset for paginated results.</summary>
    public int? Offset { get; set; }
}
