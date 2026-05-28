namespace Nexus.Identity.Api.Infrastructure.Audit;

/// <summary>
/// No-op publisher used when <c>AuditClient.Enabled</c> is <c>false</c>
/// (typically Development before the Audit service is built). Logs at debug.
/// </summary>
public sealed class NullAuditPublisher : IAuditPublisher
{
    private readonly ILogger<NullAuditPublisher> _logger;

    /// <summary>Create the publisher.</summary>
    public NullAuditPublisher(ILogger<NullAuditPublisher> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task PublishAsync(AuditEntryDto entry, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Audit (disabled) {Action} {EntityType} {EntityId} by {ActorUsername}: {Summary}",
            entry.Action, entry.EntityType, entry.EntityId, entry.ActorUsername, entry.Summary);
        return Task.CompletedTask;
    }
}
