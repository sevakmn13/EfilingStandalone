namespace EFiling.Nop.Models;

/// <summary>
/// DTO for attorney entries deserialized from the CreateCase form's AttorneysJson field.
/// </summary>
public class AttorneyEntryDto
{
    /// <summary>Primary ID of existing attorney on case (for subsequent filings).</summary>
    public string? PrimaryId { get; set; }
    /// <summary>Actual metadata code from document definition (e.g., FILING_ATTORNEY).</summary>
    public string? MetadataCode { get; set; }
    /// <summary>Side indicator (filing/opposing).</summary>
    public string? Side { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Suffix { get; set; }
    public string? FirmName { get; set; }
    public string? BarNumber { get; set; }
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
}
