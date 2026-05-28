using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nexus.Audit.Api.Configuration;

namespace Nexus.Audit.Api.Infrastructure.Observability;

/// <summary>
/// Registers health checks and the three documented endpoints: <c>/health/live</c>,
/// <c>/health/ready</c>, and <c>/health</c>. Readiness includes SQL Server and the
/// Identity JWKS URI; liveness reports process-up only.
/// </summary>
public static class HealthCheckRegistration
{
    /// <summary>Tag applied to readiness checks so liveness ignores them.</summary>
    public const string ReadyTag = "ready";

    /// <summary>Register health-check services.</summary>
    public static void Add(WebApplicationBuilder builder)
    {
        var healthChecks = builder.Services.AddHealthChecks();

        var sqlConnection = builder.Configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(sqlConnection))
        {
            healthChecks.AddSqlServer(
                connectionString: sqlConnection,
                name: "sqlserver",
                tags: new[] { ReadyTag });
        }

        var authCfg = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        var jwksUrl = authCfg.ResolvedJwksUrl;
        if (Uri.TryCreate(jwksUrl, UriKind.Absolute, out var jwksUri))
        {
            healthChecks.AddUrlGroup(
                jwksUri,
                name: "identity-jwks",
                tags: new[] { ReadyTag });
        }
    }

    /// <summary>Map health endpoints onto the application pipeline.</summary>
    public static void MapEndpoints(WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteJsonAsync,
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(ReadyTag),
            ResponseWriter = WriteJsonAsync,
        });

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteJsonAsync,
        });
    }

    private static Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            entries = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
            }),
        };

        return JsonSerializer.SerializeAsync(context.Response.Body, payload);
    }
}
