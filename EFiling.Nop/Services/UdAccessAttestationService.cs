using EFiling.Nop.Domain;
using Nop.Data;

namespace EFiling.Nop.Services;

/// <summary>
/// Database-backed UD-attestation audit service using nopCommerce's
/// <see cref="IRepository{T}"/> pattern. Persists all attestations to the
/// UdAccessAttestation SQL Server table for compliance audit retention.
/// </summary>
public class UdAccessAttestationService : IUdAccessAttestationService
{
    private readonly IRepository<UdAccessAttestation> _repository;

    public UdAccessAttestationService(IRepository<UdAccessAttestation> repository)
    {
        _repository = repository;
    }

    /// <inheritdoc/>
    public TimeSpan ValidityWindow { get; } = TimeSpan.FromHours(24);

    /// <inheritdoc/>
    public async Task<UdAccessAttestation> RecordAsync(UdAccessAttestation attestation, CancellationToken ct = default)
    {
        if (attestation == null)
            throw new ArgumentNullException(nameof(attestation));
        if (attestation.AttestedUtc == default)
            attestation.AttestedUtc = DateTime.UtcNow;
        await _repository.InsertAsync(attestation);
        return attestation;
    }

    /// <inheritdoc/>
    public async Task<bool> HasValidAttestationAsync(int customerId, string courtId, string caseDocketId, CancellationToken ct = default)
    {
        if (customerId <= 0 || string.IsNullOrEmpty(courtId) || string.IsNullOrEmpty(caseDocketId))
            return false;

        var cutoff = DateTime.UtcNow - ValidityWindow;
        var matching = await _repository.GetAllAsync(q =>
            q.Where(a => a.CustomerId == customerId
                      && a.CourtId == courtId
                      && a.CaseDocketId == caseDocketId
                      && a.AttestedAsParty
                      && a.AttestedUtc >= cutoff));
        return matching.Any();
    }

    /// <inheritdoc/>
    public async Task<UdAccessAttestation?> GetMostRecentAsync(int customerId, string courtId, string caseDocketId, CancellationToken ct = default)
    {
        if (customerId <= 0 || string.IsNullOrEmpty(courtId) || string.IsNullOrEmpty(caseDocketId))
            return null;

        var matching = await _repository.GetAllAsync(q =>
            q.Where(a => a.CustomerId == customerId
                      && a.CourtId == courtId
                      && a.CaseDocketId == caseDocketId)
             .OrderByDescending(a => a.AttestedUtc));
        return matching.FirstOrDefault();
    }
}
