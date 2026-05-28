namespace Nexus.Doctors.Api.Infrastructure.Pagination;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int Size, long Total);
