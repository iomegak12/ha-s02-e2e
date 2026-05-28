namespace Nexus.Branches.Api.Infrastructure.Pagination;

/// <summary>
/// Generic paged-list envelope returned by list endpoints.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int Size, long Total);
