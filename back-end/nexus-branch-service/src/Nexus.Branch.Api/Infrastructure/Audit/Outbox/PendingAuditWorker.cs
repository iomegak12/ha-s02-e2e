using System.Text.Json;
using Microsoft.Extensions.Options;
using Nexus.Branches.Api.Configuration;

namespace Nexus.Branches.Api.Infrastructure.Audit.Outbox;

/// <summary>
/// Background reconciler that drains <see cref="PendingAuditEntry"/> rows by
/// retrying the synchronous HTTP publish with the original idempotency key.
/// Exponential backoff via <see cref="IPendingAuditOutbox.BackoffAsync"/>.
/// </summary>
public sealed class PendingAuditWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AuditClientOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<PendingAuditWorker> _logger;

    public PendingAuditWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AuditClientOptions> options,
        TimeProvider clock,
        ILogger<PendingAuditWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = _options.CurrentValue;
            try
            {
                var drained = await DrainOnceAsync(opts.ReconcilerBatchSize, stoppingToken);
                if (drained > 0)
                {
                    _logger.LogInformation("PendingAuditWorker drained {Count} rows", drained);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PendingAuditWorker iteration failed; will retry next interval");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(opts.ReconcilerIntervalSeconds), _clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Run a single drain pass and return the number of rows successfully published.</summary>
    public async Task<int> DrainOnceAsync(int batchSize, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IPendingAuditOutbox>();
        var http = scope.ServiceProvider.GetRequiredService<HttpAuditPublisher>();

        var due = await outbox.FetchDueAsync(batchSize, ct);
        if (due.Count == 0) return 0;

        var drained = 0;
        foreach (var row in due)
        {
            ct.ThrowIfCancellationRequested();

            AuditEventDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<AuditEventDto>(row.PayloadJson, JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Corrupt outbox payload {Id}; dropping", row.Id);
                await outbox.DeleteAsync(row.Id, ct);
                continue;
            }
            if (dto is null)
            {
                await outbox.DeleteAsync(row.Id, ct);
                continue;
            }

            bool ok;
            try
            {
                ok = await http.TryPublishAsync(dto, row.IdempotencyKey, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await outbox.BackoffAsync(row.Id, ex.Message, ct);
                continue;
            }

            if (ok)
            {
                await outbox.DeleteAsync(row.Id, ct);
                drained++;
            }
            else
            {
                await outbox.BackoffAsync(row.Id, "publish returned false", ct);
            }
        }

        return drained;
    }
}
