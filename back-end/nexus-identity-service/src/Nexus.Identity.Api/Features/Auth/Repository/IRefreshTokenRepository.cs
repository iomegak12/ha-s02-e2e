using Nexus.Identity.Api.Infrastructure.Persistence.Entities;

namespace Nexus.Identity.Api.Features.Auth.Repository;

/// <summary>
/// Persistence gateway for <see cref="RefreshToken"/> entities.
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>Look up a refresh token by its SHA-256 hash, including its owning admin.</summary>
    Task<RefreshToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken);

    /// <summary>Stage a new refresh token in the unit of work.</summary>
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken);

    /// <summary>Persist all staged changes.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
