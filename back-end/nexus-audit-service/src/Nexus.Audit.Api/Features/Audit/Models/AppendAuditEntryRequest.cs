namespace Nexus.Audit.Api.Features.Audit.Models;

/// <summary>
/// Payload accepted by <c>POST /api/v1/audit</c> (<c>appendAuditEntry</c>).
/// Header-supplied fields (<c>Idempotency-Key</c>, <c>X-Source-Service</c>) are
/// carried alongside this DTO by the controller.
/// </summary>
public sealed record AppendAuditEntryRequest(
    string EntityType,
    Guid EntityId,
    string? EntityCode,
    string Action,
    Guid ActorId,
    string ActorUsername,
    DateTime OccurredAtUtc,
    string Summary,
    string? DiffJson);
