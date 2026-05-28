using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nexus.Branches.Api.Infrastructure.Persistence;

namespace Nexus.Branches.Api.Infrastructure.Audit.Outbox;

/// <summary>EF Core implementation of <see cref="IPendingAuditOutbox"/>.</summary>
public sealed class PendingAuditOutbox : IPendingAuditOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BranchDbContext _db;
    private readonly TimeProvider _clock;

    public PendingAuditOutbox(BranchDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(AuditEventDto entry, string idempotencyKey, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        _db.PendingAuditEntries.Add(new PendingAuditEntry
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            PayloadJson = JsonSerializer.Serialize(entry, JsonOptions),
            Attempts = 0,
            NextRetryUtc = now,
            CreatedAtUtc = now,
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingAuditEntry>> FetchDueAsync(int batchSize, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        return await _db.PendingAuditEntries
            .Where(p => p.NextRetryUtc <= now)
            .OrderBy(p => p.NextRetryUtc)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var row = await _db.PendingAuditEntries.FindAsync(new object?[] { id }, ct);
        if (row is null) return;
        _db.PendingAuditEntries.Remove(row);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task BackoffAsync(Guid id, string? error, CancellationToken ct)
    {
        var row = await _db.PendingAuditEntries.FindAsync(new object?[] { id }, ct);
        if (row is null) return;

        row.Attempts++;
        // Exponential backoff: 30s, 1m, 2m, 4m, 8m, 15m (capped).
        var delaySeconds = Math.Min(30 * (1 << Math.Min(row.Attempts - 1, 5)), 900);
        row.NextRetryUtc = _clock.GetUtcNow().UtcDateTime.AddSeconds(delaySeconds);
        row.LastError = Truncate(error, 1024);
        await _db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value[..max]);
}
