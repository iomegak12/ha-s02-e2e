using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Nexus.Identity.Api.Configuration;
using Nexus.Identity.Api.Features.Admins.Controller;
using Nexus.Identity.Api.Features.Admins.Repository;
using Nexus.Identity.Api.Features.Admins.Service;
using Nexus.Identity.Api.Features.Auth.Controller;
using Nexus.Identity.Api.Features.Auth.Repository;
using Nexus.Identity.Api.Features.Auth.Service;
using Nexus.Identity.Api.Infrastructure.Audit;
using Nexus.Identity.Api.Infrastructure.Errors;
using Nexus.Identity.Api.Infrastructure.Observability;
using Nexus.Identity.Api.Infrastructure.OpenApi;
using Nexus.Identity.Api.Infrastructure.Persistence;
using Nexus.Identity.Api.Infrastructure.Security;
using Nexus.Identity.Api.Infrastructure.Startup;
using Serilog;

namespace Nexus.Identity.Api;

/// <summary>
/// Composition root for the Nexus HA Identity &amp; Admin service.
/// Phase 1 wires cross-cutting infrastructure (config, errors, observability, OpenAPI,
/// CORS, rate limiting, health). Feature controllers land in later phases.
/// </summary>
public sealed class Program
{
    /// <summary>Application entry point.</summary>
    public static async Task<int> Main(string[] args)
    {
        BannerWriter.Write();

        var builder = WebApplication.CreateBuilder(args);

        // --- Configuration binding --------------------------------------------------
        builder.Services.AddOptions<JwtIssuerOptions>()
            .Bind(builder.Configuration.GetSection(JwtIssuerOptions.SectionName))
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

        builder.Services.AddOptions<ResilienceOptions>()
            .Bind(builder.Configuration.GetSection(ResilienceOptions.SectionName))
            .ValidateOnStart();

        builder.Services.AddOptions<AuditClientOptions>()
            .Bind(builder.Configuration.GetSection(AuditClientOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<ObservabilityOptions>()
            .Bind(builder.Configuration.GetSection(ObservabilityOptions.SectionName))
            .ValidateOnStart();

        builder.Services.AddOptions<SeederOptions>()
            .Bind(builder.Configuration.GetSection(SeederOptions.SectionName))
            .ValidateOnStart();

        builder.Services.AddOptions<PersistenceOptions>()
            .Bind(builder.Configuration.GetSection(PersistenceOptions.SectionName))
            .ValidateOnStart();

        // --- Logging & telemetry ----------------------------------------------------
        SerilogBootstrap.Configure(builder);
        OtelBootstrap.Add(builder);

        // --- Validation -------------------------------------------------------------
        builder.Services.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: true);

        // --- OpenAPI ----------------------------------------------------------------
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Nexus HA — Identity & Admin Service",
                Version = "v1",
                Description = "Self-hosted identity provider for Nexus HA hospital administrators.",
            });

            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "JWT issued by POST /api/v1/auth/token.",
            });

            o.OperationFilter<HeaderConventionsOperationFilter>();

            var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
            if (File.Exists(xmlPath))
            {
                o.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
            }
        });

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

            o.OnRejected = (ctx, ct) =>
            {
                var retryAfter = TimeSpan.FromSeconds(rlCfg.WindowSeconds);
                ctx.HttpContext.Response.Headers["Retry-After"] = ((int)retryAfter.TotalSeconds).ToString();
                throw new DomainException(
                    code: ErrorCodes.RateLimited,
                    status: StatusCodes.Status429TooManyRequests,
                    title: "Too Many Requests",
                    detail: $"Rate limit exceeded. Retry after {(int)retryAfter.TotalSeconds}s.");
            };

            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                if (!rlCfg.Enabled)
                {
                    return RateLimitPartition.GetNoLimiter("disabled");
                }

                var path = httpContext.Request.Path.Value ?? string.Empty;
                if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/metrics", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/.well-known/jwks.json", StringComparison.OrdinalIgnoreCase))
                {
                    return RateLimitPartition.GetNoLimiter("bypass");
                }

                var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rlCfg.PermitLimit,
                    Window = TimeSpan.FromSeconds(rlCfg.WindowSeconds),
                    QueueLimit = 0,
                });
            });
        });

        // --- Health checks ----------------------------------------------------------
        HealthCheckRegistration.Add(builder);
        AuditRegistration.AddReadinessProbe(builder);

        // --- Persistence ------------------------------------------------------------
        PersistenceRegistration.Add(builder);

        // --- Audit publishing -------------------------------------------------------
        AuditRegistration.Add(builder);

        // --- Auth feature -----------------------------------------------------------
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        builder.Services.AddScoped<ISigningKeyStore, SqlSigningKeyStore>();
        builder.Services.AddScoped<IJwksProvider, JwksProvider>();
        builder.Services.AddScoped<IJwtIssuer, JwtIssuer>();
        builder.Services.AddScoped<IAuthService, AuthService>();

        // --- Admins feature ---------------------------------------------------------
        builder.Services.AddScoped<IAdminRepository, AdminRepository>();
        builder.Services.AddScoped<IAdminService, AdminService>();

        // --- Dev-only admin seeder (Q3=A) -------------------------------------------
        builder.Services.AddHostedService<AdminSeederWorker>();

        // --- Authentication & Authorization -----------------------------------------
        var jwtCfg = builder.Configuration.GetSection(JwtIssuerOptions.SectionName).Get<JwtIssuerOptions>() ?? new JwtIssuerOptions();
        builder.Services.AddSingleton<LocalSigningKeyResolver>();
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtCfg.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtCfg.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "preferred_username",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<LocalSigningKeyResolver>((options, resolver) =>
            {
                options.TokenValidationParameters.IssuerSigningKeyResolver = resolver.Resolve;
            });
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("AdminOnly", p => p
                .RequireAuthenticatedUser()
                .RequireRole("Admin"));
        });

        // --- Build & pipeline -------------------------------------------------------
        var app = builder.Build();

        app.UseMiddleware<ProblemDetailsMiddleware>();
        app.UseSerilogRequestLogging();

        app.UseCors();
        app.UseRateLimiter();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseSwagger();
        app.UseSwaggerUI(o =>
        {
            o.SwaggerEndpoint("/swagger/v1/swagger.json", "Nexus HA Identity v1");
            o.RoutePrefix = "swagger";
        });
        app.UseReDoc(o =>
        {
            o.RoutePrefix = "redoc";
            o.SpecUrl = "/swagger/v1/swagger.json";
            o.DocumentTitle = "Nexus HA Identity API";
        });

        var obs = builder.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>() ?? new ObservabilityOptions();
        if (obs.Prometheus.Enabled)
        {
            app.MapPrometheusScrapingEndpoint("/metrics").DisableRateLimiting();
        }

        HealthCheckRegistration.MapEndpoints(app);

        app.MapAuthEndpoints();
        app.MapAdminsEndpoints();

        StartupSummaryWriter.Write(app);

        try
        {
            await PersistenceRegistration.ApplyMigrationsAsync(app);
            app.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Identity service terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
