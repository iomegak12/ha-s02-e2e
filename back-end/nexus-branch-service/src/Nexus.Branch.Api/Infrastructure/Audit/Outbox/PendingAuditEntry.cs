namespace Nexus.Branches.Api.Infrastructure.Audit.Outbox;

/// <summary>
/// Durable outbox row for audit publishing per LLD §14. Inserted whenever the
/// synchronous POST to <c>nexus-audit-service</c> fails; drained by
/// <see cref="PendingAuditWorker"/>.
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
