namespace Nexus.Audit.Api.Features.Audit.Models;

/// <summary>
/// Backs the <c>(SourceService, IdempotencyKey)</c> uniqueness constraint and lets the
/// service detect replays without scanning <c>AuditEntries</c>. Trimmed by
/// <c>IdempotencyTrimWorker</c> after <c>Idempotency:RetentionDays</c>.
/// </summary>
public sealed class IdempotencyRecord
{
    /// <summary>Server-assigned identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Source service that supplied the key (lower-cased).</summary>
    public string SourceService { get; set; } = string.Empty;

    /// <summary>Idempotency key supplied by the caller.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>FK to the stored <see cref="AuditEntry.Id"/>.</summary>
    public Guid EntryId { get; set; }

    /// <summary>SHA-256 (hex) of the canonicalised request payload — used to detect conflicts.</summary>
    public string PayloadHash { get; set; } = string.Empty;

    /// <summary>When the record was first created (server-stamped).</summary>
    public DateTime CreatedAtUtc { get; set; }
}
