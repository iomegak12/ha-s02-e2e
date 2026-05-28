using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nexus.Identity.Api.Configuration;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;

namespace Nexus.Identity.Api.Features.Auth.Service;

/// <summary>Issues short-lived access JWTs signed with the active RSA key.</summary>
public interface IJwtIssuer
{
    /// <summary>Issue an access token for the given admin.</summary>
    /// <returns>Tuple of compact JWS string and lifetime in seconds.</returns>
    Task<(string Token, int ExpiresInSeconds)> IssueAsync(Admin admin, CancellationToken cancellationToken);
}

/// <summary>Default <see cref="IJwtIssuer"/>. Uses <see cref="JsonWebTokenHandler"/> for emission.</summary>
public sealed class JwtIssuer : IJwtIssuer
{
    private readonly ISigningKeyStore _store;
    private readonly IOptionsMonitor<JwtIssuerOptions> _options;
    private readonly TimeProvider _clock;

    /// <summary>Create the issuer.</summary>
    public JwtIssuer(ISigningKeyStore store, IOptionsMonitor<JwtIssuerOptions> options, TimeProvider clock)
    {
        _store = store;
        _options = options;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<(string Token, int ExpiresInSeconds)> IssueAsync(Admin admin, CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        var signing = await _store.GetActiveAsync(cancellationToken);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(Encoding.UTF8.GetString(signing.PrivateKeyPemEncrypted));

        var securityKey = new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: true))
        {
            KeyId = signing.Kid,
        };
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

        var now = _clock.GetUtcNow().UtcDateTime;
        var lifetime = TimeSpan.FromMinutes(opts.AccessTokenLifetimeMinutes);
        var expires = now.Add(lifetime);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = opts.Issuer,
            Audience = opts.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = admin.Id.ToString(),
                ["preferred_username"] = admin.Username,
                ["name"] = admin.DisplayName,
                ["role"] = "Admin",
                ["jti"] = Guid.NewGuid().ToString("N"),
            },
        };

        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        var token = handler.CreateToken(descriptor);

        return (token, (int)lifetime.TotalSeconds);
    }
}
