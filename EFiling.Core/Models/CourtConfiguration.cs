using EFiling.Core.Enums;

namespace EFiling.Core.Models;

/// <summary>
/// Configuration for a single court endpoint. Credentials are expected to be
/// decrypted before being passed to the EFiling library.
/// </summary>
public class CourtConfiguration
{
    /// <summary>Unique identifier for this court (e.g., "madera").</summary>
    public string CourtId { get; set; } = string.Empty;

    /// <summary>Display name shown in UI (e.g., "Madera Superior Court").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>County name (e.g., "Madera"). Used to group courts by county in the UI.</summary>
    public string CountyName { get; set; } = string.Empty;

    /// <summary>Provider type key (e.g., "JTI").</summary>
    public string ProviderType { get; set; } = string.Empty;

    /// <summary>
    /// Environment label (e.g., "Staging", "Production"). Free-text for backward
    /// compatibility, but parsed via <see cref="EnvironmentKind"/> for runtime safety
    /// guards. Prefer reading <see cref="EnvironmentKind"/>, <see cref="IsStaging"/>,
    /// or <see cref="IsProduction"/> instead of string-comparing this field.
    /// </summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>SOAP endpoint for FilingReview operations.</summary>
    public string SoapEndpoint { get; set; } = string.Empty;

    /// <summary>Base URL for REST code list endpoints.</summary>
    public string RestBaseUrl { get; set; } = string.Empty;

    /// <summary>SOAP endpoint for CourtRecord operations.</summary>
    public string CourtRecordEndpoint { get; set; } = string.Empty;

    /// <summary>Callback URL sent in ReviewFiling for NFRC delivery.</summary>
    public string NfrcCallbackUrl { get; set; } = string.Empty;

    /// <summary>Username for HTTP Basic Auth (decrypted).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Password for HTTP Basic Auth (decrypted).</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Whether this court configuration is active.</summary>
    public bool IsActive { get; set; } = true;

    // ─── Case Type Configuration ──────────────────────────────────────

    /// <summary>
    /// UI-only visibility gate for the Civil-specific `CivilCaseTypeExt` checkbox panel
    /// (Complex Litigation, Class Action, Asbestos, CEQA, Conditionally Sealed). When the
    /// selected case type code is in this list, <c>CreateCase.cshtml</c> shows the panel;
    /// otherwise it hides. Per-court configurable because the CASE_TYPE code format differs
    /// (e.g., Madera uses numeric <c>["411110","421110"]</c>, LASC uses letter <c>["CU","LC"]</c>).
    ///
    /// ⚠️ **Not a court-scope list.** Madera's GetPolicy actually advertises 5 case types
    /// (Family 211110, Civil Unlimited 411110, Civil Limited 421110, Probate 511110,
    /// Small Claims 711110) — see <c>docs/fileing files/madera_CASE_TYPE.xml</c>. The main
    /// case-category dropdown is populated from policy, not from this list.
    ///
    /// ⚠️ **Not used by the XML builder.** <c>ReviewFilingXmlBuilder.BuildCase</c> always
    /// emits <c>xsi:type="ns6:CivilCaseTypeExt"</c> today regardless of this list. That is a
    /// separate latent issue tracked for the UI refactor (T-3/T-4) where an
    /// <c>ICaseCategoryPolicy</c> abstraction will subsume this field with a policy-driven
    /// case-type → extension-field mapping.
    /// </summary>
    public List<string> CivilCaseTypeCodes { get; set; } = new();

    /// <summary>
    /// Test filing mode for staging/UAT. When set, a JTI test header is injected
    /// causing immediate acceptance or rejection without clerk review.
    /// </summary>
    public Enums.TestFilingMode TestFilingMode { get; set; } = Enums.TestFilingMode.None;

    /// <summary>Additional court-specific feature flags as JSON key-value pairs.</summary>
    public Dictionary<string, string> ExtraFlags { get; set; } = new();

    // ─── Environment safety helpers (runtime guards) ──────────────────
    //
    // These properties exist so code can reason about staging vs production
    // without doing fragile string comparisons on Environment. Any unrecognized
    // or empty label parses to CourtEnvironment.Unknown, which is treated as
    // "not safe for destructive tests" (fail-closed). See CourtEnvironmentParser
    // for the recognized label set.

    /// <summary>
    /// Parsed form of <see cref="Environment"/>. Case-insensitive; unknown labels
    /// return <see cref="CourtEnvironment.Unknown"/>. Evaluated on each access so it
    /// reflects runtime updates to the <see cref="Environment"/> string.
    /// </summary>
    public CourtEnvironment EnvironmentKind => CourtEnvironmentParser.Parse(Environment);

    /// <summary>True when this court points at a staging / UAT environment.</summary>
    public bool IsStaging => EnvironmentKind == CourtEnvironment.Staging;

    /// <summary>True when this court points at live production.</summary>
    public bool IsProduction => EnvironmentKind == CourtEnvironment.Production;

    /// <summary>
    /// True when the environment label is missing or unrecognized. Treated as
    /// "unsafe / unknown" — destructive test operations must refuse to run.
    /// </summary>
    public bool IsUnknownEnvironment => EnvironmentKind == CourtEnvironment.Unknown;

    /// <summary>
    /// True when it is safe to run automated destructive tests (live submissions,
    /// cancellations, etc.) against this court. Requires the court to be explicitly
    /// labelled <see cref="CourtEnvironment.Staging"/>. Production and Unknown both
    /// return false.
    /// </summary>
    public bool AllowsDestructiveTests => IsStaging;

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when this court is not staging.
    /// Call at the top of any test / harness code that performs a live write
    /// (submissions, cancellations, payment capture).
    /// </summary>
    /// <param name="operationDescription">Short description of the operation being guarded (used in the error message).</param>
    public void RequireStagingEnvironment(string operationDescription = "destructive operation")
    {
        if (!IsStaging)
        {
            throw new InvalidOperationException(
                $"Refusing to run {operationDescription} against court '{CourtId}' " +
                $"because its environment is '{Environment}' (parsed as {EnvironmentKind}). " +
                $"This operation is only permitted when Environment == Staging. " +
                $"If you really intend to target production, do it manually — never via automated tests.");
        }
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the court is Production and
    /// <see cref="TestFilingMode"/> is not <see cref="Enums.TestFilingMode.None"/>.
    /// Test headers must never be sent to production.
    /// </summary>
    public void RequireTestModeAllowedForEnvironment()
    {
        if (IsProduction && TestFilingMode != Enums.TestFilingMode.None)
        {
            throw new InvalidOperationException(
                $"Court '{CourtId}' is configured as Production but TestFilingMode is " +
                $"{TestFilingMode}. Test headers must never be sent to production courts. " +
                $"Set TestFilingMode to None, or change Environment to Staging.");
        }
    }
}
