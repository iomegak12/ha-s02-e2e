using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Nexus.Doctors.Api.Configuration;
using Nexus.Doctors.Api.Features.Doctors.Controller;
using Nexus.Doctors.Api.Features.Doctors.Documents.Controller;
using Nexus.Doctors.Api.Features.Doctors.Documents.Repository;
using Nexus.Doctors.Api.Features.Doctors.Documents.Service;
using Nexus.Doctors.Api.Features.Doctors.Repository;
using Nexus.Doctors.Api.Features.Doctors.Service;
using Nexus.Doctors.Api.Infrastructure.Documents;
using Nexus.Doctors.Api.Infrastructure.Audit;
using Nexus.Doctors.Api.Infrastructure.Branches;
using Nexus.Doctors.Api.Infrastructure.Auth;
using Nexus.Doctors.Api.Infrastructure.Errors;
using Nexus.Doctors.Api.Infrastructure.Idempotency;
using Nexus.Doctors.Api.Infrastructure.Observability;
using Nexus.Doctors.Api.Infrastructure.Persistence;
using Nexus.Doctors.Api.Infrastructure.Startup;
using Serilog;

namespace Nexus.Doctors.Api;

/// <summary>
/// Composition root for the Nexus HA Doctor service.
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
        builder.Services.AddScoped<IDoctorRepository, DoctorRepository>();
        builder.Services.AddScoped<IDoctorCodeSequenceRepository, DoctorCodeSequenceRepository>();
        builder.Services.AddScoped<IPublicCodeGenerator, PublicCodeGenerator>();
        builder.Services.AddScoped<IDoctorsService, DoctorsService>();

        // --- Documents (Phase 7) ----------------------------------------------------
        builder.Services.AddOptions<DocumentsOptions>()
            .Bind(builder.Configuration.GetSection(DocumentsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton<IDocumentStorage, LocalFileSystemDocumentStorage>();
        builder.Services.AddScoped<IDoctorDocumentRepository, DoctorDocumentRepository>();
        builder.Services.AddScoped<IDoctorDocumentsService, DoctorDocumentsService>();

        builder.Services.AddScoped<IdempotencyKeyFilter>();

        // --- OpenAPI / Swagger -----------------------------------------------------
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "Nexus HA — Doctors Service API",
                Version = "v1",
            });
        });

        AuthRegistration.Add(builder);
        AuditRegistration.Add(builder);
        BranchesClientRegistration.Add(builder);
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

        app.MapGet("/", () => Results.Ok(new { service = "nexus-doctor", phase = 7 }));

        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Nexus Doctors v1"));
        app.UseReDoc(c => { c.SpecUrl = "/swagger/v1/swagger.json"; c.RoutePrefix = "redoc"; });

        app.MapDoctors();
        app.MapDoctorDocuments();

        StartupSummaryWriter.Write(app);

        try
        {
            await PersistenceRegistration.ApplyMigrationsAsync(app);
            app.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Doctor service terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
