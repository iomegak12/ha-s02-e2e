using Nexus.Patients.Api.Infrastructure.Audit.Outbox;

namespace Nexus.Patients.Api.Infrastructure.Audit;

/// <summary>
/// Default <see cref="IAuditPublisher"/>: try synchronous HTTP publish first; on
/// failure persist the event to the outbox so the reconciler worker can retry.
/// Fail-open — exceptions are caught at the HTTP layer.
/// </summary>
public sealed class OutboxAuditPublisher : IAuditPublisher
{
    private readonly HttpAuditPublisher _http;
    private readonly IPendingAuditOutbox _outbox;
    private readonly ILogger<OutboxAuditPublisher> _logger;

    public OutboxAuditPublisher(
        HttpAuditPublisher http,
        IPendingAuditOutbox outbox,
        ILogger<OutboxAuditPublisher> logger)
    {
        _http = http;
        _outbox = outbox;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PublishAsync(AuditEventDto entry, CancellationToken ct)
    {
        var idempotencyKey = Guid.CreateVersion7().ToString();

        try
        {
            var ok = await _http.TryPublishAsync(entry, idempotencyKey, ct);
            if (ok) return;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }

        try
        {
            await _outbox.EnqueueAsync(entry, idempotencyKey, ct);
        }
        catch (Exception ex)
        {
            // Last-resort: log but don't throw to the caller (fail-open contract).
            _logger.LogError(ex,
                "OutboxAuditPublisher could not persist outbox row for {Action} {EntityType} {EntityId}",
                entry.Action, entry.EntityType, entry.EntityId);
        }
    }
}
