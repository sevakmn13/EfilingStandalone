namespace EFiling.Core.Models;

/// <summary>
/// Payment information for a filing. Maps to JTI's PaymentMessageTypeExt.
/// </summary>
public class FilingPayment
{
    /// <summary>Customer profile ID (typically "0" for EFSP-handled billing).</summary>
    public string CustomerProfileId { get; set; } = "0";

    /// <summary>Customer payment profile ID (typically "0" for EFSP-handled billing).</summary>
    public string CustomerPaymentProfileId { get; set; } = "0";

    /// <summary>Payment type (e.g., "ACH").</summary>
    public string PaymentType { get; set; } = "ACH";

    /// <summary>Customer ACH ID (optional).</summary>
    public string? CustomerAchId { get; set; }

    /// <summary>Payer username (optional, some courts require).</summary>
    public string? UserName { get; set; }

    /// <summary>Payer email (optional, some courts require).</summary>
    public string? Email { get; set; }

    /// <summary>Payer phone number (optional, some courts require).</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Transaction authorizations (for EFSPs that pass actual payment info to JTI).</summary>
    public List<TransactionAuthorization> TransactionAuthorizations { get; set; } = new();
}

/// <summary>
/// A transaction authorization for a specific fee type (COURT, JOURNAL, STATE).
/// </summary>
public class TransactionAuthorization
{
    public string AuthorizationCode { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>Authorization type: JOURNAL, COURT, or STATE.</summary>
    public string AuthorizationType { get; set; } = string.Empty;

    public string ExternalId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
