using Nop.Core;

namespace EFiling.Nop.Domain;

/// <summary>
/// Audit-capture record for Unlawful Detainer §1161.2 disclaimer attestations.
///
/// Step #43 — implements UD-2 ("Access Tracking and Data Capture")
/// from the JTI EFM vendor doc node/436#UnlawfulDetainer
/// (<c>docs/fileing files/Subsequent Filing/General Concepts/Subsequent Filing - General Concepts _ EFM Documentation.html:257-259</c>):
///
/// <para>
/// <i>"The end user's response is required to be captured. If the user states
/// they are not a party to the case, they should not able to proceed further.
/// If answered in the affirmative, then the user proceeds to accessing the
/// Unlawful Detainer case. At this point the EFSP is required to capture the
/// answer to the question, the user name and Case Number involved in the
/// search."</i>
/// </para>
///
/// Stored as a permanent audit row (one per Y/N response). The
/// <see cref="IUdAccessAttestationService"/> uses a 24-hour validity window
/// when checking <c>HasValidAttestationAsync</c> so a single Y answer doesn't
/// require re-prompting on every page hit; each fresh-day access creates a
/// new audit row.
/// </summary>
public class UdAccessAttestation : BaseEntity
{
    /// <summary>nopCommerce Customer ID (FK to Customer table) — the "user name" required by the JTI mandate.</summary>
    public int CustomerId { get; set; }

    /// <summary>Court ID the user was searching in (e.g., "madera").</summary>
    public string CourtId { get; set; } = string.Empty;

    /// <summary>Case docket ID (case number) the user attempted to access — the "Case Number involved in the search" required by the JTI mandate.</summary>
    public string CaseDocketId { get; set; } = string.Empty;

    /// <summary>
    /// CASE_CATEGORY codelist code observed on the case at the time of attestation
    /// (e.g., Madera UD = "407200"). Captured for audit completeness so future
    /// review can confirm the case actually was UD when the disclaimer fired.
    /// </summary>
    public string? CaseCategoryCode { get; set; }

    /// <summary>
    /// The Y/N answer to the party-attestation question:
    /// <c>true</c> = user attested they ARE a party to the case (allowed to proceed);
    /// <c>false</c> = user attested they are NOT a party (blocked from proceeding).
    /// </summary>
    public bool AttestedAsParty { get; set; }

    /// <summary>UTC timestamp the attestation was captured.</summary>
    public DateTime AttestedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Snapshot of the verbatim §1161.2 disclaimer text shown to the user at
    /// the time of attestation. Captured so future reviews can verify the
    /// disclaimer text in effect when the user agreed — even if we update the
    /// JTI-mandated wording later.
    /// </summary>
    public string? DisclaimerTextShown { get; set; }

    /// <summary>
    /// IP address of the user at attestation time (RFC 5321 forward header
    /// or socket remote address). Optional but useful for fraud-review.
    /// </summary>
    public string? IpAddress { get; set; }
}
