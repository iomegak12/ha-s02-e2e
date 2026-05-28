using System.ComponentModel.DataAnnotations;

namespace Nexus.Audit.Api.Configuration;

/// <summary>
/// Tunables for the idempotency / clock-skew contract documented in
/// <c>docs/IMPLEMENTATION_PLAN.md</c> §21.
/// </summary>
public class IdempotencyOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Idempotency";

    /// <summary>How long idempotency records are retained before the trim worker deletes them.</summary>
    [Range(1, 365)]
    public int RetentionDays { get; set; } = 7;

    /// <summary>How far <c>occurredAtUtc</c> may be in the future (relative to the server clock).</summary>
    [Range(0, 60)]
    public int MaxClockSkewMinutes { get; set; } = 5;

    /// <summary>How often <c>IdempotencyTrimWorker</c> runs.</summary>
    [Range(1, 1440)]
    public int TrimIntervalMinutes { get; set; } = 60;
}
