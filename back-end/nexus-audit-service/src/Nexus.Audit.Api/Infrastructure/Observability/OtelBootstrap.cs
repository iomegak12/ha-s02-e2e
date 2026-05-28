using System.Reflection;
using Nexus.Audit.Api.Configuration;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Nexus.Audit.Api.Infrastructure.Observability;

/// <summary>
/// Wires OpenTelemetry traces and metrics. Logs are emitted via Serilog (see
/// <see cref="SerilogBootstrap"/>). Prometheus scraping is enabled when
/// <c>Observability:Prometheus:Enabled</c> is true; OTLP export is conditional on
/// <c>Observability:Otlp:Endpoint</c> being non-empty.
/// </summary>
public static class OtelBootstrap
{
    /// <summary>Add OpenTelemetry to DI.</summary>
    public static void Add(WebApplicationBuilder builder)
    {
        var obsOptions = builder.Configuration
            .GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        var serviceVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0";

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(rb => rb
                .AddService(serviceName: SerilogBootstrap.ServiceName, serviceVersion: serviceVersion)
                .AddAttributes(new KeyValuePair<string, object>[]
                {
                    new("service.namespace", "nexus-ha"),
                }))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation();
                t.AddHttpClientInstrumentation();
                if (!string.IsNullOrWhiteSpace(obsOptions.Otlp.Endpoint))
                {
                    t.AddOtlpExporter(o => o.Endpoint = new Uri(obsOptions.Otlp.Endpoint));
                }
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation();
                m.AddHttpClientInstrumentation();
                m.AddRuntimeInstrumentation();
                m.AddMeter(AuditMetrics.MeterName);

                if (obsOptions.Prometheus.Enabled)
                {
                    m.AddPrometheusExporter();
                }

                if (!string.IsNullOrWhiteSpace(obsOptions.Otlp.Endpoint))
                {
                    m.AddOtlpExporter(o => o.Endpoint = new Uri(obsOptions.Otlp.Endpoint));
                }
            });
    }
}
