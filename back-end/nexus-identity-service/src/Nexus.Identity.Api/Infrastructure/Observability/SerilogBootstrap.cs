using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace Nexus.Identity.Api.Infrastructure.Observability;

/// <summary>
/// Bootstraps Serilog as the host logger, reading sink configuration from
/// <c>appsettings.json</c> and applying a sane default fallback.
/// </summary>
public static class SerilogBootstrap
{
    /// <summary>Service name attached to every log event.</summary>
    public const string ServiceName = "nexus-identity";

    /// <summary>Install Serilog on the web application builder.</summary>
    public static void Configure(WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, logger) =>
        {
            logger
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .Enrich.WithProperty("service", ServiceName)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning);

            if (context.HostingEnvironment.IsDevelopment())
            {
                logger.WriteTo.Console();
            }
            else
            {
                logger.WriteTo.Console(formatter: new Serilog.Formatting.Compact.CompactJsonFormatter());
            }

            var filePath = context.Configuration["Logging:File:Path"] ?? "logs/identity-.log";
            var retain = context.Configuration.GetValue<int?>("Logging:File:RetainedFileCountLimit") ?? 7;
            logger.WriteTo.File(
                path: filePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: retain);

            var otlp = context.Configuration["Observability:Otlp:Endpoint"];
            if (!string.IsNullOrWhiteSpace(otlp))
            {
                logger.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = otlp;
                    options.Protocol = OtlpProtocol.Grpc;
                    options.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = ServiceName,
                        ["service.namespace"] = "nexus-ha",
                    };
                });
            }
        });
    }
}
