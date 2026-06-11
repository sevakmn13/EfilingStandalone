namespace EFiling.Core.Models;

/// <summary>
/// Parsed representation of a court's policy response (GetPolicy).
/// </summary>
public class CourtPolicy
{
    /// <summary>Policy version identifier — used as cache key.</summary>
    public int PolicyVersionId { get; set; }

    /// <summary>When the policy was last updated by the court.</summary>
    public DateTime PolicyLastUpdateDate { get; set; }

    /// <summary>Code list REST endpoint URLs keyed by list type (e.g., "CASE_TYPE" → URL).</summary>
    public Dictionary<string, string> CodeListUrls { get; set; } = new();

    /// <summary>Document list endpoint URL.</summary>
    public string? DocumentListUrl { get; set; }

    /// <summary>Court locations endpoint URL.</summary>
    public string? CourtLocationsUrl { get; set; }

    /// <summary>Attorney list endpoint URL.</summary>
    public string? AttorneyListUrl { get; set; }

    /// <summary>Raw XML of the policy response for debugging/inspection.</summary>
    public string? RawXml { get; set; }
}
