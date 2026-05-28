using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Nexus.Branches.Api.Configuration;
using Nexus.Branches.Api.Features.Branches.Controller;
using Nexus.Branches.Api.Features.Branches.Repository;
using Nexus.Branches.Api.Features.Branches.Service;
using Nexus.Branches.Api.Infrastructure.Audit;
using Nexus.Branches.Api.Infrastructure.Auth;
using Nexus.Branches.Api.Infrastructure.Errors;
using Nexus.Branches.Api.Infrastructure.Idempotency;
using Nexus.Branches.Api.Infrastructure.Observability;
using Nexus.Branches.Api.Infrastructure.Persistence;
using Nexus.Branches.Api.Infrastructure.Startup;
using Serilog;

namespace Nexus.Branches.Api;

/// <summary>
/// Composition root for the Nexus HA Branch service.
/// Phase 2 adds cross-cutting infrastructure (errors, validation, auth,
/// idempotency, observability, health, CORS, rate limiting).
/// </summary>
public sealed class Program
{
    /// <summary>Application entry point.</summary>
    public static async Task<int> Main(string[] args)
    {
        BannerWriter.Write();

        var builder = WebApplication.CreateBuilder(args);

        // --- Configuration binding --------------------------------------------------
        builder.Services.AddOptions<PersistenceOptions>()
            .Bind(builder.Configuration.GetSection(PersistenceOptions.SectionName))
            .ValidateOnStart();

        builder.Services.AddOptions<AuthOptions>()
            .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<RateLimitingOptions>()
            .Bind(builder.Configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<CorsOptions>()
            .Bind(builder.Configuration.GetSection(CorsOptions.SectionName))
            .Validate(o => !(o.AllowCredentials && o.AllowedOrigins.Contains("*")),
                "Cors.AllowedOrigins cannot contain '*' when AllowCredentials is true.")
            .ValidateOnStart();

        builder.Services.AddOptions<ObservabilityOptions>()
            .Bind(builder.Configuration.GetSection(ObservabilityOptions.SectionName))
            .ValidateOnStart();

        // --- Logging & telemetry ----------------------------------------------------
        SerilogBootstrap.Configure(builder);
        OtelBootstrap.Add(builder);

        // --- Validation -------------------------------------------------------------
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: true);

        // --- Persistence ------------------------------------------------------------
        PersistenceRegistration.Add(builder);
        builder.Services.AddScoped<IBranchRepository, BranchRepository>();
        builder.Services.AddScoped<IBranchesService, BranchesService>();

        // --- OpenAPI / Swagger ------------------------------------------------------
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "Nexus HA — Branches Service API",
                Version = "v1",
            });
        });

        // --- Idempotency filter (used by write endpoints in Phase 4) ----------------
        builder.Services.AddScoped<IdempotencyKeyFilter>();

        // --- Auth -------------------------------------------------------------------
        AuthRegistration.Add(builder);

        // --- Audit publisher + outbox + reconciler ----------------------------------
        AuditRegistration.Add(builder);

        // --- Health -----------------------------------------------------------------
        HealthCheckRegistration.Add(builder);
        AuditRegistration.AddReadinessProbe(builder);

        // --- CORS -------------------------------------------------------------------
        var corsCfg = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
        builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        {
            if (corsCfg.AllowedOrigins.Contains("*")) p.AllowAnyOrigin(); else p.WithOrigins(corsCfg.AllowedOrigins);
            if (corsCfg.AllowedHeaders.Contains("*")) p.AllowAnyHeader(); else p.WithHeaders(corsCfg.AllowedHeaders);
            if (corsCfg.AllowedMethods.Contains("*")) p.AllowAnyMethod(); else p.WithMethods(corsCfg.AllowedMethods);
            if (corsCfg.AllowCredentials) p.AllowCredentials();
        }));

        // --- Rate limiting ----------------------------------------------------------
        var rlCfg = builder.Configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (ctx, _) =>
            {
                ctx.HttpContext.Response.Headers["Retry-After"] = rlCfg.WindowSeconds.ToString();
                throw new DomainException(
                    ErrorCodes.RateLimited, StatusCodes.Status429TooManyRequests,
                    "Too Many Requests", $"Rate limit exceeded. Retry after {rlCfg.WindowSeconds}s.");
            };

            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                if (!rlCfg.Enabled) return RateLimitPartition.GetNoLimiter("disabled");

                var path = http.Request.Path.Value ?? string.Empty;
                if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/metrics", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/redoc", StringComparison.OrdinalIgnoreCase))
                {
                    return RateLimitPartition.GetNoLimiter("bypass");
                }

                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter($"ip:{ip}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rlCfg.PermitLimit,
                    Window = TimeSpan.FromSeconds(rlCfg.WindowSeconds),
                    QueueLimit = 0,
                });
            });
        });

        var app = builder.Build();

        // --- Pipeline ---------------------------------------------------------------
        app.UseMiddleware<ProblemDetailsMiddleware>();
        app.UseSerilogRequestLogging();

        app.UseCors();
        app.UseRateLimiter();

        app.UseAuthentication();
        app.UseAuthorization();

        HealthCheckRegistration.MapEndpoints(app);

        var obs = builder.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>() ?? new ObservabilityOptions();
        if (obs.Prometheus.Enabled)
        {
            app.MapPrometheusScrapingEndpoint("/metrics").DisableRateLimiting();
        }

        app.MapGet("/", () => Results.Ok(new { service = "nexus-branch", phase = 4 }));

        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Nexus Branches v1"));
        app.UseReDoc(c => { c.SpecUrl = "/swagger/v1/swagger.json"; c.RoutePrefix = "redoc"; });

        app.MapBranches();

        StartupSummaryWriter.Write(app);

        try
        {
            await PersistenceRegistration.ApplyMigrationsAsync(app);
            app.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Branch service terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
