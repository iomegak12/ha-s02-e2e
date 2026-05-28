namespace Nexus.Doctors.Api.Infrastructure.Audit;

/// <summary>
/// Fire-and-forget publisher for audit events. Implementations are <b>fail-open</b>:
/// they never throw to the caller. Durability is provided by the
/// <see cref="Outbox.PendingAuditEntry"/> outbox + reconciler worker.
/// </summary>
public interface IAuditPublisher
{
    /// <summary>Publish (or queue) the event. Never throws to the caller.</summary>
    Task PublishAsync(AuditEventDto entry, CancellationToken ct);
}
