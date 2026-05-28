using System.Net.Http.Headers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Nexus.Doctors.Api.Configuration;
using Nexus.Doctors.Api.Infrastructure.Audit.Outbox;
using Polly;

namespace Nexus.Doctors.Api.Infrastructure.Audit;

/// <summary>
/// Wires the audit publisher chain (HTTP + outbox + reconciler worker), the
/// typed <see cref="HttpClient"/> with standard resilience handler, and the
/// readiness probe.
/// </summary>
public static class AuditRegistration
{
    /// <summary>Register audit publishing services.</summary>
    public static void Add(WebApplicationBuilder builder)
    {
        var auditCfg = builder.Configuration.GetSection(AuditClientOptions.SectionName).Get<AuditClientOptions>() ?? new AuditClientOptions();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddOptions<AuditClientOptions>()
            .Bind(builder.Configuration.GetSection(AuditClientOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (!auditCfg.Enabled)
        {
            builder.Services.AddSingleton<IAuditPublisher, NullAuditPublisher>();
            return;
        }

        // Concrete HttpAuditPublisher registered as typed HttpClient.
        builder.Services
            .AddHttpClient<HttpAuditPublisher>(HttpAuditPublisher.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(auditCfg.BaseUrl, UriKind.Absolute);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddStandardResilienceHandler(o =>
            {
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                o.Retry.MaxRetryAttempts = 3;
                o.Retry.Delay = TimeSpan.FromMilliseconds(200);
                o.Retry.UseJitter = true;
                o.Retry.BackoffType = DelayBackoffType.Exponential;
                o.CircuitBreaker.FailureRatio = 0.5;
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
                o.CircuitBreaker.MinimumThroughput = 10;
                o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
            });

        builder.Services.AddScoped<IPendingAuditOutbox, PendingAuditOutbox>();
        builder.Services.AddScoped<IAuditPublisher, OutboxAuditPublisher>();
        builder.Services.AddHostedService<PendingAuditWorker>();
    }

    /// <summary>
    /// Append the audit readiness probe. Reported as <c>Degraded</c> (not
    /// <c>Unhealthy</c>) so the service stays ready when audit is briefly down.
    /// </summary>
    public static void AddReadinessProbe(WebApplicationBuilder builder)
    {
        var auditCfg = builder.Configuration.GetSection(AuditClientOptions.SectionName).Get<AuditClientOptions>() ?? new AuditClientOptions();
        if (!auditCfg.Enabled || string.IsNullOrWhiteSpace(auditCfg.BaseUrl)) return;

        builder.Services
            .AddHealthChecks()
            .AddUrlGroup(
                uriOptions => uriOptions
                    .AddUri(new Uri(new Uri(auditCfg.BaseUrl), "/health/live"))
                    .UseHttpMethod(HttpMethod.Get),
                name: "audit-service",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "ready" });
    }
}
