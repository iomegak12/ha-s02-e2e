namespace Nexus.Patients.Api.Infrastructure.Pagination;

/// <summary>Generic paged-list envelope returned by list endpoints.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int Size, long Total);
