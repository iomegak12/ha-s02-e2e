using Nexus.Branches.Api.Infrastructure.Persistence;

namespace Nexus.Branches.Api.Features.Branches.Models;

/// <summary>
/// A hospital branch. Codes are immutable while a branch is active; a deactivated
/// branch frees its <see cref="Code"/> for re-use (filtered unique index on the
/// table ignores rows where <see cref="IsActive"/> is false).
/// </summary>
public sealed class Branch : IAuditedEntity
{
    /// <summary>Stable identifier (SQL <c>uniqueidentifier</c>).</summary>
    public Guid Id { get; set; }

    /// <summary>Short uppercase code used in public-facing identifiers (e.g. <c>BLR</c>).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>City the branch operates in.</summary>
    public string? City { get; set; }

    /// <summary>Soft-delete flag. A deactivated branch cannot be linked to but its
    /// historical references remain valid.</summary>
    public bool IsActive { get; set; } = true;

    /// <inheritdoc />
    public DateTime CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>SQL Server <c>rowversion</c> for optimistic concurrency / ETag.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
