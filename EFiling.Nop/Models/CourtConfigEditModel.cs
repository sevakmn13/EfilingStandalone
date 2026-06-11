namespace EFiling.Nop.Models;

/// <summary>
/// View model for the admin court configuration create/edit form.
/// </summary>
public class CourtConfigEditModel
{
    // ─── Core ────────────────────────────────────────────────────────

    public string CourtId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CountyName { get; set; } = string.Empty;
    public string ProviderType { get; set; } = "JTI";
    public string Environment { get; set; } = "Staging";

    // ─── Endpoints ───────────────────────────────────────────────────

    public string SoapEndpoint { get; set; } = string.Empty;
    public string RestBaseUrl { get; set; } = string.Empty;
    public string CourtRecordEndpoint { get; set; } = string.Empty;
    public string NfrcCallbackUrl { get; set; } = string.Empty;

    // ─── Credentials ─────────────────────────────────────────────────

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    // ─── Status ──────────────────────────────────────────────────────

    public bool IsActive { get; set; } = true;

    // ─── Case Type Configuration ──────────────────────────────────

    /// <summary>Comma-separated case type codes that map to ECF CivilCaseType (e.g., "411110,421110").</summary>
    public string CivilCaseTypeCodes { get; set; } = string.Empty;

    // ─── Test Filing Mode ────────────────────────────────────────────

    /// <summary>Test filing mode: 0=None, 1=AutoAccept, 2=AutoReject. Staging/UAT only.</summary>
    public int TestFilingMode { get; set; } = 0;

    // ─── UI State ────────────────────────────────────────────────────

    /// <summary>True when editing an existing court (CourtId is read-only).</summary>
    public bool IsEdit { get; set; }

    /// <summary>
    /// Environment-safety warnings to display in the edit UI (populated by
    /// <c>CourtAdminController</c> via <c>CourtConfigurationValidator</c>).
    /// Warnings are non-blocking; errors are surfaced via ModelState instead.
    /// </summary>
    public List<string> ValidationWarnings { get; set; } = new();
}
