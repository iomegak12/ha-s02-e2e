using System.Diagnostics.Metrics;

namespace Nexus.Audit.Api.Infrastructure.Observability;

/// <summary>
/// Custom metrics emitted by the audit service. Counter names match
/// <c>docs/IMPLEMENTATION_PLAN.md</c> §10.
/// </summary>
public static class AuditMetrics
{
    /// <summary>Meter name exposed to the OTel pipeline.</summary>
    public const string MeterName = "Nexus.Audit";

    /// <summary>Singleton meter for audit-domain instruments.</summary>
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>Audit entries successfully appended, tagged by source service / entity / action.</summary>
    public static readonly Counter<long> EntriesAppended = Meter.CreateCounter<long>(
        "nexus_audit_entries_appended_total",
        unit: "{entries}",
        description: "Total audit entries appended.");

    /// <summary>Replays — identical payload re-sent under an existing idempotency key.</summary>
    public static readonly Counter<long> IdempotencyReplays = Meter.CreateCounter<long>(
        "nexus_audit_idempotency_replays_total",
        unit: "{replays}",
        description: "Total idempotent replays returned to callers.");

    /// <summary>Conflicts — different payload sent under an existing idempotency key.</summary>
    public static readonly Counter<long> IdempotencyConflicts = Meter.CreateCounter<long>(
        "nexus_audit_idempotency_conflicts_total",
        unit: "{conflicts}",
        description: "Total idempotency conflicts rejected.");
}
