namespace Nexus.Branches.Api.Infrastructure.Pagination;

/// <summary>
/// Common pagination query parameters. 1-based page indexing, page size capped.
/// </summary>
public sealed record PageRequest(int Page = 1, int Size = 20)
{
    /// <summary>Maximum allowed page size per LLD §6.</summary>
    public const int MaxSize = 100;

    /// <summary>Normalised, clamped page number (≥ 1).</summary>
    public int NormalizedPage => Page < 1 ? 1 : Page;

    /// <summary>Normalised, clamped page size (1 ≤ size ≤ <see cref="MaxSize"/>).</summary>
    public int NormalizedSize => Size < 1 ? 1 : (Size > MaxSize ? MaxSize : Size);

    /// <summary>EF Core <c>Skip</c> value.</summary>
    public int Skip => (NormalizedPage - 1) * NormalizedSize;
}
