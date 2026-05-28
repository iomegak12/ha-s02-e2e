using Nexus.Audit.Api.Features.Audit.Models;
using Nexus.Audit.Api.Infrastructure.Pagination;

namespace Nexus.Audit.Api.Features.Audit.Repository;

/// <summary>
/// Append-only access to <see cref="AuditEntry"/>. By design exposes
/// <c>Add</c> / <c>Get</c> / <c>Query</c> only — no <c>Update</c> or <c>Delete</c>.
/// </summary>
public interface IAuditEntryRepository
{
    /// <summary>Inserts a new entry. The entry is expected to have an assigned <see cref="AuditEntry.Id"/>.</summary>
    Task<AuditEntry> AddAsync(AuditEntry entry, CancellationToken ct);

    /// <summary>Returns the entry by id, or <c>null</c> if absent.</summary>
    Task<AuditEntry?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Returns a filtered, paged slice of entries projected as list items.</summary>
    Task<PagedResult<AuditEntryListItem>> QueryAsync(AuditQuery query, CancellationToken ct);

    /// <summary>
    /// Redact PII for every entry matching <paramref name="entityType"/> + <paramref name="entityId"/>:
    /// replaces <c>Summary</c> with the supplied placeholder and nulls <c>DiffJson</c>. Returns the
    /// number of rows touched. This is the **only** allowed mutation on the append-only log.
    /// </summary>
    Task<int> RedactByEntityAsync(string entityType, Guid entityId, string redactionPlaceholder, CancellationToken ct);
}
