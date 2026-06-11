using EFiling.Core.Enums;

namespace EFiling.Core.Models;

/// <summary>
/// Result returned after submitting a filing via ReviewFiling.
/// </summary>
public class FilingResult
{
    /// <summary>Whether the filing was accepted for processing (HTTP 200 + no SOAP fault).</summary>
    public bool Success { get; set; }

    /// <summary>EFM-assigned filing reference ID (from MessageReceipt).</summary>
    public string? EfmReferenceId { get; set; }

    /// <summary>EFSP's own filing reference ID (passed in the request).</summary>
    public string? EfspReferenceId { get; set; }

    /// <summary>Initial filing status.</summary>
    public FilingStatus Status { get; set; } = FilingStatus.Unknown;

    /// <summary>Error code from the response (0 = success).</summary>
    public int ErrorCode { get; set; }

    /// <summary>Error text if the filing was rejected at submission.</summary>
    public string? ErrorText { get; set; }

    /// <summary>Raw XML response for debugging.</summary>
    public string? RawXml { get; set; }
}
