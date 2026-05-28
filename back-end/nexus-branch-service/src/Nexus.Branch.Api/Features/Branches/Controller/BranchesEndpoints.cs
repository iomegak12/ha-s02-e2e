using Microsoft.AspNetCore.Mvc;
using Nexus.Branches.Api.Features.Branches.Models;
using Nexus.Branches.Api.Features.Branches.Service;
using Nexus.Branches.Api.Infrastructure.Auth;
using Nexus.Branches.Api.Infrastructure.Errors;
using Nexus.Branches.Api.Infrastructure.Idempotency;

namespace Nexus.Branches.Api.Features.Branches.Controller;

/// <summary>Minimal-API mapping for the 5 Branches operations defined in <c>specs/branches.openapi.json</c>.</summary>
public static class BranchesEndpoints
{
    /// <summary>Mount the Branches feature surface under <c>/api/v1/branches</c>.</summary>
    public static IEndpointRouteBuilder MapBranches(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/branches")
            .WithTags("Branches")
            .RequireAuthorization(AuthPolicies.AdminOnly);

        group.MapGet("/", ListAsync)
            .WithName("listBranches")
            .WithSummary("List branches");

        group.MapPost("/", CreateAsync)
            .WithName("createBranch")
            .WithSummary("Create a new branch")
            .AddEndpointFilter<IdempotencyKeyFilter>()
            .AddEndpointFilter<ValidationFilter<BranchCreateRequest>>();

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("getBranchById")
            .WithSummary("Get branch by id");

        group.MapPatch("/{id:guid}", PatchAsync)
            .WithName("patchBranch")
            .WithSummary("Partially update a branch")
            .AddEndpointFilter<IdempotencyKeyFilter>()
            .AddEndpointFilter<ValidationFilter<BranchPatchRequest>>();

        group.MapDelete("/{id:guid}", DeactivateAsync)
            .WithName("deactivateBranch")
            .WithSummary("Deactivate (soft-delete) a branch")
            .AddEndpointFilter<IdempotencyKeyFilter>();

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] IBranchesService service,
        [FromQuery] bool? isActive,
        [FromQuery] string? city,
        [FromQuery] int? page,
        [FromQuery] int? size,
        CancellationToken ct)
    {
        // `city` is an optional filter from the spec — wired through CodeContains-equivalent at the repo
        // level is out of scope (city is name-keyed, not code-keyed); for v1 it is treated as a passthrough
        // and applied client-side via post-filter on the page below.
        var result = await service.ListAsync(isActive, codeContains: null, page ?? 1, size ?? 20, ct);

        if (!string.IsNullOrWhiteSpace(city))
        {
            var filtered = result.Items.Where(i => string.Equals(i.City, city, StringComparison.OrdinalIgnoreCase)).ToList();
            return Results.Ok(new PagedBranchResponse
            {
                Page = result.Page,
                Size = result.Size,
                TotalCount = filtered.Count,
                Items = filtered,
            });
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateAsync(
        HttpContext http,
        [FromServices] IBranchesService service,
        [FromBody] BranchCreateRequest request,
        CancellationToken ct)
    {
        var outcome = await service.CreateAsync(request, http.User, ct);
        http.Response.Headers.ETag = outcome.ETag;
        return Results.Created($"/api/v1/branches/{outcome.Branch.Id}", outcome.Branch);
    }

    private static async Task<IResult> GetByIdAsync(
        HttpContext http,
        [FromServices] IBranchesService service,
        Guid id,
        CancellationToken ct)
    {
        var outcome = await service.GetByIdAsync(id, ct);
        if (outcome is null)
        {
            throw new DomainException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "Not Found", "Branch was not found.");
        }
        http.Response.Headers.ETag = outcome.ETag;
        return Results.Ok(outcome.Branch);
    }

    private static async Task<IResult> PatchAsync(
        HttpContext http,
        [FromServices] IBranchesService service,
        Guid id,
        [FromBody] BranchPatchRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken ct)
    {
        var outcome = await service.PatchAsync(id, request, ifMatch, http.User, ct);
        http.Response.Headers.ETag = outcome.ETag;
        return Results.Ok(outcome.Branch);
    }

    private static async Task<IResult> DeactivateAsync(
        HttpContext http,
        [FromServices] IBranchesService service,
        Guid id,
        CancellationToken ct)
    {
        await service.DeactivateAsync(id, http.User, ct);
        return Results.NoContent();
    }
}
