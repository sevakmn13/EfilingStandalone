using EFiling.Nop.Domain;

namespace EFiling.Nop.Services;

/// <summary>
/// Audit-capture service for §1161.2 Unlawful Detainer disclaimer attestations.
///
/// Step #43 — backs the UD-2 mandate from JTI EFM vendor doc
/// node/436#UnlawfulDetainer ("Access Tracking and Data Capture"). See the
/// <see cref="UdAccessAttestation"/> XML doc for the verbatim source quote.
///
/// Two responsibilities:
/// 1. <see cref="RecordAsync"/> — write a permanent audit row whenever a user
///    answers the Y/N party-attestation question. Required for compliance
///    even if the answer is N.
/// 2. <see cref="HasValidAttestationAsync"/> — gate the case-access controller
///    actions: if a customer has a recent (within <see cref="ValidityWindow"/>)
///    affirmative attestation for the same court+case, allow them to proceed
///    without re-prompting. Each fresh-day access produces a new audit row.
/// </summary>
public interface IUdAccessAttestationService
{
    /// <summary>
    /// Lookback window for treating an attestation as "still valid" — i.e.
    /// not requiring re-prompting. 24 hours is the default; the audit row
    /// itself is persisted permanently regardless of this window.
    /// </summary>
    TimeSpan ValidityWindow { get; }

    /// <summary>
    /// Persists a new audit row for the given attestation. Always inserts;
    /// never updates an existing row. The N response is captured equally to
    /// the Y response (the JTI mandate explicitly requires capturing the
    /// answer regardless of which way it goes).
    /// </summary>
    Task<UdAccessAttestation> RecordAsync(UdAccessAttestation attestation, CancellationToken ct = default);

    /// <summary>
    /// Returns <c>true</c> only when an affirmative (Y) attestation exists
    /// for the given (customerId, courtId, caseDocketId) tuple within the
    /// <see cref="ValidityWindow"/>. N attestations never grant access.
    /// </summary>
    Task<bool> HasValidAttestationAsync(int customerId, string courtId, string caseDocketId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the most-recent attestation row for the given tuple, or
    /// <c>null</c> if none exists. Used primarily for tests and audit
    /// review tooling.
    /// </summary>
    Task<UdAccessAttestation?> GetMostRecentAsync(int customerId, string courtId, string caseDocketId, CancellationToken ct = default);
}
