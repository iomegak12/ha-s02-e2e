using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nexus.Audit.Api.Features.Audit.Models;
using Nexus.Audit.Api.Features.Audit.Repository;
using Nexus.Audit.Api.Infrastructure.Errors;
using Nexus.Audit.Api.Infrastructure.Idempotency;
using Nexus.Audit.Api.Infrastructure.Observability;
using Nexus.Audit.Api.Infrastructure.Pagination;

namespace Nexus.Audit.Api.Features.Audit.Service;

/// <summary>
/// Default <see cref="IAuditService"/> implementation. Owns the idempotency
/// state machine and stamps server-only fields (<c>Id</c>, <c>ReceivedAtUtc</c>,
/// <c>SourceService</c>, <c>IdempotencyKey</c>) on inserted entries.
/// </summary>
public sealed class AuditService : IAuditService
{
    private static readonly JsonSerializerOptions HashJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IAuditEntryRepository _entries;
    private readonly IIdempotencyRepository _idempotency;
    private readonly TimeProvider _clock;
    private readonly ILogger<AuditService> _logger;

    /// <summary>Create a new <see cref="AuditService"/>.</summary>
    public AuditService(
        IAuditEntryRepository entries,
        IIdempotencyRepository idempotency,
        TimeProvider clock,
        ILogger<AuditService> logger)
    {
        _entries = entries;
        _idempotency = idempotency;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AppendAuditEntryResult> AppendAsync(
        AppendAuditEntryRequest request,
        IdempotencyContext idempotency,
        CancellationToken ct)
    {
        var payloadHash = ComputePayloadHash(request);
        var existing = await _idempotency.FindAsync(idempotency.SourceService, idempotency.IdempotencyKey, ct);

        if (existing is not null)
        {
            if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Idempotency conflict for {SourceService}/{Key}: stored hash differs",
                    idempotency.SourceService, idempotency.IdempotencyKey);
                AuditMetrics.IdempotencyConflicts.Add(1,
                    new KeyValuePair<string, object?>("sourceService", idempotency.SourceService));
                throw new DomainException(
                    code: ErrorCodes.IdempotencyConflict,
                    status: StatusCodes.Status409Conflict,
                    title: "Idempotency conflict",
                    detail: "The supplied Idempotency-Key was previously used with a different payload.");
            }

            var replayed = await _entries.GetByIdAsync(existing.EntryId, ct)
                ?? throw new InvalidOperationException(
                    $"Idempotency record references missing entry {existing.EntryId}.");
            AuditMetrics.IdempotencyReplays.Add(1,
                new KeyValuePair<string, object?>("sourceService", idempotency.SourceService));
            return new AppendAuditEntryResult(replayed, IsReplay: true);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var entry = new AuditEntry
        {
            Id = Guid.NewGuid(),
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            EntityCode = request.EntityCode,
            Action = request.Action,
            ActorId = request.ActorId,
            ActorUsername = request.ActorUsername,
            OccurredAtUtc = request.OccurredAtUtc,
            ReceivedAtUtc = now,
            Summary = request.Summary,
            DiffJson = request.DiffJson,
            SourceService = idempotency.SourceService,
            IdempotencyKey = idempotency.IdempotencyKey,
        };

        await _entries.AddAsync(entry, ct);
        await _idempotency.AddAsync(new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            SourceService = idempotency.SourceService,
            IdempotencyKey = idempotency.IdempotencyKey,
            EntryId = entry.Id,
            PayloadHash = payloadHash,
            CreatedAtUtc = now,
        }, ct);

        AuditMetrics.EntriesAppended.Add(1,
            new KeyValuePair<string, object?>("sourceService", idempotency.SourceService),
            new KeyValuePair<string, object?>("entityType", entry.EntityType),
            new KeyValuePair<string, object?>("action", entry.Action));

        return new AppendAuditEntryResult(entry, IsReplay: false);
    }

    /// <inheritdoc />
    public Task<AuditEntry?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _entries.GetByIdAsync(id, ct);

    /// <inheritdoc />
    public Task<PagedResult<AuditEntryListItem>> QueryAsync(AuditQuery query, CancellationToken ct) =>
        _entries.QueryAsync(query, ct);

    /// <inheritdoc />
    public Task<int> RedactByEntityAsync(string entityType, Guid entityId, string redactionPlaceholder, CancellationToken ct) =>
        _entries.RedactByEntityAsync(entityType, entityId, redactionPlaceholder, ct);

    /// <summary>SHA-256 (hex) over the canonical JSON form of the request.</summary>
    public static string ComputePayloadHash(AppendAuditEntryRequest request)
    {
        var json = JsonSerializer.Serialize(request, HashJsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
