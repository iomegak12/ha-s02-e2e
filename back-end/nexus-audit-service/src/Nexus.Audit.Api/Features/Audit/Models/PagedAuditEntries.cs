namespace Nexus.Audit.Api.Features.Audit.Models;

/// <summary>
/// Paged response returned by <c>queryAuditEntries</c>.
/// </summary>
public sealed record PagedAuditEntries(
    IReadOnlyList<AuditEntryListItem> Items,
    int Page,
    int Size,
    long TotalCount);
