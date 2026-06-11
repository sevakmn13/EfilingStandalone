namespace EFiling.Nop.Models;

/// <summary>
/// View model for case search form.
/// </summary>
public class CaseSearchModel
{
    public string CourtId { get; set; } = string.Empty;

    /// <summary>Search mode: CaseNumber, PartyIndividual, PartyBusiness, Title, Category.</summary>
    public string SearchMode { get; set; } = "CaseNumber";

    public string? CaseDocketId { get; set; }
    public string? PartyName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? OrganizationName { get; set; }
    public string? CaseTitle { get; set; }

    /// <summary>
    /// Case category code (or human-readable category text) for category-based browsing.
    /// Wired through `CaseSearchCriteria.CaseCategoryCode` →
    /// `SoapEnvelopeBuilder.BuildGetCaseListRequest` → `<ns1:CaseCategoryText>` SOAP element.
    /// Added at Step #57 as part of the category-search probe to validate that
    /// Madera CMS honors the filter; can be promoted to a full UI dropdown if the probe succeeds.
    /// </summary>
    public string? CaseCategoryCode { get; set; }
}
