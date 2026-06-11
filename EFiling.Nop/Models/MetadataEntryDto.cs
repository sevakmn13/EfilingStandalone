using System.Collections.Generic;

namespace EFiling.Nop.Models;

/// <summary>
/// DTO for metadata entries deserialized from MetadataJson.
/// Captures all classTypes with actual metadata codes from document definition.
/// </summary>
public class MetadataEntryDto
{
    /// <summary>Actual metadata code from document definition (e.g., FILING_PARTY, FILING_ATTORNEY).</summary>
    public string? Code { get; set; }
    
    /// <summary>ClassType (caseParticipant, caseAssignment, contact, codeList, currency, date, boolean, text).</summary>
    public string? ClassType { get; set; }
    
    /// <summary>Sub-type (e.g., "filed-by", "refers-to").</summary>
    public string? SubType { get; set; }
    
    /// <summary>Value restriction ("new-data" or "existing-data").</summary>
    public string? ValueRestriction { get; set; }
    
    /// <summary>Values for this metadata item (used by SubsequentFiling collectMetadataJson).</summary>
    public List<MetadataValueDto>? Values { get; set; }
    
    /// <summary>Single value (used by initial filing document metadata).</summary>
    public object? Value { get; set; }
    
    /// <summary>Additional info tags (fee waiver, self-rep, etc.) - string list format.</summary>
    public List<string>? Tags { get; set; }
    
    /// <summary>Additional info tags - structured format for initial filing.</summary>
    public List<AdditionalInfoTagDto>? AdditionalInfoTags { get; set; }
}

/// <summary>
/// DTO for a single value within a metadata entry.
/// </summary>
public class MetadataValueDto
{
    // For caseParticipant / caseAssignment (existing-data)
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Role { get; set; }
    public List<string>? Tags { get; set; }
    
    // For new party/attorney
    public bool IsNew { get; set; }
    public string? PartyType { get; set; }
    public bool IsOrganization { get; set; }
    public string? OrganizationName { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Suffix { get; set; }
    public bool SelfRepresented { get; set; }
    public string? LeadAttorneyId { get; set; }
    public string? InterpreterLanguage { get; set; }
    public string? FeeExemptionType { get; set; }
    
    // For new attorney
    public string? BarNumber { get; set; }
    public string? FirmName { get; set; }
    
    // For contact — full schema (10 fields per JtiClassTypeSchema.json#/classTypes/contact).
    // Property names align 1:1 with schema field names (no normalization layer between JS and DTO).
    // The wire layer (ContactValueData / JTI XML <phoneNumber>) uses different naming — mapping
    // happens in MetadataValueMapper.FromDto, NOT here.
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
    public string? AddressType { get; set; }
    public string? TelephoneNumber { get; set; }
    public string? TelephoneType { get; set; }
    public string? Email { get; set; }

    // AKA / DBA alternate names. Reuses AlternateNameEntryDto from PartyEntryDto.cs to keep
    // the JS payload shape symmetric across CC initial-filing (PartiesJson) and SF metadata
    // (MetadataJson). Pre-2026-05-17 the SF flow silently dropped this field at deserialization
    // because it wasn't declared here, while CC has supported it since the AKA card landed.
    public List<AlternateNameEntryDto>? Akas { get; set; }

    // For simple values (text, date, currency, boolean, codeList)
    public object? Value { get; set; }
}
