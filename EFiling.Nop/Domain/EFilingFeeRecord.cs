using Nop.Core;

namespace EFiling.Nop.Domain;

/// <summary>
/// Fee line item for a filing. Two sets per filing:
/// Source=Estimated (from GetFeesCalculation at submit time) and
/// Source=Charged (from NFRC #2 with actual court fees).
/// </summary>
public class EFilingFeeRecord : BaseEntity
{
    /// <summary>FK to EFilingOrderRecord.Id.</summary>
    public int EFilingOrderRecordId { get; set; }

    /// <summary>"Estimated" (from GetFeesCalculation) or "Charged" (from NFRC #2).</summary>
    public string Source { get; set; } = "Estimated";

    /// <summary>Fee amount in dollars.</summary>
    public decimal Amount { get; set; }

    /// <summary>Fee category: COURT or JOURNAL.</summary>
    public string AccountingCostCode { get; set; } = string.Empty;

    /// <summary>Description of the fee (e.g., "Statutory Filing Fee").</summary>
    public string? Description { get; set; }

    /// <summary>When this record was created (UTC).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
