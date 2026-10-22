using Microsoft.Extensions.Caching.Memory;

namespace DevOpsBlazor.AzureDevOps.Http;

/// <summary>A dedicated in-memory cache for Azure DevOps responses, so "Refresh" can clear it without touching anything else.</summary>
public sealed class AdoCache : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(key, out T? cached) && cached is not null) return cached;
        var value = await factory(cancellationToken);
        if (ttl > TimeSpan.Zero) _cache.Set(key, value, ttl);
        return value;
    }

    public void Clear() => _cache.Clear();

    public void Dispose() => _cache.Dispose();
}
