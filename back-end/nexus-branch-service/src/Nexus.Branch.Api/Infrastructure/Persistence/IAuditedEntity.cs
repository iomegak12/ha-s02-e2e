namespace Nexus.Branches.Api.Infrastructure.Persistence;

/// <summary>
/// Marker for entities whose <c>CreatedAtUtc</c> / <c>UpdatedAtUtc</c> timestamps are
/// stamped automatically by <see cref="AuditedTimestampInterceptor"/> on save.
/// </summary>
public interface IAuditedEntity
{
    /// <summary>UTC timestamp set when the row is first inserted.</summary>
    DateTime CreatedAtUtc { get; set; }

    /// <summary>UTC timestamp updated on every persisted mutation.</summary>
    DateTime UpdatedAtUtc { get; set; }
}
