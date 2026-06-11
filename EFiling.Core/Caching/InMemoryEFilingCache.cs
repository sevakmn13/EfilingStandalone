using System.Collections.Concurrent;
using EFiling.Core.Interfaces;

namespace EFiling.Core.Caching;

/// <summary>
/// Simple in-memory cache implementation for standalone use and testing.
/// Production hosts (nopCommerce) should provide their own implementation
/// backed by IStaticCacheManager or Redis.
/// </summary>
public class InMemoryEFilingCache : IEFilingCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.Expiration.HasValue && entry.Expiration.Value < DateTimeOffset.UtcNow)
            {
                _cache.TryRemove(key, out _);
                return Task.FromResult<T?>(null);
            }
            return Task.FromResult(entry.Value as T);
        }
        return Task.FromResult<T?>(null);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default) where T : class
    {
        var entry = new CacheEntry
        {
            Value = value,
            Expiration = expiration.HasValue ? DateTimeOffset.UtcNow.Add(expiration.Value) : null
        };
        _cache[key] = entry;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _cache.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var keysToRemove = _cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }

    private class CacheEntry
    {
        public object? Value { get; set; }
        public DateTimeOffset? Expiration { get; set; }
    }
}
