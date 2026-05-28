using Microsoft.AspNetCore.Mvc;
using Nexus.Doctors.Api.Features.Doctors.Models;
using Nexus.Doctors.Api.Features.Doctors.Service;
using Nexus.Doctors.Api.Infrastructure.Auth;
using Nexus.Doctors.Api.Infrastructure.Errors;
using Nexus.Doctors.Api.Infrastructure.Idempotency;

namespace Nexus.Doctors.Api.Features.Doctors.Controller;

/// <summary>
/// Minimal-API mapping for the 11 non-document Doctor operations in
/// <c>specs/doctors.openapi.json</c>. Documents (5 more ops) come in Phase 7.
/// </summary>
public static class DoctorsEndpoints
{
    /// <summary>Mount the Doctors feature surface under <c>/api/v1/doctors</c>.</summary>
    public static IEndpointRouteBuilder MapDoctors(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/doctors")
            .WithTags("Doctors")
            .RequireAuthorization(AuthPolicies.AdminOnly);

        group.MapGet("/", ListAsync).WithName("listDoctors");

        group.MapPost("/", CreateAsync)
            .WithName("createDoctor")
            .AddEndpointFilter<IdempotencyKeyFilter>()
            .AddEndpointFilter<ValidationFilter<DoctorCreateRequest>>();

        group.MapGet("/{id:guid}", GetByIdAsync).WithName("getDoctorById");

        group.MapPatch("/{id:guid}", PatchAsync)
            .WithName("patchDoctor")
            .AddEndpointFilter<IdempotencyKeyFilter>()
            .AddEndpointFilter<ValidationFilter<DoctorPatchRequest>>();

        group.MapPost("/{id:guid}/verify", VerifyAsync)
            .WithName("verifyDoctor")
            .AddEndpointFilter<IdempotencyKeyFilter>();

        group.MapPost("/{id:guid}/approve", ApproveAsync)
            .WithName("approveDoctor")
            .AddEndpointFilter<IdempotencyKeyFilter>();

        group.MapPost("/{id:guid}/activate", ActivateAsync)
            .WithName("activateDoctor")
            .AddEndpointFilter<IdempotencyKeyFilter>();

        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName("deactivateDoctor")
            .AddEndpointFilter<IdempotencyKeyFilter>();

        group.MapGet("/{id:guid}/branches", ListBranchesAsync).WithName("listDoctorBranches");

        group.MapPost("/{id:guid}/branches", LinkBranchAsync)
            .WithName("linkDoctorToBranch")
            .AddEndpointFilter<IdempotencyKeyFilter>()
            .AddEndpointFilter<ValidationFilter<DoctorBranchLinkRequest>>();

        group.MapDelete("/{id:guid}/branches/{branchId:guid}", UnlinkBranchAsync)
            .WithName("unlinkDoctorFromBranch")
            .AddEndpointFilter<IdempotencyKeyFilter>();

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] IDoctorsService service,
        [FromQuery] string? status, [FromQuery] Guid? branchId, [FromQuery] string? q,
        [FromQuery] int? page, [FromQuery] int? size, CancellationToken ct)
    {
        DoctorStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DoctorStatus>(status, ignoreCase: true, out var s))
            {
                throw new DomainException(ErrorCodes.BadRequest, StatusCodes.Status400BadRequest,
                    "Bad Request", $"Invalid status '{status}'.");
            }
            parsed = s;
        }
        var result = await service.ListAsync(parsed, branchId, q, page ?? 1, size ?? 20, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateAsync(
        HttpContext http, [FromServices] IDoctorsService service,
        [FromBody] DoctorCreateRequest request, CancellationToken ct)
    {
        var outcome = await service.CreateAsync(request, http.User, ct);
        http.Response.Headers.ETag = outcome.ETag;
        return Results.Created($"/api/v1/doctors/{outcome.Doctor.Id}", outcome.Doctor);
    }

    private static async Task<IResult> GetByIdAsync(
        HttpContext http, [FromServices] IDoctorsService service, Guid id, CancellationToken ct)
    {
        var outcome = await service.GetByIdAsync(id, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "Not Found", "Doctor was not found.");
        http.Response.Headers.ETag = outcome.ETag;
        return Results.Ok(outcome.Doctor);
    }

    private static async Task<IResult> PatchAsync(
        HttpContext http, [FromServices] IDoctorsService service, Guid id,
        [FromBody] DoctorPatchRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        var outcome = await service.PatchAsync(id, request, ifMatch, http.User, ct);
        http.Response.Headers.ETag = outcome.ETag;
        return Results.Ok(outcome.Doctor);
    }

    private static async Task<IResult> VerifyAsync(HttpContext http, [FromServices] IDoctorsService service, Guid id, CancellationToken ct)
    {
        var outcome = await service.VerifyAsync(id, http.User, ct);
        http.Response.Headers.ETag = outcome.ETag;
        return Results.Ok(outcome.Doctor);
    }

    private static async Task<IResult> ApproveAsync(HttpContext http, [FromServices] IDoctorsService service, Guid id, CancellationToken ct)
    {
        var outcome = await service.ApproveAsync(id, http.User, ct);
        http.Response.Headers.ETag = outcome.ETag;
        return Results.Ok(outcome.Doctor);
    }

    private static async Task<IResult> ActivateAsync(HttpContext http, [FromServices] IDoctorsService service, Guid id, CancellationToken ct)
    {
        var outcome = await service.ActivateAsync(id, http.User, ct);
        http.Response.Headers.ETag = outcome.ETag;
        return Results.Ok(outcome.Doctor);
    }

    private static async Task<IResult> DeactivateAsync(HttpContext http, [FromServices] IDoctorsService service, Guid id, CancellationToken ct)
    {
        var outcome = await service.DeactivateAsync(id, http.User, ct);
        http.Response.Headers.ETag = outcome.ETag;
        return Results.Ok(outcome.Doctor);
    }

    private static async Task<IResult> ListBranchesAsync(
        [FromServices] IDoctorsService service, Guid id, CancellationToken ct)
        => Results.Ok(await service.ListBranchesAsync(id, ct));

    private static async Task<IResult> LinkBranchAsync(
        HttpContext http, [FromServices] IDoctorsService service, Guid id,
        [FromBody] DoctorBranchLinkRequest request, CancellationToken ct)
    {
        var link = await service.LinkBranchAsync(id, request, http.User, ct);
        return Results.Created($"/api/v1/doctors/{id}/branches/{request.BranchId}", link);
    }

    private static async Task<IResult> UnlinkBranchAsync(
        HttpContext http, [FromServices] IDoctorsService service, Guid id, Guid branchId, CancellationToken ct)
    {
        await service.UnlinkBranchAsync(id, branchId, http.User, ct);
        return Results.NoContent();
    }
}
