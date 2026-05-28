using Microsoft.EntityFrameworkCore;
using Nexus.Identity.Api.Infrastructure.Persistence;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;

namespace Nexus.Identity.Api.Features.Auth.Repository;

/// <summary>EF Core implementation of <see cref="IRefreshTokenRepository"/>.</summary>
public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IdentityDbContext _db;

    /// <summary>Creates the repository.</summary>
    public RefreshTokenRepository(IdentityDbContext db) => _db = db;

    /// <inheritdoc />
    public Task<RefreshToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        _db.RefreshTokens
            .Include(rt => rt.Admin)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken) =>
        await _db.RefreshTokens.AddAsync(token, cancellationToken);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
