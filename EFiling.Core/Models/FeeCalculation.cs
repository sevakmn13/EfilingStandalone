namespace EFiling.Core.Models;

/// <summary>
/// Result of a GetFeesCalculation call.
/// </summary>
public class FeeCalculation
{
    /// <summary>Total fees amount.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Individual fee line items.</summary>
    public List<FeeLineItem> LineItems { get; set; } = new();

    /// <summary>Fee exemption type if applicable (e.g., "WAIVED", "EXEMPTED").</summary>
    public string? ExemptionType { get; set; }

    /// <summary>Error code (0 = success).</summary>
    public int ErrorCode { get; set; }

    /// <summary>Error text if fee calculation failed.</summary>
    public string? ErrorText { get; set; }

    /// <summary>Raw XML response for debugging.</summary>
    public string? RawXml { get; set; }
}

/// <summary>
/// A single fee line item from the AllowanceCharge element.
/// </summary>
public class FeeLineItem
{
    /// <summary>Fee amount.</summary>
    public decimal Amount { get; set; }

    /// <summary>Fee category code (COURT or JOURNAL).</summary>
    public string AccountingCostCode { get; set; } = string.Empty;

    /// <summary>Description of the fee.</summary>
    public string? Description { get; set; }
}
