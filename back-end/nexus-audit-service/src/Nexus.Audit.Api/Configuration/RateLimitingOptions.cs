using System.ComponentModel.DataAnnotations;

namespace Nexus.Audit.Api.Configuration;

/// <summary>
/// Rate-limiting configuration. The non-append endpoints partition by IP; the
/// append endpoint partitions by <c>X-Source-Service</c> with a higher ceiling
/// (per <c>docs/IMPLEMENTATION_PLAN.md</c> §12).
/// </summary>
public sealed class RateLimitingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>Master toggle. When <c>false</c>, no requests are throttled.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Permitted requests per window per IP partition (read endpoints).</summary>
    [Range(1, 1_000_000)]
    public int PermitLimit { get; init; } = 100;

    /// <summary>Window length in seconds.</summary>
    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 60;

    /// <summary>Permitted appends per minute per source-service partition.</summary>
    [Range(1, 1_000_000)]
    public int AppendPermitLimit { get; init; } = 500;
}
