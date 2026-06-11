using Nop.Core;

namespace EFiling.Nop.Domain;

/// <summary>
/// Database entity for court configuration. Stored in the CourtConfiguration table.
/// Maps to/from <see cref="EFiling.Core.Models.CourtConfiguration"/> for use by the EFiling library.
/// </summary>
public class CourtConfigurationRecord : BaseEntity
{
    /// <summary>Unique court identifier (e.g., "madera").</summary>
    public string CourtId { get; set; } = string.Empty;

    /// <summary>Display name for UI (e.g., "Madera Superior Court").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>County name (e.g., "Madera").</summary>
    public string CountyName { get; set; } = string.Empty;

    /// <summary>Provider type key (e.g., "JTI").</summary>
    public string ProviderType { get; set; } = string.Empty;

    /// <summary>Environment label (e.g., "Staging", "Production").</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>SOAP endpoint for FilingReview operations.</summary>
    public string SoapEndpoint { get; set; } = string.Empty;

    /// <summary>Base URL for REST code list endpoints.</summary>
    public string RestBaseUrl { get; set; } = string.Empty;

    /// <summary>SOAP endpoint for CourtRecord operations.</summary>
    public string CourtRecordEndpoint { get; set; } = string.Empty;

    /// <summary>Callback URL for NFRC delivery.</summary>
    public string NfrcCallbackUrl { get; set; } = string.Empty;

    /// <summary>Username for HTTP Basic Auth.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Encrypted password for HTTP Basic Auth.</summary>
    public string EncryptedPassword { get; set; } = string.Empty;

    /// <summary>Whether this court is active and available in the UI.</summary>
    public bool IsActive { get; set; } = true;

    // ─── Case Type Configuration ──────────────────────────────────────

    /// <summary>JSON array of case type codes that map to ECF CivilCaseType (e.g., ["411110","421110"]).</summary>
    public string CivilCaseTypeCodesJson { get; set; } = "[]";

    /// <summary>Test filing mode (0=None, 1=AutoAccept, 2=AutoReject). Staging/UAT only.</summary>
    public int TestFilingMode { get; set; } = 0;

    /// <summary>Additional feature flags stored as JSON.</summary>
    public string ExtraFlagsJson { get; set; } = "{}";

    /// <summary>When this record was created (UTC).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When this record was last updated (UTC).</summary>
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
