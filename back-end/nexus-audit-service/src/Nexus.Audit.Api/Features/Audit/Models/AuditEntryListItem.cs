namespace Nexus.Audit.Api.Features.Audit.Models;

/// <summary>
/// Lightweight projection used by <c>queryAuditEntries</c>. Omits <c>DiffJson</c>
/// to keep list payloads small; clients fetch full entries via
/// <c>getAuditEntryById</c> when they need the diff.
/// </summary>
public sealed record AuditEntryListItem(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string? EntityCode,
    string Action,
    Guid ActorId,
    string ActorUsername,
    DateTime OccurredAtUtc,
    DateTime ReceivedAtUtc,
    string Summary,
    string SourceService);
