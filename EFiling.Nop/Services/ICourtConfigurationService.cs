using EFiling.Core.Models;

namespace EFiling.Nop.Services;

/// <summary>
/// Service for managing court configurations stored in nopCommerce settings/DB.
/// </summary>
public interface ICourtConfigurationService
{
    /// <summary>Get all active court configurations.</summary>
    Task<List<CourtConfiguration>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Get a court configuration by court ID.</summary>
    Task<CourtConfiguration?> GetByCourtIdAsync(string courtId, CancellationToken ct = default);

    /// <summary>Save or update a court configuration.</summary>
    Task SaveAsync(CourtConfiguration config, CancellationToken ct = default);

    /// <summary>Delete a court configuration.</summary>
    Task DeleteAsync(string courtId, CancellationToken ct = default);
}
