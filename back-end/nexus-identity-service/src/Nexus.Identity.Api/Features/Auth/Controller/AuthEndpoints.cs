using Nexus.Identity.Api.Features.Auth.Models;
using Nexus.Identity.Api.Features.Auth.Service;
using Nexus.Identity.Api.Infrastructure.Errors;

namespace Nexus.Identity.Api.Features.Auth.Controller;

/// <summary>
/// Minimal-API endpoint registrations for the <c>Auth</c> feature.
/// Mapped from <c>Program.cs</c>.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>Map all Auth endpoints onto the application.</summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/token", async (TokenRequest body, IAuthService svc, CancellationToken ct) =>
                Results.Ok(await svc.IssueAsync(body, ct)))
            .AddEndpointFilter<ValidationFilter<TokenRequest>>()
            .AllowAnonymous()
            .WithName("issueToken")
            .WithSummary("Exchange admin credentials for an access + refresh token pair.")
            .Produces<TokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/refresh", async (RefreshRequest body, IAuthService svc, CancellationToken ct) =>
                Results.Ok(await svc.RefreshAsync(body, ct)))
            .AddEndpointFilter<ValidationFilter<RefreshRequest>>()
            .AllowAnonymous()
            .WithName("refreshToken")
            .WithSummary("Exchange a refresh token for a new access token.")
            .Produces<TokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/revoke", async (RefreshRequest body, IAuthService svc, CancellationToken ct) =>
            {
                await svc.RevokeAsync(body, ct);
                return Results.NoContent();
            })
            .AddEndpointFilter<ValidationFilter<RefreshRequest>>()
            .WithName("revokeToken")
            .WithSummary("Revoke a refresh token (idempotent).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // Per OpenAPI spec, JWKS is published at the well-known path. Cache for 5 min.
        app.MapGet("/.well-known/jwks.json", async (IJwksProvider provider, HttpContext http, CancellationToken ct) =>
            {
                var jwks = await provider.GetAsync(ct);
                http.Response.Headers.CacheControl = "public, max-age=300";
                return Results.Json(jwks);
            })
            .WithTags("Auth")
            .AllowAnonymous()
            .WithName("getJwks")
            .WithSummary("Public signing keys (JWKS).")
            .Produces<JwksResponse>(StatusCodes.Status200OK);

        return app;
    }
}
