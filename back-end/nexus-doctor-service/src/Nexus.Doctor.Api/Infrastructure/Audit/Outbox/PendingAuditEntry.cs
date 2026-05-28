namespace Nexus.Doctors.Api.Infrastructure.Audit.Outbox;

/// <summary>
/// Durable outbox row for audit publishing per LLD §14. Persisted in the same
/// transaction as the business write; drained by the Phase 3 reconciler.
/// </summary>
public sealed class PendingAuditEntry
{
    public Guid Id { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime NextRetryUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? LastError { get; set; }
}
