namespace EFiling.Nop.Models;

/// <summary>
/// DTO for document entries deserialized from the CreateCase form's DocumentsJson field.
/// </summary>
public class DocumentEntryDto
{
    /// <summary>"lead" or "connected".</summary>
    public string? Role { get; set; }

    /// <summary>Document type code from court policy.</summary>
    public string? DocumentCode { get; set; }

    /// <summary>Human-readable document type name (e.g., "Complaint", "Summons").</summary>
    public string? DocumentDescription { get; set; }

    /// <summary>Optional name extension (e.g., "Amended").</summary>
    public string? NameExtension { get; set; }

    /// <summary>Metadata values collected from the dynamic form.</summary>
    public List<MetadataEntryDto>? Metadata { get; set; }

    /// <summary>IdentificationSourceText for DocumentIdentification (e.g., party role code "PLA").</summary>
    public string? IdentificationSourceText { get; set; }

    // Azure Blob fields (populated when file was uploaded during draft save)
    public string? BlobPath { get; set; }
    public string? BlobUrl { get; set; }
    public string? BlobFileName { get; set; }
    public long BlobFileSize { get; set; }
    public int BlobPageCount { get; set; }
}

// MetadataEntryDto moved to MetadataEntryDto.cs

public class AdditionalInfoTagDto
{
    public string? TagType { get; set; }
    public string? TagValue { get; set; }
}
