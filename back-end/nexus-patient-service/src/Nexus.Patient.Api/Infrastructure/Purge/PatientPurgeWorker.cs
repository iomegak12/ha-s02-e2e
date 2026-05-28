using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.Patients.Api.Configuration;
using Nexus.Patients.Api.Features.Patients.Models;
using Nexus.Patients.Api.Infrastructure.Audit;
using Nexus.Patients.Api.Infrastructure.Observability;
using Nexus.Patients.Api.Infrastructure.Persistence;

namespace Nexus.Patients.Api.Infrastructure.Purge;

/// <summary>
/// Background worker that:
/// <list type="number">
///   <item>Scans for patients in <c>Archived</c> state older than <c>RetentionDays</c>.</item>
///   <item>Calls the audit service's PII redact hook for each one (best-effort).</item>
///   <item>Hard-deletes the patient and its branch links inside a single EF transaction.</item>
///   <item>Emits a single <c>Patient.Purged</c> audit event via the existing outbox publisher.</item>
/// </list>
/// Disabled by default — flip <c>Purge:Enabled</c> to <c>true</c> to activate.
/// </summary>
public sealed class PatientPurgeWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PurgeOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<PatientPurgeWorker> _logger;

    public PatientPurgeWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<PurgeOptions> options,
        TimeProvider clock,
        ILogger<PatientPurgeWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("PatientPurgeWorker disabled (Purge:Enabled=false).");
            return;
        }

        _logger.LogInformation(
            "PatientPurgeWorker starting. RetentionDays={RetentionDays} IntervalMinutes={IntervalMinutes} BatchSize={BatchSize}",
            _options.RetentionDays, _options.IntervalMinutes, _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PatientPurgeWorker cycle failed; continuing.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(_options.IntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Run one scan/purge cycle. Exposed for the worker test.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatientDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IAuditPublisher>();
        var redactor = scope.ServiceProvider.GetRequiredService<IAuditRedactorClient>();

        var cutoff = _clock.GetUtcNow().UtcDateTime.AddDays(-_options.RetentionDays);

        var due = await db.Patients
            .Where(p => p.Status == PatientStatus.Archived && p.ArchivedAtUtc != null && p.ArchivedAtUtc < cutoff)
            .OrderBy(p => p.ArchivedAtUtc)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0) return 0;

        var purged = 0;
        foreach (var patient in due)
        {
            try
            {
                // 1. Best-effort PII redaction in the audit trail. We tolerate failure here —
                // purge proceeds regardless so we don't pile up unpurged archived rows.
                try
                {
                    await redactor.RedactAsync("Patient", patient.Id, _options.RedactionPlaceholder, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Audit redact failed for patient {PatientId}; continuing purge.", patient.Id);
                }

                // 2. Hard-delete the patient + its branch links in a single transaction.
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    var links = await db.PatientBranchLinks.Where(l => l.PatientId == patient.Id).ToListAsync(ct);
                    db.PatientBranchLinks.RemoveRange(links);
                    db.Patients.Remove(patient);
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }

                // 3. Emit Patient.Purged audit event via the existing outbox chain.
                await publisher.PublishAsync(new AuditEventDto
                {
                    EntityType = "Patient",
                    EntityId = patient.Id,
                    EntityCode = patient.PublicCode,
                    Action = "Patient.Purged",
                    ActorId = Guid.Empty,
                    ActorUsername = "system:purge-worker",
                    OccurredAtUtc = _clock.GetUtcNow().UtcDateTime,
                    Summary = $"Patient {patient.PublicCode} purged after {_options.RetentionDays}-day retention.",
                }, ct);

                PatientMetrics.PatientsPurged.Add(1);
                purged++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to purge patient {PatientId}", patient.Id);
            }
        }

        if (purged > 0)
        {
            _logger.LogInformation("PatientPurgeWorker purged {Count} archived patient(s) in this cycle.", purged);
        }

        return purged;
    }
}
