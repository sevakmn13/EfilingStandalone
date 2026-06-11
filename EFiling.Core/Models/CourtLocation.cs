namespace EFiling.Core.Models;

/// <summary>
/// A court location returned from the court locations lookup endpoint.
/// </summary>
public class CourtLocation
{
    /// <summary>Location code (ParentLocationCode, e.g., "RCD", "LA", "GIB").</summary>
    public string LocationCode { get; set; } = string.Empty;

    /// <summary>Display name for the location.</summary>
    public string LocationName { get; set; } = string.Empty;
}
