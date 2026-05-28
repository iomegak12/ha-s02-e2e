using System.Net.Http.Headers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Nexus.Patients.Api.Configuration;
using Polly;

namespace Nexus.Patients.Api.Infrastructure.Branches;

/// <summary>Wires <see cref="BranchesClient"/> with Polly resilience and the readiness probe.</summary>
public static class BranchesClientRegistration
{
    /// <summary>Register the typed HttpClient.</summary>
    public static void Add(WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<BranchesClientOptions>()
            .Bind(builder.Configuration.GetSection(BranchesClientOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var cfg = builder.Configuration.GetSection(BranchesClientOptions.SectionName).Get<BranchesClientOptions>()
                  ?? new BranchesClientOptions();

        builder.Services
            .AddHttpClient<IBranchesClient, BranchesClient>(BranchesClient.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(cfg.BaseUrl, UriKind.Absolute);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.Timeout = TimeSpan.FromSeconds(cfg.TotalRequestTimeoutSeconds + 5);
            })
            .AddStandardResilienceHandler(o =>
            {
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(cfg.TotalRequestTimeoutSeconds);
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(cfg.AttemptTimeoutSeconds);
                o.Retry.MaxRetryAttempts = cfg.MaxRetryAttempts;
                o.Retry.Delay = TimeSpan.FromMilliseconds(200);
                o.Retry.UseJitter = true;
                o.Retry.BackoffType = DelayBackoffType.Exponential;
                o.CircuitBreaker.FailureRatio = 0.3;
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
                o.CircuitBreaker.MinimumThroughput = 5;
                o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
            });
    }

    /// <summary>Append the Branches readiness probe (Degraded — not Unhealthy).</summary>
    public static void AddReadinessProbe(WebApplicationBuilder builder)
    {
        var cfg = builder.Configuration.GetSection(BranchesClientOptions.SectionName).Get<BranchesClientOptions>()
                  ?? new BranchesClientOptions();
        if (string.IsNullOrWhiteSpace(cfg.BaseUrl)) return;

        builder.Services
            .AddHealthChecks()
            .AddUrlGroup(
                uriOptions => uriOptions
                    .AddUri(new Uri(new Uri(cfg.BaseUrl), "/health/live"))
                    .UseHttpMethod(HttpMethod.Get),
                name: "branches-service",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "ready" });
    }
}
