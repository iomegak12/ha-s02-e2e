using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Nexus.Audit.Api.Configuration;

namespace Nexus.Audit.Api.Infrastructure.Auth;

/// <summary>
/// DI wiring for JWT bearer authentication (Identity-issued tokens validated via
/// JWKS) and the <see cref="AuthPolicies.AdminOnly"/> authorization policy.
/// </summary>
public static class AuthRegistration
{
    /// <summary>Register authentication, authorization, and the JWKS resolver.</summary>
    public static void Add(WebApplicationBuilder builder)
    {
        var authCfg = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

        builder.Services.AddSingleton(new JwksKeyResolver(authCfg.ResolvedJwksUrl));

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authCfg.Issuer,
                    ValidateAudience = true,
                    ValidAudience = authCfg.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "preferred_username",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwksKeyResolver>((options, resolver) =>
            {
                options.TokenValidationParameters.IssuerSigningKeyResolver = resolver.Resolve;
            });

        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy(AuthPolicies.AdminOnly, p => p
                .RequireAuthenticatedUser()
                .RequireRole("Admin"));
        });
    }
}
