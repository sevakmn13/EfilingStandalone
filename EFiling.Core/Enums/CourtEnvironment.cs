namespace EFiling.Core.Enums;

/// <summary>
/// Parsed court environment. Derived from the free-text <c>CourtConfiguration.Environment</c>
/// string via <see cref="CourtEnvironmentParser.Parse"/>. Used for runtime safety guards
/// (e.g., preventing destructive test operations from running against production).
///
/// Fail-closed semantics: any unrecognized/empty label parses to <see cref="Unknown"/>,
/// which is treated as "not safe for destructive tests".
/// </summary>
public enum CourtEnvironment
{
    /// <summary>
    /// Environment could not be determined (empty or unrecognized label).
    /// Treated as unsafe: destructive-test guards must refuse to run.
    /// </summary>
    Unknown = 0,

    /// <summary>Staging / UAT. Safe for destructive tests and live-write smoke tests.</summary>
    Staging = 1,

    /// <summary>
    /// Live production. NEVER safe for automated destructive tests.
    /// Filings submitted here create real court records.
    /// </summary>
    Production = 2,
}

/// <summary>
/// Parses free-text <c>Environment</c> labels from <see cref="Models.CourtConfiguration"/>
/// into a strongly-typed <see cref="CourtEnvironment"/>. Case-insensitive.
/// Recognized aliases kept deliberately narrow so misconfiguration does not silently
/// map to Production (or silently map Production down to Staging).
/// </summary>
public static class CourtEnvironmentParser
{
    /// <summary>
    /// Parse a raw environment label. Returns <see cref="CourtEnvironment.Unknown"/>
    /// for null, empty, whitespace, or any value that is not in the recognized set.
    /// </summary>
    public static CourtEnvironment Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return CourtEnvironment.Unknown;

        var trimmed = raw.Trim();

        // Staging aliases — deliberately narrow.
        if (trimmed.Equals("Staging", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Stage", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("UAT", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Test", StringComparison.OrdinalIgnoreCase))
        {
            return CourtEnvironment.Staging;
        }

        // Production aliases — require an exact, unambiguous label.
        if (trimmed.Equals("Production", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Prod", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Live", StringComparison.OrdinalIgnoreCase))
        {
            return CourtEnvironment.Production;
        }

        return CourtEnvironment.Unknown;
    }

    /// <summary>
    /// Canonical string representation used for storage/display.
    /// Round-trips with <see cref="Parse"/>.
    /// </summary>
    public static string ToCanonicalString(CourtEnvironment env) => env switch
    {
        CourtEnvironment.Staging => "Staging",
        CourtEnvironment.Production => "Production",
        _ => "Unknown",
    };
}
