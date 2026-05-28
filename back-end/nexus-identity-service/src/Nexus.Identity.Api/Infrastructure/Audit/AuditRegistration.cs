using System.Net.Http.Headers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Nexus.Identity.Api.Configuration;
using Polly;

namespace Nexus.Identity.Api.Infrastructure.Audit;

/// <summary>
/// Wires the audit publisher, its typed <see cref="HttpClient"/>, the
/// standard resilience handler pipeline and the readiness probe.
/// </summary>
public static class AuditRegistration
{
    /// <summary>Register audit publishing services.</summary>
    public static void Add(WebApplicationBuilder builder)
    {
        var audit = builder.Configuration.GetSection(AuditClientOptions.SectionName).Get<AuditClientOptions>() ?? new AuditClientOptions();
        var resilience = builder.Configuration.GetSection(ResilienceOptions.SectionName).Get<ResilienceOptions>() ?? new ResilienceOptions();

        builder.Services.AddHttpContextAccessor();

        if (!audit.Enabled)
        {
            builder.Services.AddSingleton<IAuditPublisher, NullAuditPublisher>();
            return;
        }

        builder.Services
            .AddHttpClient<IAuditPublisher, HttpAuditPublisher>(HttpAuditPublisher.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(audit.BaseUrl, UriKind.Absolute);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.Timeout = TimeSpan.FromSeconds(Math.Max(resilience.Audit.TotalRequestTimeoutSeconds + 5, 15));
            })
            .AddStandardResilienceHandler(o =>
            {
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(resilience.Audit.TotalRequestTimeoutSeconds);
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(resilience.Audit.AttemptTimeoutSeconds);

                o.Retry.MaxRetryAttempts = resilience.Audit.Retry.MaxRetryAttempts;
                o.Retry.Delay = resilience.Audit.Retry.Delay;
                o.Retry.UseJitter = resilience.Audit.Retry.UseJitter;
                o.Retry.BackoffType = ParseBackoff(resilience.Audit.Retry.BackoffType);

                o.CircuitBreaker.FailureRatio = resilience.Audit.CircuitBreaker.FailureRatio;
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(resilience.Audit.CircuitBreaker.SamplingDurationSeconds);
                o.CircuitBreaker.MinimumThroughput = resilience.Audit.CircuitBreaker.MinimumThroughput;
                o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(resilience.Audit.CircuitBreaker.BreakDurationSeconds);
            });
    }

    /// <summary>
    /// Append the audit readiness probe to the health-check pipeline when enabled.
    /// Reported as <c>Degraded</c> (not <c>Unhealthy</c>) so the service stays ready
    /// when the audit dependency is briefly unavailable.
    /// </summary>
    public static void AddReadinessProbe(WebApplicationBuilder builder)
    {
        var audit = builder.Configuration.GetSection(AuditClientOptions.SectionName).Get<AuditClientOptions>() ?? new AuditClientOptions();
        if (!audit.Enabled || string.IsNullOrWhiteSpace(audit.BaseUrl))
        {
            return;
        }

        builder.Services
            .AddHealthChecks()
            .AddUrlGroup(
                uriOptions =>
                {
                    uriOptions
                        .AddUri(new Uri(new Uri(audit.BaseUrl), "/api/v1/audit?size=1"))
                        .UseHttpMethod(HttpMethod.Head);
                },
                name: "audit-service",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "ready" });
    }

    private static DelayBackoffType ParseBackoff(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "linear" => DelayBackoffType.Linear,
        "constant" => DelayBackoffType.Constant,
        _ => DelayBackoffType.Exponential,
    };
}
