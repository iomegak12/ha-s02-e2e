using Nexus.Audit.Api.Features.Audit.Models;

namespace Nexus.Audit.Api.Features.Audit.Repository;

/// <summary>
/// Lookup + add for <see cref="IdempotencyRecord"/>. Replays are recognised by the
/// service comparing <see cref="IdempotencyRecord.PayloadHash"/>.
/// </summary>
public interface IIdempotencyRepository
{
    /// <summary>Returns the existing record for the given pair, or <c>null</c>.</summary>
    Task<IdempotencyRecord?> FindAsync(string sourceService, string idempotencyKey, CancellationToken ct);

    /// <summary>Inserts a new record.</summary>
    Task<IdempotencyRecord> AddAsync(IdempotencyRecord record, CancellationToken ct);
}
