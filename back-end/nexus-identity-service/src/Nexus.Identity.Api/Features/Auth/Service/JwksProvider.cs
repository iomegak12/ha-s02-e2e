using System.Text.Json;
using Nexus.Identity.Api.Features.Auth.Models;

namespace Nexus.Identity.Api.Features.Auth.Service;

/// <summary>Returns the published JWKS for token verification.</summary>
public interface IJwksProvider
{
    /// <summary>Build the JWKS response.</summary>
    Task<JwksResponse> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Default <see cref="IJwksProvider"/> backed by <see cref="ISigningKeyStore"/>.</summary>
public sealed class JwksProvider : IJwksProvider
{
    private readonly ISigningKeyStore _store;
    private readonly ILogger<JwksProvider> _logger;

    /// <summary>Create the provider.</summary>
    public JwksProvider(ISigningKeyStore store, ILogger<JwksProvider> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<JwksResponse> GetAsync(CancellationToken cancellationToken)
    {
        var keys = await _store.GetPublishedAsync(cancellationToken);
        var jwks = new List<JwkKey>(keys.Count);

        foreach (var k in keys)
        {
            try
            {
                var jwk = JsonSerializer.Deserialize<JwkKey>(k.PublicJwkJson);
                if (jwk is not null)
                {
                    jwks.Add(jwk);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping malformed JWK for kid {Kid}", k.Kid);
            }
        }

        return new JwksResponse { Keys = jwks };
    }
}
