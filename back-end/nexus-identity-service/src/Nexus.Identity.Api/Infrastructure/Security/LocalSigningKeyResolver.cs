using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Nexus.Identity.Api.Features.Auth.Models;
using Nexus.Identity.Api.Features.Auth.Service;

namespace Nexus.Identity.Api.Infrastructure.Security;

/// <summary>
/// Resolves <see cref="SecurityKey"/> instances for the JwtBearer middleware by
/// reading the currently-published signing keys from <see cref="ISigningKeyStore"/>.
/// Implements a short in-memory cache to avoid hitting the database on every
/// authenticated request.
/// </summary>
public sealed class LocalSigningKeyResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, RsaSecurityKey> _byKid = new(StringComparer.Ordinal);
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;
    private readonly object _refreshLock = new();

    /// <summary>Create the resolver.</summary>
    public LocalSigningKeyResolver(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// JwtBearer-compatible <see cref="IssuerSigningKeyResolver"/> delegate.
    /// </summary>
    public IEnumerable<SecurityKey> Resolve(string token, SecurityToken securityToken, string kid, TokenValidationParameters parameters)
    {
        EnsureLoaded();

        if (!string.IsNullOrEmpty(kid) && _byKid.TryGetValue(kid, out var match))
        {
            return new SecurityKey[] { match };
        }

        return _byKid.Values.Cast<SecurityKey>().ToArray();
    }

    private void EnsureLoaded()
    {
        if (_timeProvider.GetUtcNow() < _expiresAt && !_byKid.IsEmpty)
        {
            return;
        }

        lock (_refreshLock)
        {
            if (_timeProvider.GetUtcNow() < _expiresAt && !_byKid.IsEmpty)
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ISigningKeyStore>();
            var keys = store.GetPublishedAsync(CancellationToken.None).GetAwaiter().GetResult();

            _byKid.Clear();
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key.PublicJwkJson))
                {
                    continue;
                }

                JwkKey? jwk;
                try
                {
                    jwk = JsonSerializer.Deserialize<JwkKey>(key.PublicJwkJson);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (jwk is null || string.IsNullOrEmpty(jwk.N) || string.IsNullOrEmpty(jwk.E))
                {
                    continue;
                }

                var rsa = RSA.Create();
                rsa.ImportParameters(new RSAParameters
                {
                    Modulus = Base64UrlEncoder.DecodeBytes(jwk.N),
                    Exponent = Base64UrlEncoder.DecodeBytes(jwk.E),
                });

                _byKid[key.Kid] = new RsaSecurityKey(rsa) { KeyId = key.Kid };
            }

            _expiresAt = _timeProvider.GetUtcNow().Add(CacheTtl);
        }
    }
}
