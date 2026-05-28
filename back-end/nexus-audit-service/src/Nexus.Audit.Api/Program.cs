using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using Nexus.Audit.Api.Configuration;
using Nexus.Audit.Api.Features.Audit.Controller;
using Nexus.Audit.Api.Features.Audit.Repository;
using Nexus.Audit.Api.Features.Audit.Service;
using Nexus.Audit.Api.Infrastructure.Auth;
using Nexus.Audit.Api.Infrastructure.Errors;
using Nexus.Audit.Api.Infrastructure.Idempotency;
using Nexus.Audit.Api.Infrastructure.Observability;
using Nexus.Audit.Api.Infrastructure.OpenApi;
using Nexus.Audit.Api.Infrastructure.Persistence;
using Nexus.Audit.Api.Infrastructure.Startup;
using OpenTelemetry.Metrics;
using Serilog;

namespace Nexus.Audit.Api;

/// <summary>
/// Composition root for the Nexus HA Audit service.
/// Phase 5 adds Serilog, OpenTelemetry traces / metrics, Prometheus exposition,
/// CORS, rate limiting (off by default, source-service partition when on), and
/// the <see cref="IdempotencyTrimWorker"/> background service.
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

        builder.Services.AddOptions<IdempotencyOptions>()
            .Bind(builder.Configuration.GetSection(IdempotencyOptions.SectionName))
            .ValidateDataAnnotations()
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
        builder.Services.AddScoped<IAuditEntryRepository, AuditEntryRepository>();
        builder.Services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();

        // --- Audit feature ----------------------------------------------------------
        builder.Services.AddScoped<IAuditService, AuditService>();
        builder.Services.AddScoped<IdempotencyKeyFilter>();
        builder.Services.AddScoped<ValidationFilter<Features.Audit.Models.AppendAuditEntryRequest>>();
        builder.Services.AddScoped<ValidationFilter<Features.Audit.Models.RedactAuditEntriesRequest>>();
        builder.Services.AddHostedService<IdempotencyTrimWorker>();

        // --- Auth -------------------------------------------------------------------
        AuthRegistration.Add(builder);

        // --- Health -----------------------------------------------------------------
        HealthCheckRegistration.Add(builder);

        // --- CORS -------------------------------------------------------------------
        var corsCfg = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
        builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        {
            if (corsCfg.AllowedOrigins.Contains("*"))
            {
                p.AllowAnyOrigin();
            }
            else
            {
                p.WithOrigins(corsCfg.AllowedOrigins);
            }

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
                    code: ErrorCodes.RateLimited,
                    status: StatusCodes.Status429TooManyRequests,
                    title: "Too Many Requests",
                    detail: $"Rate limit exceeded. Retry after {rlCfg.WindowSeconds}s.");
            };

            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                if (!rlCfg.Enabled)
                {
                    return RateLimitPartition.GetNoLimiter("disabled");
                }

                var path = http.Request.Path.Value ?? string.Empty;
                if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/metrics", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/redoc", StringComparison.OrdinalIgnoreCase))
                {
                    return RateLimitPartition.GetNoLimiter("bypass");
                }

                // Append endpoint: partition by source service.
                if (http.Request.Method == HttpMethods.Post && path.Equals("/api/v1/audit", StringComparison.OrdinalIgnoreCase))
                {
                    var src = http.Request.Headers.TryGetValue(SourceServiceResolver.HeaderName, out var v)
                        ? v.ToString().ToLowerInvariant()
                        : SourceServiceResolver.Unknown;
                    return RateLimitPartition.GetFixedWindowLimiter($"src:{src}", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rlCfg.AppendPermitLimit,
                        Window = TimeSpan.FromSeconds(rlCfg.WindowSeconds),
                        QueueLimit = 0,
                    });
                }

                // Read endpoints: partition by IP.
                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter($"ip:{ip}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rlCfg.PermitLimit,
                    Window = TimeSpan.FromSeconds(rlCfg.WindowSeconds),
                    QueueLimit = 0,
                });
            });
        });

        // --- OpenAPI ----------------------------------------------------------------
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Nexus HA — Audit Service",
                Version = "v1",
                Description = "Append-only audit sink for the Nexus HA platform.",
            });
            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Admin JWT issued by nexus-identity-service.",
            });
            o.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
                }] = Array.Empty<string>(),
            });
            o.OperationFilter<AuditHeaderOperationFilter>();

            var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
            if (File.Exists(xmlPath))
            {
                o.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
            }
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

        app.UseSwagger();
        app.UseSwaggerUI(o =>
        {
            o.SwaggerEndpoint("/swagger/v1/swagger.json", "Nexus HA Audit v1");
            o.RoutePrefix = "swagger";
        });
        app.UseReDoc(o =>
        {
            o.RoutePrefix = "redoc";
            o.SpecUrl = "/swagger/v1/swagger.json";
            o.DocumentTitle = "Nexus HA Audit API";
        });

        var obs = builder.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>() ?? new ObservabilityOptions();
        if (obs.Prometheus.Enabled)
        {
            app.MapPrometheusScrapingEndpoint("/metrics").DisableRateLimiting();
        }

        app.MapGet("/", () => Results.Ok(new { service = "nexus-audit", phase = 5 }));

        app.MapAuditEndpoints();

        StartupSummaryWriter.Write(app);

        try
        {
            await PersistenceRegistration.ApplyMigrationsAsync(app);
            app.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Audit service terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
