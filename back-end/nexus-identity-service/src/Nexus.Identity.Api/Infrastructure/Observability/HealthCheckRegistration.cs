using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Nexus.Identity.Api.Infrastructure.Observability;

/// <summary>
/// Registers health checks and the three documented endpoints: <c>/health/live</c>,
/// <c>/health/ready</c>, and <c>/health</c>.
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
        // Audit probe is appended in Phase 5.
    }

    /// <summary>Map health endpoints onto the application pipeline.</summary>
    public static void MapEndpoints(WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteJsonAsync,
        }).DisableRateLimiting();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(ReadyTag),
            ResponseWriter = WriteJsonAsync,
        }).DisableRateLimiting();

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteJsonAsync,
        }).DisableRateLimiting();
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
