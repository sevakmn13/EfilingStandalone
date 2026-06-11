using EFiling.Core.Interfaces;

namespace EFiling.Nop.Caching;

/// <summary>
/// nopCommerce cache adapter that wraps IStaticCacheManager.
/// When running inside nopCommerce, inject the real IStaticCacheManager.
/// This adapter translates IEFilingCache calls to nopCommerce's cache API.
/// </summary>
public class NopEFilingCache : IEFilingCache
{
    private readonly INopCacheAdapter _adapter;

    public NopEFilingCache(INopCacheAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
        => _adapter.GetAsync<T>(key, ct);

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class
        => _adapter.SetAsync(key, value, expiration, ct);

    public Task RemoveAsync(string key, CancellationToken ct = default)
        => _adapter.RemoveAsync(key, ct);

    public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
        => _adapter.RemoveByPrefixAsync(prefix, ct);
}

/// <summary>
/// Thin abstraction over nopCommerce's IStaticCacheManager.
/// Implement this interface in the nopCommerce plugin to bridge the gap.
/// </summary>
public interface INopCacheAdapter
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) where T : class;
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
}
