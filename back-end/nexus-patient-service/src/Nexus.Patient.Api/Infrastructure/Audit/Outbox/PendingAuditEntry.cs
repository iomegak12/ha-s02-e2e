namespace Nexus.Patients.Api.Infrastructure.Audit.Outbox;

/// <summary>
/// Durable outbox row for audit publishing per LLD §14. Persisted in the same
/// transaction as the business write; drained by <c>PendingAuditWorker</c> in
/// Phase 3.
/// </summary>
public sealed class PendingAuditEntry
{
    /// <summary>Server-assigned identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Caller-side idempotency key (UUID v7 by convention) forwarded to the Audit service.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Serialised <c>appendAuditEntry</c> request body.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>Retry attempt count.</summary>
    public int Attempts { get; set; }

    /// <summary>Earliest UTC instant the reconciler may attempt this row.</summary>
    public DateTime NextRetryUtc { get; set; }

    /// <summary>UTC timestamp the row was first inserted.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Last error captured by the reconciler (for diagnostics; trimmed).</summary>
    public string? LastError { get; set; }
}
