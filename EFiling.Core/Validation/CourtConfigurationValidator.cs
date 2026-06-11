using EFiling.Core.Enums;
using EFiling.Core.Models;

namespace EFiling.Core.Validation;

/// <summary>
/// Severity of a <see cref="CourtConfigurationValidationIssue"/>.
/// </summary>
public enum CourtConfigValidationSeverity
{
    /// <summary>Informational — something the admin should be aware of but not necessarily wrong.</summary>
    Info,

    /// <summary>Warning — likely misconfiguration, but not fatal. Saves are allowed.</summary>
    Warning,

    /// <summary>Error — invalid configuration that would lead to unsafe runtime behavior. Saves should be blocked.</summary>
    Error,
}

/// <summary>
/// A single issue found by <see cref="CourtConfigurationValidator.Validate"/>.
/// </summary>
public sealed class CourtConfigurationValidationIssue
{
    public CourtConfigValidationSeverity Severity { get; init; }

    /// <summary>Short code identifying the rule (e.g., <c>ENV_URL_MISMATCH</c>). Stable for tests.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Field on <see cref="CourtConfiguration"/> that triggered the issue, if applicable.</summary>
    public string? Field { get; init; }

    /// <summary>Human-readable message suitable for display in the admin UI.</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Inspects a <see cref="CourtConfiguration"/> for staging/production safety issues:
///   - Missing/unknown environment label.
///   - Declared environment vs. endpoint-URL pattern mismatch.
///   - <see cref="TestFilingMode"/> set on a production court (blocked).
///   - <see cref="TestFilingMode"/> set but environment is Unknown (warning).
///
/// Returns a list of issues. Callers decide whether to block saves, display
/// warnings, or abort on startup.
/// </summary>
public static class CourtConfigurationValidator
{
    /// <summary>
    /// Run all environment-safety checks on <paramref name="config"/>.
    /// </summary>
    public static IReadOnlyList<CourtConfigurationValidationIssue> Validate(CourtConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var issues = new List<CourtConfigurationValidationIssue>();

        // ── Environment label present and recognized ────────────────────
        if (config.IsUnknownEnvironment)
        {
            issues.Add(new CourtConfigurationValidationIssue
            {
                Severity = CourtConfigValidationSeverity.Warning,
                Code = "ENV_UNKNOWN",
                Field = nameof(CourtConfiguration.Environment),
                Message = string.IsNullOrWhiteSpace(config.Environment)
                    ? "Environment is empty. Set it to 'Staging' or 'Production'. Unknown environments are treated as unsafe — destructive tests will refuse to run."
                    : $"Environment label '{config.Environment}' is not recognized. Expected 'Staging' or 'Production'. Unknown environments are treated as unsafe.",
            });
        }

        // ── Test mode on production = ERROR (blocked) ───────────────────
        if (config.IsProduction && config.TestFilingMode != TestFilingMode.None)
        {
            issues.Add(new CourtConfigurationValidationIssue
            {
                Severity = CourtConfigValidationSeverity.Error,
                Code = "PROD_TEST_MODE",
                Field = nameof(CourtConfiguration.TestFilingMode),
                Message = $"TestFilingMode is '{config.TestFilingMode}' but Environment is 'Production'. " +
                          "Test headers must never be sent to production courts. Set TestFilingMode to None, " +
                          "or change Environment to Staging.",
            });
        }

        // ── Test mode on unknown env = warning ──────────────────────────
        if (config.IsUnknownEnvironment && config.TestFilingMode != TestFilingMode.None)
        {
            issues.Add(new CourtConfigurationValidationIssue
            {
                Severity = CourtConfigValidationSeverity.Warning,
                Code = "UNKNOWN_ENV_TEST_MODE",
                Field = nameof(CourtConfiguration.TestFilingMode),
                Message = $"TestFilingMode is '{config.TestFilingMode}' but Environment is not clearly labelled. " +
                          "Explicitly set Environment to 'Staging' to confirm this is safe.",
            });
        }

        // ── URL pattern vs declared environment ─────────────────────────
        // Check each endpoint URL we know about. Produce one issue per mismatched URL.
        CheckUrlMismatch(issues, config, config.SoapEndpoint, nameof(CourtConfiguration.SoapEndpoint));
        CheckUrlMismatch(issues, config, config.RestBaseUrl, nameof(CourtConfiguration.RestBaseUrl));
        CheckUrlMismatch(issues, config, config.CourtRecordEndpoint, nameof(CourtConfiguration.CourtRecordEndpoint));

        return issues;
    }

    /// <summary>
    /// Convenience: true when <see cref="Validate"/> reports no errors. Warnings allowed.
    /// </summary>
    public static bool IsSafeToSave(CourtConfiguration config)
        => !Validate(config).Any(i => i.Severity == CourtConfigValidationSeverity.Error);

    private static void CheckUrlMismatch(
        List<CourtConfigurationValidationIssue> issues,
        CourtConfiguration config,
        string url,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (config.EnvironmentKind == CourtEnvironment.Unknown) return;

        var inferred = JtiUrlConvention.InferFromUrl(url);
        if (inferred == CourtEnvironment.Unknown) return;
        if (inferred == config.EnvironmentKind) return;

        issues.Add(new CourtConfigurationValidationIssue
        {
            Severity = CourtConfigValidationSeverity.Warning,
            Code = "ENV_URL_MISMATCH",
            Field = fieldName,
            Message = $"{fieldName} URL looks like {inferred} (based on hostname pattern) but Environment is declared as " +
                      $"{config.EnvironmentKind}. Double-check that the URL matches the intended environment.",
        });
    }
}
