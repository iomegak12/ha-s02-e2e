using Nexus.Branches.Api.Features.Branches.Models;
using Nexus.Branches.Api.Infrastructure.Pagination;

namespace Nexus.Branches.Api.Features.Branches.Repository;

/// <summary>Data access for <see cref="Branch"/>. The feature surface in Phase 4 wraps these calls.</summary>
public interface IBranchRepository
{
    /// <summary>Insert a new row.</summary>
    Task<Branch> AddAsync(Branch entity, CancellationToken ct);

    /// <summary>Fetch by id (tracked).</summary>
    Task<Branch?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Fetch by id (no-track).</summary>
    Task<Branch?> GetByIdNoTrackingAsync(Guid id, CancellationToken ct);

    /// <summary>Fetch by code among active rows; returns <c>null</c> if no active branch carries the code.</summary>
    Task<Branch?> GetActiveByCodeAsync(string code, CancellationToken ct);

    /// <summary>Persist changes on a tracked entity (no-op if nothing changed).</summary>
    Task<int> SaveChangesAsync(CancellationToken ct);

    /// <summary>Paged + filtered query.</summary>
    Task<PagedResult<Branch>> QueryAsync(BranchQuery query, CancellationToken ct);
}

/// <summary>Filter / paging shape consumed by <see cref="IBranchRepository.QueryAsync"/>.</summary>
public sealed record BranchQuery(
    bool? IsActive = null,
    string? CodeContains = null,
    int Page = 1,
    int Size = 20);
