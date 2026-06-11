namespace EFiling.Nop.Models;

/// <summary>
/// View model for the §1161.2 Unlawful Detainer disclaimer + Y/N
/// party-attestation page. Step #43.
///
/// Backs <c>UdAttestation.cshtml</c>. Posted by the user when they answer the
/// party-attestation question; the controller writes a permanent
/// <see cref="Domain.UdAccessAttestation"/> audit row per the JTI UD-2
/// mandate (see <see cref="UdDisclaimer.UdDisclaimerPolicy"/> XML doc).
/// </summary>
public class UdAttestationModel
{
    /// <summary>Court ID the user is searching in (e.g., "madera").</summary>
    public string? CourtId { get; set; }

    /// <summary>Case docket ID (case number) the user attempted to access.</summary>
    public string? CaseDocketId { get; set; }

    /// <summary>
    /// CASE_CATEGORY codelist code observed on the case (used to verify the
    /// case actually is UD before showing the disclaimer or writing an
    /// audit row).
    /// </summary>
    public string? CaseCategoryCode { get; set; }

    /// <summary>
    /// Server-side URL to redirect to after a Y (affirmative) attestation.
    /// Must be a local URL (<see cref="Microsoft.AspNetCore.Mvc.IUrlHelper.IsLocalUrl"/>);
    /// non-local URLs are ignored and the user is redirected to SearchCase
    /// as a safe default.
    /// </summary>
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// User's Y/N answer to the party-attestation question:
    /// <c>true</c> = user attested they ARE a party to the case;
    /// <c>false</c> = user attested they are NOT a party (blocked).
    /// </summary>
    public bool AttestedAsParty { get; set; }
}
