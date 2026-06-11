namespace EFiling.Core.Interfaces;

/// <summary>
/// Abstract cache interface for the EFiling library.
/// Hosts (nopCommerce, console app, etc.) provide their own implementation.
/// </summary>
public interface IEFilingCache
{
    /// <summary>
    /// Get a cached value by key. Returns default if not found or expired.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;

    /// <summary>
    /// Set a value in cache with an optional expiration.
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class;

    /// <summary>
    /// Remove a specific key from cache.
    /// </summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Remove all keys matching the given prefix (e.g., "efiling:madera:" to clear all Madera cache).
    /// </summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
}
