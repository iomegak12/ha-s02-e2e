using Microsoft.EntityFrameworkCore;
using Nexus.Audit.Api.Features.Audit.Models;
using Nexus.Audit.Api.Infrastructure.Persistence;

namespace Nexus.Audit.Api.Features.Audit.Repository;

/// <summary>EF Core implementation of <see cref="IIdempotencyRepository"/>.</summary>
public sealed class IdempotencyRepository : IIdempotencyRepository
{
    private readonly AuditDbContext _db;

    /// <summary>Creates a new <see cref="IdempotencyRepository"/>.</summary>
    public IdempotencyRepository(AuditDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<IdempotencyRecord?> FindAsync(string sourceService, string idempotencyKey, CancellationToken ct)
    {
        var src = sourceService.ToLowerInvariant();
        return _db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.SourceService == src && r.IdempotencyKey == idempotencyKey, ct);
    }

    /// <inheritdoc />
    public async Task<IdempotencyRecord> AddAsync(IdempotencyRecord record, CancellationToken ct)
    {
        _db.IdempotencyRecords.Add(record);
        await _db.SaveChangesAsync(ct);
        return record;
    }
}
