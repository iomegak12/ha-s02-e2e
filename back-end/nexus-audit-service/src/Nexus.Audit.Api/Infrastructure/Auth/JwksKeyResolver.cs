using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace Nexus.Audit.Api.Infrastructure.Auth;

/// <summary>
/// Caches the Identity service's JWKS and exposes the signing keys as an
/// <see cref="IssuerSigningKeyResolver"/>. Refreshed automatically by
/// <see cref="ConfigurationManager{T}"/> (15-minute interval, 1-minute throttled
/// refresh on key-not-found).
/// </summary>
public sealed class JwksKeyResolver
{
    private readonly ConfigurationManager<JsonWebKeySet> _manager;

    /// <summary>Creates a resolver pointing at the given JWKS URL.</summary>
    public JwksKeyResolver(string jwksUrl)
    {
        if (string.IsNullOrWhiteSpace(jwksUrl))
        {
            throw new ArgumentException("JWKS URL must be non-empty.", nameof(jwksUrl));
        }

        var docRetriever = new HttpDocumentRetriever { RequireHttps = false };
        _manager = new ConfigurationManager<JsonWebKeySet>(
            metadataAddress: jwksUrl,
            configRetriever: new JwksDocumentRetriever(),
            docRetriever: docRetriever)
        {
            AutomaticRefreshInterval = TimeSpan.FromMinutes(15),
            RefreshInterval = TimeSpan.FromMinutes(1),
        };
    }

    /// <summary>
    /// Resolve the current set of signing keys for the supplied token. Matching
    /// <paramref name="kid"/> is returned first when present.
    /// </summary>
    public IEnumerable<SecurityKey> Resolve(
        string token,
        SecurityToken securityToken,
        string? kid,
        TokenValidationParameters parameters)
    {
        var jwks = _manager.GetConfigurationAsync(CancellationToken.None).GetAwaiter().GetResult();
        var keys = jwks.GetSigningKeys();
        if (kid is null)
        {
            return keys;
        }

        var matched = keys.Where(k => string.Equals(k.KeyId, kid, StringComparison.Ordinal)).ToList();
        return matched.Count > 0 ? matched : keys;
    }

    private sealed class JwksDocumentRetriever : IConfigurationRetriever<JsonWebKeySet>
    {
        public async Task<JsonWebKeySet> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
        {
            var json = await retriever.GetDocumentAsync(address, cancel);
            return new JsonWebKeySet(json);
        }
    }
}
