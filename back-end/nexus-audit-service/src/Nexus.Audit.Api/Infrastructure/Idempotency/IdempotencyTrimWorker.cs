using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.Audit.Api.Configuration;
using Nexus.Audit.Api.Infrastructure.Persistence;

namespace Nexus.Audit.Api.Infrastructure.Idempotency;

/// <summary>
/// Background worker that deletes <c>IdempotencyRecords</c> older than
/// <c>Idempotency:RetentionDays</c>, running every
/// <c>Idempotency:TrimIntervalMinutes</c>. Audit entries themselves are
/// <b>never</b> deleted by this worker.
/// </summary>
public sealed class IdempotencyTrimWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<IdempotencyOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<IdempotencyTrimWorker> _logger;

    /// <summary>Create the worker.</summary>
    public IdempotencyTrimWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<IdempotencyOptions> options,
        TimeProvider clock,
        ILogger<IdempotencyTrimWorker> logger)
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
                var deleted = await TrimOnceAsync(stoppingToken);
                if (deleted > 0)
                {
                    _logger.LogInformation("IdempotencyTrimWorker removed {Count} expired records", deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IdempotencyTrimWorker iteration failed; will retry next interval");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(opts.TrimIntervalMinutes), _clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Run one trim pass and return the number of rows deleted.</summary>
    public async Task<int> TrimOnceAsync(CancellationToken ct)
    {
        var opts = _options.CurrentValue;
        var cutoff = _clock.GetUtcNow().UtcDateTime - TimeSpan.FromDays(opts.RetentionDays);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var stale = await db.IdempotencyRecords
            .Where(r => r.CreatedAtUtc < cutoff)
            .ToListAsync(ct);
        if (stale.Count == 0)
        {
            return 0;
        }

        db.IdempotencyRecords.RemoveRange(stale);
        await db.SaveChangesAsync(ct);
        return stale.Count;
    }
}
