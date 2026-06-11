using System.Collections.Generic;

namespace EFiling.Nop.Models;

/// <summary>
/// DTO for party entries deserialized from the CreateCase form's PartiesJson field.
/// </summary>
public class PartyEntryDto
{
    public string? Side { get; set; }
    public string? PartyType { get; set; }
    public string? PrimaryId { get; set; }
    /// <summary>Actual metadata code from document definition (e.g., FILING_PARTY, FILED_BY).</summary>
    public string? MetadataCode { get; set; }
    public bool IsOrganization { get; set; }
    public bool FirstAppearancePaid { get; set; }
    public bool GovernmentExempt { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Suffix { get; set; }
    public string? OrganizationName { get; set; }
    public bool SelfRepresented { get; set; }
    public string? InterpreterLanguage { get; set; }
    public string? FeeExemptionType { get; set; }
    public int LeadAttorneyIdx { get; set; } = -1;
    public bool EService { get; set; }
    public string? AddressType { get; set; }
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
    public string? PhoneType { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public List<AlternateNameEntryDto>? Akas { get; set; }
}

/// <summary>
/// DTO for AKA/DBA alternate name entries within a party.
/// </summary>
public class AlternateNameEntryDto
{
    public string? Type { get; set; }
    public bool IsOrganization { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Suffix { get; set; }
    public string? OrganizationName { get; set; }
}
