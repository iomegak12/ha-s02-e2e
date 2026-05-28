namespace Nexus.Identity.Api.Infrastructure.Audit;

/// <summary>
/// Fire-and-forget gateway for emitting audit entries to the Audit service.
/// Implementations MUST swallow downstream failures so user-facing operations
/// never fail because the Audit service is unreachable; failures are logged.
/// </summary>
public interface IAuditPublisher
{
    /// <summary>Publish a single audit entry.</summary>
    Task PublishAsync(AuditEntryDto entry, CancellationToken cancellationToken);
}
