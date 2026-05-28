using Microsoft.EntityFrameworkCore;
using Nexus.Identity.Api.Infrastructure.Persistence;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;

namespace Nexus.Identity.Api.Features.Admins.Repository;

/// <summary>EF Core implementation of <see cref="IAdminRepository"/>.</summary>
public sealed class AdminRepository : IAdminRepository
{
    private readonly IdentityDbContext _db;

    /// <summary>Create the repository.</summary>
    public AdminRepository(IdentityDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<Admin?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => _db.Admins.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Admin?> GetByUsernameAsync(string username, CancellationToken cancellationToken)
        => _db.Admins.FirstOrDefaultAsync(a => a.Username == username, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken)
        => _db.Admins.AnyAsync(a => a.Username == username, cancellationToken);

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Admin> Items, long Total)> ListAsync(
        int page,
        int size,
        bool? isActive,
        AdminSortField sort,
        bool descending,
        CancellationToken cancellationToken)
    {
        IQueryable<Admin> query = _db.Admins.AsNoTracking();

        if (isActive is { } active)
        {
            query = query.Where(a => a.IsActive == active);
        }

        query = (sort, descending) switch
        {
            (AdminSortField.Username, false) => query.OrderBy(a => a.Username),
            (AdminSortField.Username, true) => query.OrderByDescending(a => a.Username),
            (AdminSortField.DisplayName, false) => query.OrderBy(a => a.DisplayName),
            (AdminSortField.DisplayName, true) => query.OrderByDescending(a => a.DisplayName),
            (AdminSortField.CreatedAtUtc, true) => query.OrderByDescending(a => a.CreatedAtUtc).ThenBy(a => a.Id),
            _ => query.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id),
        };

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, total);
    }

    /// <inheritdoc />
    public async Task AddAsync(Admin admin, CancellationToken cancellationToken)
    {
        await _db.Admins.AddAsync(admin, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken)
        => _db.SaveChangesAsync(cancellationToken);
}
