namespace Nexus.Identity.Api.Configuration;

/// <summary>
/// Outbound HTTP resilience configuration. Bound from <c>Resilience</c>.
/// Currently only the <c>Audit</c> client is configured here.
/// </summary>
public sealed class ResilienceOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Resilience";

    /// <summary>Audit-client resilience settings.</summary>
    public ResilienceClientOptions Audit { get; init; } = new();
}

/// <summary>
/// Per-client resilience knobs mirroring <c>AddStandardResilienceHandler</c> defaults.
/// </summary>
public sealed class ResilienceClientOptions
{
    /// <summary>Hard upper bound across all retry attempts.</summary>
    public int TotalRequestTimeoutSeconds { get; init; } = 10;

    /// <summary>Per-attempt timeout.</summary>
    public int AttemptTimeoutSeconds { get; init; } = 3;

    /// <summary>Retry strategy.</summary>
    public ResilienceRetryOptions Retry { get; init; } = new();

    /// <summary>Circuit-breaker strategy.</summary>
    public ResilienceCircuitBreakerOptions CircuitBreaker { get; init; } = new();
}

/// <summary>Retry strategy configuration.</summary>
public sealed class ResilienceRetryOptions
{
    /// <summary>Maximum retry attempts after the initial request.</summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>Base delay between attempts.</summary>
    public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Backoff growth pattern (<c>Constant</c>, <c>Linear</c>, <c>Exponential</c>).</summary>
    public string BackoffType { get; init; } = "Exponential";

    /// <summary>Apply jitter to spread retries across callers.</summary>
    public bool UseJitter { get; init; } = true;
}

/// <summary>Circuit-breaker configuration.</summary>
public sealed class ResilienceCircuitBreakerOptions
{
    /// <summary>Failure ratio (0.0 - 1.0) that trips the breaker.</summary>
    public double FailureRatio { get; init; } = 0.5;

    /// <summary>Rolling sampling duration in seconds.</summary>
    public int SamplingDurationSeconds { get; init; } = 30;

    /// <summary>Minimum number of requests in the window before the breaker can trip.</summary>
    public int MinimumThroughput { get; init; } = 8;

    /// <summary>How long the breaker remains open before half-open probes.</summary>
    public int BreakDurationSeconds { get; init; } = 30;
}
