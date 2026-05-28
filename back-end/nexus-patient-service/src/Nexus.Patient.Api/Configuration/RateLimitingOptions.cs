using System.ComponentModel.DataAnnotations;

namespace Nexus.Patients.Api.Configuration;

/// <summary>Fixed-window IP rate-limiting configuration. Off by default.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; init; } = false;

    [Range(1, 1_000_000)]
    public int PermitLimit { get; init; } = 100;

    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 60;
}
