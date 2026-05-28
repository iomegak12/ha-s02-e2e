using System.Diagnostics.Metrics;
using System.Reflection;
using Nexus.Identity.Api.Configuration;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Nexus.Identity.Api.Infrastructure.Observability;

/// <summary>
/// Wires OpenTelemetry traces and metrics. Logs are emitted via Serilog (see <see cref="SerilogBootstrap"/>).
/// Prometheus scraping is enabled via the AspNetCore exporter; OTLP is conditional on
/// <c>Observability:Otlp:Endpoint</c> being non-empty.
/// </summary>
public static class OtelBootstrap
{
    /// <summary>Meter name for custom Identity metrics.</summary>
    public const string MeterName = "Nexus.Identity";

    /// <summary>Custom meter exposed for feature code.</summary>
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>Counter of tokens issued, partitioned by <c>result</c>.</summary>
    public static readonly Counter<long> TokensIssued = Meter.CreateCounter<long>(
        "nexus_identity_tokens_issued_total",
        unit: "{tokens}",
        description: "Total access tokens issued, partitioned by success/failure.");

    /// <summary>Add OpenTelemetry to DI.</summary>
    public static void Add(WebApplicationBuilder builder)
    {
        var obsOptions = builder.Configuration
            .GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        var serviceVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0";

        var resource = ResourceBuilder.CreateDefault()
            .AddService(serviceName: SerilogBootstrap.ServiceName, serviceVersion: serviceVersion)
            .AddAttributes(new KeyValuePair<string, object>[]
            {
                new("service.namespace", "nexus-ha"),
            });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(rb => rb
                .AddService(serviceName: SerilogBootstrap.ServiceName, serviceVersion: serviceVersion))
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
                m.AddMeter(MeterName);

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
