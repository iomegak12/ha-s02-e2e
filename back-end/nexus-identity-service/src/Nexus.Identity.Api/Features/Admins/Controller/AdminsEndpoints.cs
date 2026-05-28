using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nexus.Identity.Api.Features.Admins.Models;
using Nexus.Identity.Api.Features.Admins.Service;
using Nexus.Identity.Api.Infrastructure.Errors;

namespace Nexus.Identity.Api.Features.Admins.Controller;

/// <summary>
/// Minimal-API endpoint registrations for the <c>Admins</c> feature.
/// Mapped from <c>Program.cs</c>.
/// </summary>
public static class AdminsEndpoints
{
    /// <summary>Map all Admins endpoints onto the application.</summary>
    public static IEndpointRouteBuilder MapAdminsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/admins")
            .WithTags("Admins")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/", async (
                [FromQuery] int? page,
                [FromQuery] int? size,
                [FromQuery] string? sort,
                [FromQuery] bool? isActive,
                IAdminService svc,
                CancellationToken ct) =>
            {
                var p = NormalisePage(page);
                var s = NormaliseSize(size);
                var result = await svc.ListAsync(p, s, isActive, sort, ct);
                return Results.Ok(result);
            })
            .WithName("listAdmins")
            .WithSummary("List admins with paging and optional active filter.")
            .Produces<PagedAdminResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/", async (
                AdminCreateRequest body,
                IAdminService svc,
                HttpContext http,
                CancellationToken ct) =>
            {
                var (admin, etag) = await svc.CreateAsync(body, ct);
                http.Response.Headers.ETag = etag;
                return Results.Created($"/api/v1/admins/{admin.Id}", admin);
            })
            .AddEndpointFilter<ValidationFilter<AdminCreateRequest>>()
            .WithName("createAdmin")
            .WithSummary("Create a new admin account.")
            .Produces<AdminDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/{id:guid}", async (
                Guid id,
                IAdminService svc,
                HttpContext http,
                CancellationToken ct) =>
            {
                var result = await svc.GetByIdAsync(id, ct);
                if (result is null)
                {
                    throw NotFound(id);
                }

                http.Response.Headers.ETag = result.Value.ETag;
                return Results.Ok(result.Value.Admin);
            })
            .WithName("getAdmin")
            .WithSummary("Fetch a single admin by id.")
            .Produces<AdminDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}", async (
                Guid id,
                AdminPatchRequest body,
                IAdminService svc,
                HttpContext http,
                CancellationToken ct) =>
            {
                http.Request.Headers.TryGetValue("If-Match", out var ifMatch);
                var (admin, etag) = await svc.PatchAsync(id, body, ifMatch.ToString(), ct);
                http.Response.Headers.ETag = etag;
                return Results.Ok(admin);
            })
            .AddEndpointFilter<ValidationFilter<AdminPatchRequest>>()
            .WithName("patchAdmin")
            .WithSummary("Update display name or active flag on an admin.")
            .Produces<AdminDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/{id:guid}", async (
                Guid id,
                IAdminService svc,
                CancellationToken ct) =>
            {
                await svc.DeactivateAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("deactivateAdmin")
            .WithSummary("Soft-delete an admin (sets isActive=false).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static int NormalisePage(int? page) => page is null or < 1 ? 1 : page.Value;

    private static int NormaliseSize(int? size)
    {
        var s = size ?? 20;
        if (s < 1) return 1;
        if (s > 100) return 100;
        return s;
    }

    private static DomainException NotFound(Guid id) => new(
        ErrorCodes.NotFound,
        StatusCodes.Status404NotFound,
        "Not Found",
        $"Admin '{id}' was not found.");
}
