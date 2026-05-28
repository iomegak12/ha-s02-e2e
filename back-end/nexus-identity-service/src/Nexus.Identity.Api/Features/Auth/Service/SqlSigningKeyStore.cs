using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Nexus.Identity.Api.Features.Auth.Models;
using Nexus.Identity.Api.Infrastructure.Persistence;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;

namespace Nexus.Identity.Api.Features.Auth.Service;

/// <summary>
/// SQL Server implementation of <see cref="ISigningKeyStore"/>. On first call with no active
/// key the store self-bootstraps a new 2048-bit RSA key. Bootstrap is guarded by a
/// process-wide semaphore so concurrent first-callers do not race.
/// </summary>
/// <remarks>
/// Phase 3 stores the PEM as UTF-8 bytes in <see cref="SigningKey.PrivateKeyPemEncrypted"/>
/// without at-rest encryption. Phase 8 will wrap the bytes with DPAPI/AES.
/// </remarks>
public sealed class SqlSigningKeyStore : ISigningKeyStore
{
    private static readonly SemaphoreSlim BootstrapLock = new(1, 1);

    private readonly IdentityDbContext _db;
    private readonly ILogger<SqlSigningKeyStore> _logger;
    private readonly TimeProvider _clock;

    /// <summary>Create the store.</summary>
    public SqlSigningKeyStore(IdentityDbContext db, ILogger<SqlSigningKeyStore> logger, TimeProvider clock)
    {
        _db = db;
        _logger = logger;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<SigningKey> GetActiveAsync(CancellationToken cancellationToken)
    {
        var existing = await QueryActive().FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        await BootstrapLock.WaitAsync(cancellationToken);
        try
        {
            existing = await QueryActive().FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var fresh = CreateKey();
            _db.SigningKeys.Add(fresh);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Bootstrapped initial RSA signing key {Kid}", fresh.Kid);
            return fresh;
        }
        finally
        {
            BootstrapLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SigningKey>> GetPublishedAsync(CancellationToken cancellationToken)
    {
        var active = await GetActiveAsync(cancellationToken);

        var keys = await _db.SigningKeys
            .AsNoTracking()
            .Where(k => k.RetiredAtUtc == null)
            .OrderByDescending(k => k.ActivatedAtUtc)
            .ToListAsync(cancellationToken);

        if (keys.Count == 0)
        {
            keys.Add(active);
        }

        return keys;
    }

    private IQueryable<SigningKey> QueryActive() =>
        _db.SigningKeys
            .Where(k => k.RetiredAtUtc == null)
            .OrderByDescending(k => k.ActivatedAtUtc);

    private SigningKey CreateKey()
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        using var rsa = RSA.Create(2048);

        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        var kid = $"nexus-{now:yyyyMM}-{Guid.NewGuid().ToString("N").AsSpan(0, 8)}";
        var jwk = new JwkKey
        {
            Kty = "RSA",
            Use = "sig",
            Alg = "RS256",
            Kid = kid,
            N = Base64UrlEncoder.Encode(parameters.Modulus!),
            E = Base64UrlEncoder.Encode(parameters.Exponent!),
        };

        var pem = rsa.ExportRSAPrivateKeyPem();

        return new SigningKey
        {
            Kid = kid,
            Algorithm = "RS256",
            PublicJwkJson = JsonSerializer.Serialize(jwk),
            PrivateKeyPemEncrypted = Encoding.UTF8.GetBytes(pem),
            CreatedAtUtc = now,
            ActivatedAtUtc = now,
            RetiredAtUtc = null,
        };
    }
}
