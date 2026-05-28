namespace Nexus.Patients.Api.Infrastructure.Audit;

/// <summary>No-op publisher used when <c>AuditClient:Enabled</c> is false.</summary>
public sealed class NullAuditPublisher : IAuditPublisher
{
    /// <inheritdoc />
    public Task PublishAsync(AuditEventDto entry, CancellationToken ct) => Task.CompletedTask;
}
