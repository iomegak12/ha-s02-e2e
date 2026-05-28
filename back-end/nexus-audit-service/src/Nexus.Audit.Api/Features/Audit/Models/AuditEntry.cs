namespace Nexus.Audit.Api.Features.Audit.Models;

/// <summary>
/// One row of the append-only audit log. Persisted to <c>AuditEntries</c> in the
/// <c>NexusAudit</c> database and never mutated after insert.
/// </summary>
public sealed class AuditEntry
{
    /// <summary>Server-assigned identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Domain entity type (e.g. <c>Patient</c>, <c>Doctor</c>, <c>Branch</c>).</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Identifier of the affected entity.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Optional human-readable handle (e.g. branch code) — never required.</summary>
    public string? EntityCode { get; set; }

    /// <summary>One of <see cref="AuditActions"/>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Identifier of the admin that performed the change.</summary>
    public Guid ActorId { get; set; }

    /// <summary>Username of the admin (denormalised for query convenience).</summary>
    public string ActorUsername { get; set; } = string.Empty;

    /// <summary>When the business event occurred (caller-supplied).</summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>When the audit service received the entry (server-stamped).</summary>
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Short human-readable summary of the change.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Optional structured diff (JSON) of before/after state.</summary>
    public string? DiffJson { get; set; }

    /// <summary>Lower-cased identifier of the calling service.</summary>
    public string SourceService { get; set; } = string.Empty;

    /// <summary>Idempotency key supplied by the caller; unique per source service.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
