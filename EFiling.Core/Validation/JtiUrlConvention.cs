using EFiling.Core.Enums;

namespace EFiling.Core.Validation;

/// <summary>
/// Heuristics for inferring the environment of a JTI EFM endpoint from its URL.
/// JTI uses conventional hostname patterns:
///   - Staging / public-UAT: hostnames contain <c>aux-pub-</c> (e.g., <c>aux-pub-efm-madera-ca.ecourt.com</c>).
///   - Production: hostnames typically contain <c>-prod-</c> or no <c>aux-</c> prefix
///     (e.g., <c>efm-madera-court-prod-pub.ecourt.com</c>).
///
/// This is a defensive, best-effort check used to detect config drift between the
/// admin-panel <c>Environment</c> label and the actual endpoint URL. It is NOT a
/// substitute for the explicit label — it exists to catch copy-paste mistakes.
/// </summary>
public static class JtiUrlConvention
{
    /// <summary>
    /// Infer the environment from a JTI URL. Returns <see cref="CourtEnvironment.Unknown"/>
    /// if the URL is empty or doesn't match a known pattern.
    /// </summary>
    public static CourtEnvironment InferFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return CourtEnvironment.Unknown;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return CourtEnvironment.Unknown;

        var host = uri.Host.ToLowerInvariant();

        // Staging: JTI public-UAT convention uses an "aux-pub-" prefix.
        if (host.Contains("aux-pub-") || host.Contains("-uat.") || host.Contains(".uat."))
            return CourtEnvironment.Staging;

        // Production: "-prod-" token or no "aux-" indicator.
        if (host.Contains("-prod-") || host.Contains(".prod.") || host.StartsWith("efm-"))
        {
            // But only if it's not also a staging marker.
            if (!host.Contains("aux-pub-"))
                return CourtEnvironment.Production;
        }

        return CourtEnvironment.Unknown;
    }

    /// <summary>
    /// True when <paramref name="declared"/> conflicts with the environment inferred
    /// from <paramref name="url"/>. <see cref="CourtEnvironment.Unknown"/> on either
    /// side is not a conflict (we cannot prove a mismatch).
    /// </summary>
    public static bool IsMismatch(CourtEnvironment declared, string? url)
    {
        var inferred = InferFromUrl(url);
        if (declared == CourtEnvironment.Unknown || inferred == CourtEnvironment.Unknown)
            return false;
        return declared != inferred;
    }
}
