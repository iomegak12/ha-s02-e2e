using Nexus.Audit.Api.Features.Audit.Models;
using Nexus.Audit.Api.Infrastructure.Idempotency;
using Nexus.Audit.Api.Infrastructure.Pagination;

namespace Nexus.Audit.Api.Features.Audit.Service;

/// <summary>
/// Application-layer surface for the audit feature. Encapsulates the
/// idempotency state machine and the read-side queries.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Append (or replay) an audit entry. The state machine is:
    /// <list type="bullet">
    ///   <item>No existing idempotency record → insert and return a result with <c>IsReplay = false</c>.</item>
    ///   <item>Existing record with matching payload → return the original entry with <c>IsReplay = true</c>.</item>
    ///   <item>Existing record with a different payload → throw <c>409 IDEMPOTENCY_CONFLICT</c>.</item>
    /// </list>
    /// </summary>
    Task<AppendAuditEntryResult> AppendAsync(
        AppendAuditEntryRequest request,
        IdempotencyContext idempotency,
        CancellationToken ct);

    /// <summary>Return the entry by id, or <c>null</c> if absent.</summary>
    Task<AuditEntry?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Return a filtered, paged slice of entries.</summary>
    Task<PagedResult<AuditEntryListItem>> QueryAsync(AuditQuery query, CancellationToken ct);

    /// <summary>
    /// Redact PII for every entry matching <paramref name="entityType"/> + <paramref name="entityId"/>.
    /// Returns the number of rows touched. Idempotent — re-running on the same entity is a no-op once redacted.
    /// </summary>
    Task<int> RedactByEntityAsync(string entityType, Guid entityId, string redactionPlaceholder, CancellationToken ct);
}
