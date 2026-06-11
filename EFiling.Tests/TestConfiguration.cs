using System.Text.Json;
using EFiling.Core.Models;
using EFiling.Core.Security;

namespace EFiling.Tests;

/// <summary>
/// Loads court configuration from testsettings.json (gitignored).
/// Copy testsettings.template.json → testsettings.json and fill in credentials.
/// 
/// Security model:
///   - Passphrase is read from EFILING_PASSPHRASE environment variable (never stored in files)
///   - Username/Password in testsettings.json are AES-256 encrypted (ENC:... prefix)
///   - Plaintext values still work for quick local setup but are not recommended
///
/// To encrypt credentials:
///   1. Set env var: $env:EFILING_PASSPHRASE = "your-secret-passphrase"
///   2. Run the EncryptCredentials_GenerateEncryptedValues test
///   3. Paste the ENC:... values into testsettings.json
/// </summary>
public static class TestConfiguration
{
    private const string PassphraseEnvVar = "EFILING_PASSPHRASE";

    private static readonly Lazy<Dictionary<string, CourtConfiguration>> _courts = new(LoadCourts);

    /// <summary>
    /// Get court config by key (e.g., "Madera.Staging", "Madera.Production").
    /// </summary>
    public static CourtConfiguration GetCourt(string name)
    {
        if (!_courts.Value.TryGetValue(name, out var config))
            throw new InvalidOperationException(
                $"Court '{name}' not found in testsettings.json. " +
                "Copy testsettings.template.json → testsettings.json and fill in your credentials.");
        return config;
    }

    /// <summary>Madera Staging (default test target).</summary>
    public static CourtConfiguration Madera => GetCourt("Madera.Staging");

    // ─── Environment-safety guards for live tests ──────────────────────
    //
    // Any test that performs a destructive live operation (SubmitFiling,
    // CancelFiling, fee capture, etc.) MUST call RequireStaging() at the top.
    // This is a hard runtime check — it refuses to run the test if the loaded
    // config is anything other than Staging.

    /// <summary>
    /// Guard: throws <see cref="InvalidOperationException"/> unless the supplied
    /// court is labelled as staging. Call this at the top of any test that
    /// performs live write operations against the court.
    /// </summary>
    /// <param name="config">Court configuration loaded via <see cref="GetCourt"/>.</param>
    /// <param name="operationDescription">Short description of the guarded operation (shown in the error).</param>
    public static void RequireStaging(CourtConfiguration config, string operationDescription = "live test operation")
    {
        ArgumentNullException.ThrowIfNull(config);
        config.RequireStagingEnvironment(operationDescription);
    }

    /// <summary>
    /// Guard variant for tests that want to return a skip-worthy reason string
    /// instead of throwing. Returns null when the config is safe to use for
    /// destructive tests, or a human-readable reason when the test should skip.
    /// </summary>
    public static string? GetSkipReasonIfNotStaging(CourtConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.IsStaging) return null;
        return $"Skipped: court '{config.CourtId}' environment is '{config.Environment}' " +
               $"(parsed as {config.EnvironmentKind}). Live destructive tests require Staging.";
    }

    /// <summary>
    /// Get the encryption passphrase from environment variable.
    /// Returns empty string if not set (plaintext credentials will still work).
    /// </summary>
    public static string GetPassphrase()
    {
        return Environment.GetEnvironmentVariable(PassphraseEnvVar) ?? string.Empty;
    }

    private static Dictionary<string, CourtConfiguration> LoadCourts()
    {
        var path = FindSettingsFile();
        if (path == null)
            throw new FileNotFoundException(
                "testsettings.json not found. Copy testsettings.template.json → testsettings.json and fill in your credentials.");

        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        var courts = new Dictionary<string, CourtConfiguration>(StringComparer.OrdinalIgnoreCase);

        // Passphrase from environment variable (never stored in config files)
        var passphrase = GetPassphrase();

        if (doc.RootElement.TryGetProperty("Courts", out var courtsElement))
        {
            foreach (var courtProp in courtsElement.EnumerateObject())
            {
                // Each court can have multiple environments
                if (courtProp.Value.ValueKind != JsonValueKind.Object)
                    continue;

                // Check if this is a flat court entry or nested environments
                if (courtProp.Value.TryGetProperty("Environments", out var envsElement))
                {
                    // Multi-environment: Courts.Madera.Environments.Staging / Production
                    foreach (var envProp in envsElement.EnumerateObject())
                    {
                        var key = $"{courtProp.Name}.{envProp.Name}";
                        courts[key] = ParseCourtConfig(envProp.Value, passphrase);
                    }
                }
                else
                {
                    // Flat entry (backwards compatible): Courts.Madera
                    courts[courtProp.Name] = ParseCourtConfig(courtProp.Value, passphrase);
                }
            }
        }

        return courts;
    }

    private static CourtConfiguration ParseCourtConfig(JsonElement el, string passphrase)
    {
        return new CourtConfiguration
        {
            CourtId = GetString(el, "CourtId"),
            DisplayName = GetString(el, "DisplayName"),
            ProviderType = GetString(el, "ProviderType"),
            Environment = GetString(el, "Environment"),
            SoapEndpoint = GetString(el, "SoapEndpoint"),
            RestBaseUrl = GetString(el, "RestBaseUrl"),
            CourtRecordEndpoint = GetString(el, "CourtRecordEndpoint"),
            Username = DecryptField(GetString(el, "Username"), passphrase),
            Password = DecryptField(GetString(el, "Password"), passphrase),
            IsActive = el.TryGetProperty("IsActive", out var active) && active.GetBoolean()
        };
    }

    private static string DecryptField(string value, string passphrase)
    {
        if (string.IsNullOrEmpty(value) || !EFilingEncryption.IsEncrypted(value))
            return value;

        if (string.IsNullOrEmpty(passphrase))
            throw new InvalidOperationException(
                $"Credentials are encrypted (ENC:...) but {PassphraseEnvVar} environment variable is not set. " +
                $"Set it: $env:{PassphraseEnvVar} = \"your-passphrase\"");

        return EFilingEncryption.Decrypt(value, passphrase);
    }

    private static string GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var val) ? val.GetString() ?? string.Empty : string.Empty;

    private static string? FindSettingsFile()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "testsettings.json");
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        return null;
    }
}
