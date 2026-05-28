using System.ComponentModel.DataAnnotations;

namespace Nexus.Identity.Api.Configuration;

/// <summary>
/// Fixed-window IP rate-limiting configuration. Bound from <c>RateLimiting</c>.
/// </summary>
public sealed class RateLimitingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>Master toggle. When <c>false</c>, the policy is registered but bypassed.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Permitted requests per window per partition (IP).</summary>
    [Range(1, 1_000_000)]
    public int PermitLimit { get; init; } = 100;

    /// <summary>Window length in seconds.</summary>
    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 60;
}
