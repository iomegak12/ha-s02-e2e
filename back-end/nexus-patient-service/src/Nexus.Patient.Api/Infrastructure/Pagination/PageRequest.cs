namespace Nexus.Patients.Api.Infrastructure.Pagination;

/// <summary>Common 1-based pagination parameters with a capped page size.</summary>
public sealed record PageRequest(int Page = 1, int Size = 20)
{
    /// <summary>Maximum allowed page size.</summary>
    public const int MaxSize = 100;

    /// <summary>Normalised, clamped page number (≥ 1).</summary>
    public int NormalizedPage => Page < 1 ? 1 : Page;

    /// <summary>Normalised, clamped page size (1 ≤ size ≤ <see cref="MaxSize"/>).</summary>
    public int NormalizedSize => Size < 1 ? 1 : (Size > MaxSize ? MaxSize : Size);

    /// <summary>EF Core <c>Skip</c> value.</summary>
    public int Skip => (NormalizedPage - 1) * NormalizedSize;
}
