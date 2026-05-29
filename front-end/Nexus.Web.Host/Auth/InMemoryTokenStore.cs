using Microsoft.Extensions.Caching.Memory;

namespace Nexus.Web.Host.Auth;

/// <summary>
/// In-memory token store backed by IMemoryCache.
/// In a multi-instance deployment, replace with a distributed cache (Redis).
/// Entries are evicted automatically when the refresh token expires.
/// </summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    private readonly IMemoryCache _cache;

    public InMemoryTokenStore(IMemoryCache cache) => _cache = cache;

    public void Store(string sessionId, TokenEntry entry)
    {
        var opts = new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = entry.RefreshTokenExpiry
        };
        _cache.Set(CacheKey(sessionId), entry, opts);
    }

    public TokenEntry? Get(string sessionId) =>
        _cache.TryGetValue(CacheKey(sessionId), out TokenEntry? entry) ? entry : null;

    public void Remove(string sessionId) =>
        _cache.Remove(CacheKey(sessionId));

    private static string CacheKey(string sessionId) => $"nexus:token:{sessionId}";
}
