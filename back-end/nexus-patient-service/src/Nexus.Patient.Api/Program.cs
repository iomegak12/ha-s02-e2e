using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Nexus.Patients.Api.Configuration;
using Nexus.Patients.Api.Features.Patients.Controller;
using Nexus.Patients.Api.Features.Patients.Repository;
using Nexus.Patients.Api.Features.Patients.Service;
using Nexus.Patients.Api.Infrastructure.Audit;
using Nexus.Patients.Api.Infrastructure.Branches;
using Nexus.Patients.Api.Infrastructure.Purge;
using Nexus.Patients.Api.Infrastructure.Auth;
using Nexus.Patients.Api.Infrastructure.Errors;
using Nexus.Patients.Api.Infrastructure.Idempotency;
using Nexus.Patients.Api.Infrastructure.Observability;
using Nexus.Patients.Api.Infrastructure.Persistence;
using Nexus.Patients.Api.Infrastructure.Startup;
using Serilog;

namespace Nexus.Patients.Api;

/// <summary>
/// Composition root for the Nexus HA Patient service.
/// Phase 2 adds cross-cutting infrastructure.
/// </summary>
public sealed class Program
{
    /// <summary>Application entry point.</summary>
    public static async Task<int> Main(string[] args)
    {
        BannerWriter.Write();

        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddOptions<PersistenceOptions>()
            .Bind(builder.Configuration.GetSection(PersistenceOptions.SectionName))
            .ValidateOnStart();

        builder.Services.AddOptions<AuthOptions>()
            .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();

        builder.Services.AddOptions<RateLimitingOptions>()
            .Bind(builder.Configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();

        builder.Services.AddOptions<CorsOptions>()
            .Bind(builder.Configuration.GetSection(CorsOptions.SectionName))
            .Validate(o => !(o.AllowCredentials && o.AllowedOrigins.Contains("*")),
                "Cors.AllowedOrigins cannot contain '*' when AllowCredentials is true.")
            .ValidateOnStart();

        builder.Services.AddOptions<ObservabilityOptions>()
            .Bind(builder.Configuration.GetSection(ObservabilityOptions.SectionName))
            .ValidateOnStart();

        SerilogBootstrap.Configure(builder);
        OtelBootstrap.Add(builder);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: true);

        PersistenceRegistration.Add(builder);
        builder.Services.AddScoped<IPatientRepository, PatientRepository>();
        builder.Services.AddScoped<IPatientCodeSequenceRepository, PatientCodeSequenceRepository>();
        builder.Services.AddScoped<IPublicCodeGenerator, PublicCodeGenerator>();
        builder.Services.AddScoped<IPatientsService, PatientsService>();

        builder.Services.AddScoped<IdempotencyKeyFilter>();

        // --- OpenAPI / Swagger -----------------------------------------------------
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "Nexus HA — Patients Service API",
                Version = "v1",
            });
        });

        AuthRegistration.Add(builder);
        AuditRegistration.Add(builder);
        BranchesClientRegistration.Add(builder);

        // --- Purge worker (Phase 8) -------------------------------------------------
        builder.Services.AddOptions<PurgeOptions>()
            .Bind(builder.Configuration.GetSection(PurgeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var auditCfg = builder.Configuration.GetSection(AuditClientOptions.SectionName).Get<AuditClientOptions>() ?? new AuditClientOptions();
        builder.Services.AddHttpClient<IAuditRedactorClient, AuditRedactorClient>(AuditRedactorClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(auditCfg.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        builder.Services.AddHostedService<PatientPurgeWorker>();
        HealthCheckRegistration.Add(builder);
        AuditRegistration.AddReadinessProbe(builder);
        BranchesClientRegistration.AddReadinessProbe(builder);

        var corsCfg = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
        builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        {
            if (corsCfg.AllowedOrigins.Contains("*")) p.AllowAnyOrigin(); else p.WithOrigins(corsCfg.AllowedOrigins);
            if (corsCfg.AllowedHeaders.Contains("*")) p.AllowAnyHeader(); else p.WithHeaders(corsCfg.AllowedHeaders);
            if (corsCfg.AllowedMethods.Contains("*")) p.AllowAnyMethod(); else p.WithMethods(corsCfg.AllowedMethods);
            if (corsCfg.AllowCredentials) p.AllowCredentials();
        }));

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

        app.MapGet("/", () => Results.Ok(new { service = "nexus-patient", phase = 5 }));

        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Nexus Patients v1"));
        app.UseReDoc(c => { c.SpecUrl = "/swagger/v1/swagger.json"; c.RoutePrefix = "redoc"; });

        app.MapPatients();

        StartupSummaryWriter.Write(app);

        try
        {
            await PersistenceRegistration.ApplyMigrationsAsync(app);
            app.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Patient service terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
