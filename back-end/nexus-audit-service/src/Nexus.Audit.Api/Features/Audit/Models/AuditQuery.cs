namespace Nexus.Audit.Api.Features.Audit.Models;

/// <summary>
/// Filter / pagination shape consumed by <c>IAuditEntryRepository.QueryAsync</c>.
/// All filter fields are optional; if both <see cref="From"/> and <see cref="To"/>
/// are supplied they are inclusive bounds on <see cref="AuditEntry.OccurredAtUtc"/>.
/// </summary>
public sealed record AuditQuery(
    string? EntityType = null,
    Guid? EntityId = null,
    string? Action = null,
    Guid? ActorId = null,
    DateTime? From = null,
    DateTime? To = null,
    string? SourceService = null,
    int Page = 1,
    int Size = 20,
    string? Sort = null);
