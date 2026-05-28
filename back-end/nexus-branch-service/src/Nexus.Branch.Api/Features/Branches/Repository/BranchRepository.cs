using Microsoft.EntityFrameworkCore;
using Nexus.Branches.Api.Features.Branches.Models;
using Nexus.Branches.Api.Infrastructure.Pagination;
using Nexus.Branches.Api.Infrastructure.Persistence;

namespace Nexus.Branches.Api.Features.Branches.Repository;

/// <summary>EF Core implementation of <see cref="IBranchRepository"/>.</summary>
public sealed class BranchRepository : IBranchRepository
{
    private readonly BranchDbContext _db;

    /// <summary>Create a new <see cref="BranchRepository"/>.</summary>
    public BranchRepository(BranchDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Branch> AddAsync(Branch entity, CancellationToken ct)
    {
        _db.Branches.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    /// <inheritdoc />
    public Task<Branch?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Branches.FirstOrDefaultAsync(b => b.Id == id, ct);

    /// <inheritdoc />
    public Task<Branch?> GetByIdNoTrackingAsync(Guid id, CancellationToken ct) =>
        _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);

    /// <inheritdoc />
    public Task<Branch?> GetActiveByCodeAsync(string code, CancellationToken ct) =>
        _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.IsActive && b.Code == code, ct);

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    /// <inheritdoc />
    public async Task<PagedResult<Branch>> QueryAsync(BranchQuery query, CancellationToken ct)
    {
        var q = _db.Branches.AsNoTracking().AsQueryable();

        if (query.IsActive is { } isActive) q = q.Where(b => b.IsActive == isActive);
        if (!string.IsNullOrWhiteSpace(query.CodeContains))
        {
            var pattern = query.CodeContains.Trim();
            q = q.Where(b => b.Code.Contains(pattern));
        }

        var page = new PageRequest(query.Page, query.Size);
        var total = await q.LongCountAsync(ct);
        var items = await q.OrderBy(b => b.Code).Skip(page.Skip).Take(page.NormalizedSize).ToListAsync(ct);
        return new PagedResult<Branch>(items, page.NormalizedPage, page.NormalizedSize, total);
    }
}
