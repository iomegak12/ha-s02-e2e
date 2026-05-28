using System.ComponentModel.DataAnnotations;

namespace Nexus.Audit.Api.Features.Audit.Models;

/// <summary>
/// Body for the admin-only redact hook (LLD §15 — Phase 8 patient-purge PII scrub).
/// Targets every audit entry matching <see cref="EntityType"/> + <see cref="EntityId"/>;
/// replaces <c>Summary</c> with <see cref="RedactionPlaceholder"/> and nulls <c>DiffJson</c>.
/// </summary>
public sealed class RedactAuditEntriesRequest
{
    /// <summary>Domain entity type, e.g. <c>Patient</c>.</summary>
    [Required] public string EntityType { get; set; } = string.Empty;

    /// <summary>Identifier of the entity whose audit trail should be scrubbed.</summary>
    [Required] public Guid EntityId { get; set; }

    /// <summary>Replacement placeholder for the <c>Summary</c> field. Defaults to <c>[REDACTED]</c>.</summary>
    public string? RedactionPlaceholder { get; set; }
}

/// <summary>Response shape of the redact hook.</summary>
public sealed class RedactAuditEntriesResponse
{
    public string EntityType { get; init; } = string.Empty;
    public Guid EntityId { get; init; }
    public int RowsRedacted { get; init; }
}
