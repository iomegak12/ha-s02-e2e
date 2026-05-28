using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nexus.Identity.Api.Configuration;
using Nexus.Identity.Api.Features.Auth.Models;
using Nexus.Identity.Api.Features.Auth.Repository;
using Nexus.Identity.Api.Infrastructure.Errors;
using Nexus.Identity.Api.Infrastructure.Persistence;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;
using Nexus.Identity.Api.Infrastructure.Security;

namespace Nexus.Identity.Api.Features.Auth.Service;

/// <summary>Default <see cref="IAuthService"/> implementation.</summary>
public sealed class AuthService : IAuthService
{
    private const int RefreshTokenBytes = 32;

    private readonly IdentityDbContext _db;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtIssuer _jwtIssuer;
    private readonly IOptionsMonitor<JwtIssuerOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<AuthService> _logger;

    /// <summary>Create the service.</summary>
    public AuthService(
        IdentityDbContext db,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher passwordHasher,
        IJwtIssuer jwtIssuer,
        IOptionsMonitor<JwtIssuerOptions> options,
        TimeProvider clock,
        ILogger<AuthService> logger)
    {
        _db = db;
        _refreshTokens = refreshTokens;
        _passwordHasher = passwordHasher;
        _jwtIssuer = jwtIssuer;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TokenResponse> IssueAsync(TokenRequest request, CancellationToken cancellationToken)
    {
        var admin = await _db.Admins
            .FirstOrDefaultAsync(a => a.Username == request.Username, cancellationToken);

        if (admin is null || !admin.IsActive || !_passwordHasher.Verify(request.Password, admin.PasswordHash))
        {
            throw InvalidCredentials();
        }

        return await CreateTokenPairAsync(admin, replacing: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TokenResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var hash = ComputeHash(request.RefreshToken);
        var existing = await _refreshTokens.GetByHashAsync(hash, cancellationToken);

        var now = _clock.GetUtcNow().UtcDateTime;
        if (existing is null || existing.RevokedAtUtc is not null || existing.ExpiresAtUtc <= now
            || existing.Admin is null || !existing.Admin.IsActive)
        {
            throw RefreshInvalid();
        }

        var pair = await CreateTokenPairAsync(existing.Admin, replacing: existing, cancellationToken);
        return pair;
    }

    /// <inheritdoc />
    public async Task RevokeAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var hash = ComputeHash(request.RefreshToken);
        var existing = await _refreshTokens.GetByHashAsync(hash, cancellationToken);

        if (existing is null || existing.RevokedAtUtc is not null)
        {
            return; // idempotent
        }

        existing.RevokedAtUtc = _clock.GetUtcNow().UtcDateTime;
        await _refreshTokens.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Refresh token {TokenId} revoked", existing.Id);
    }

    private async Task<TokenResponse> CreateTokenPairAsync(Admin admin, RefreshToken? replacing, CancellationToken cancellationToken)
    {
        var (accessToken, expiresInSeconds) = await _jwtIssuer.IssueAsync(admin, cancellationToken);

        var now = _clock.GetUtcNow().UtcDateTime;
        var plainRefresh = GenerateRefreshTokenPlaintext();
        var refresh = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AdminId = admin.Id,
            TokenHash = ComputeHash(plainRefresh),
            IssuedAtUtc = now,
            ExpiresAtUtc = now.AddDays(_options.CurrentValue.RefreshTokenLifetimeDays),
        };

        await _refreshTokens.AddAsync(refresh, cancellationToken);

        if (replacing is not null)
        {
            replacing.RevokedAtUtc = now;
            replacing.ReplacedByTokenId = refresh.Id;
        }

        await _refreshTokens.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Issued token pair for admin {AdminId}; refresh {RefreshId} (replacing {Replaced})",
            admin.Id, refresh.Id, replacing?.Id);

        return new TokenResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresIn = expiresInSeconds,
            RefreshToken = plainRefresh,
        };
    }

    private static string GenerateRefreshTokenPlaintext()
    {
        Span<byte> buffer = stackalloc byte[RefreshTokenBytes];
        RandomNumberGenerator.Fill(buffer);
        return Base64UrlEncoder.Encode(buffer.ToArray());
    }

    private static byte[] ComputeHash(string plaintext) =>
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plaintext));

    private static DomainException InvalidCredentials() => new(
        code: ErrorCodes.InvalidCredentials,
        status: StatusCodes.Status401Unauthorized,
        title: "Unauthorized",
        detail: "Username or password is incorrect.");

    private static DomainException RefreshInvalid() => new(
        code: ErrorCodes.RefreshInvalid,
        status: StatusCodes.Status401Unauthorized,
        title: "Unauthorized",
        detail: "Refresh token is no longer valid.");
}
