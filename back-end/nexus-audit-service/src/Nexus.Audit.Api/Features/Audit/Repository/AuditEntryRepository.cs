using Microsoft.EntityFrameworkCore;
using Nexus.Audit.Api.Features.Audit.Models;
using Nexus.Audit.Api.Infrastructure.Pagination;
using Nexus.Audit.Api.Infrastructure.Persistence;

namespace Nexus.Audit.Api.Features.Audit.Repository;

/// <summary>EF Core implementation of <see cref="IAuditEntryRepository"/>.</summary>
public sealed class AuditEntryRepository : IAuditEntryRepository
{
    private readonly AuditDbContext _db;

    /// <summary>Creates a new <see cref="AuditEntryRepository"/>.</summary>
    public AuditEntryRepository(AuditDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<AuditEntry> AddAsync(AuditEntry entry, CancellationToken ct)
    {
        _db.AuditEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry;
    }

    /// <inheritdoc />
    public Task<AuditEntry?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.AuditEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    /// <inheritdoc />
    public async Task<int> RedactByEntityAsync(string entityType, Guid entityId, string redactionPlaceholder, CancellationToken ct)
    {
        var rows = await _db.AuditEntries
            .Where(e => e.EntityType == entityType && e.EntityId == entityId)
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            row.Summary = redactionPlaceholder;
            row.DiffJson = null;
        }
        await _db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <inheritdoc />
    public async Task<PagedResult<AuditEntryListItem>> QueryAsync(AuditQuery query, CancellationToken ct)
    {
        var q = _db.AuditEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            q = q.Where(e => e.EntityType == query.EntityType);
        }

        if (query.EntityId is { } entityId)
        {
            q = q.Where(e => e.EntityId == entityId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            q = q.Where(e => e.Action == query.Action);
        }

        if (query.ActorId is { } actorId)
        {
            q = q.Where(e => e.ActorId == actorId);
        }

        if (query.From is { } from)
        {
            q = q.Where(e => e.OccurredAtUtc >= from);
        }

        if (query.To is { } to)
        {
            q = q.Where(e => e.OccurredAtUtc <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.SourceService))
        {
            var src = query.SourceService.ToLowerInvariant();
            q = q.Where(e => e.SourceService == src);
        }

        var page = new PageRequest(query.Page, query.Size);
        var total = await q.LongCountAsync(ct);

        // Default sort: occurredAtUtc desc. Any other sort accepted in Phase 4 by SortParser.
        var ordered = q.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id);

        var items = await ordered
            .Skip(page.Skip)
            .Take(page.NormalizedSize)
            .Select(e => new AuditEntryListItem(
                e.Id,
                e.EntityType,
                e.EntityId,
                e.EntityCode,
                e.Action,
                e.ActorId,
                e.ActorUsername,
                e.OccurredAtUtc,
                e.ReceivedAtUtc,
                e.Summary,
                e.SourceService))
            .ToListAsync(ct);

        return new PagedResult<AuditEntryListItem>(items, page.NormalizedPage, page.NormalizedSize, total);
    }
}
