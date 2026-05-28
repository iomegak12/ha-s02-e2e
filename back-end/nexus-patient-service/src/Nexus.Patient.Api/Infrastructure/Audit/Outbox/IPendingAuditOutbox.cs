namespace Nexus.Patients.Api.Infrastructure.Audit.Outbox;

/// <summary>
/// Durable buffer for audit events that failed their synchronous publish.
/// Drained by <see cref="PendingAuditWorker"/>.
/// </summary>
public interface IPendingAuditOutbox
{
    /// <summary>Persist a new outbox row for the given event.</summary>
    Task EnqueueAsync(AuditEventDto entry, string idempotencyKey, CancellationToken ct);

    /// <summary>Return rows due for retry (<see cref="PendingAuditEntry.NextRetryUtc"/> ≤ now), capped by <paramref name="batchSize"/>.</summary>
    Task<IReadOnlyList<PendingAuditEntry>> FetchDueAsync(int batchSize, CancellationToken ct);

    /// <summary>Delete a row after a successful publish.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Mark a row as failed: increment <c>Attempts</c>, set <c>NextRetryUtc</c>
    /// using exponential backoff, capture the last error.
    /// </summary>
    Task BackoffAsync(Guid id, string? error, CancellationToken ct);
}
