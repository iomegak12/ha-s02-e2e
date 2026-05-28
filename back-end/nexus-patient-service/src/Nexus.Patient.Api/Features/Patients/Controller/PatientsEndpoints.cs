using Microsoft.AspNetCore.Mvc;
using Nexus.Patients.Api.Features.Patients.Models;
using Nexus.Patients.Api.Features.Patients.Service;
using Nexus.Patients.Api.Infrastructure.Auth;
using Nexus.Patients.Api.Infrastructure.Errors;
using Nexus.Patients.Api.Infrastructure.Idempotency;

namespace Nexus.Patients.Api.Features.Patients.Controller;

/// <summary>Minimal-API mapping for the 9 Patient operations in <c>specs/patients.openapi.json</c>.</summary>
public static class PatientsEndpoints
{
    /// <summary>Mount the Patients feature surface under <c>/api/v1/patients</c>.</summary>
    public static IEndpointRouteBuilder MapPatients(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/patients")
            .WithTags("Patients")
            .RequireAuthorization(AuthPolicies.AdminOnly);

        group.MapGet("/", ListAsync).WithName("listPatients");

        group.MapPost("/", CreateAsync)
            .WithName("createPatient")
            .AddEndpointFilter<IdempotencyKeyFilter>()
            .AddEndpointFilter<ValidationFilter<PatientCreateRequest>>();

        group.MapGet("/{id:guid}", GetByIdAsync).WithName("getPatientById");

        group.MapPatch("/{id:guid}", PatchAsync)
            .WithName("patchPatient")
            .AddEndpointFilter<IdempotencyKeyFilter>()
            .AddEndpointFilter<ValidationFilter<PatientPatchRequest>>();

        group.MapPost("/{id:guid}/activate", ActivateAsync)
            .WithName("activatePatient")
            .AddEndpointFilter<IdempotencyKeyFilter>();

        group.MapPost("/{id:guid}/archive", ArchiveAsync)
            .WithName("archivePatient")
            .AddEndpointFilter<IdempotencyKeyFilter>();

        group.MapGet("/{id:guid}/branches", ListBranchesAsync).WithName("listPatientBranches");

        group.MapPost("/{id:guid}/branches", LinkBranchAsync)
            .WithName("linkPatientToBranch")
            .AddEndpointFilter<IdempotencyKeyFilter>()
            .AddEndpointFilter<ValidationFilter<PatientBranchLinkRequest>>();

        group.MapDelete("/{id:guid}/branches/{branchId:guid}", UnlinkBranchAsync)
            .WithName("unlinkPatientFromBranch")
            .AddEndpointFilter<IdempotencyKeyFilter>();

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] IPatientsService service,
        [FromQuery] string? status, [FromQuery] Guid? branchId, [FromQuery] string? q,
        [FromQuery] int? page, [FromQuery] int? size, CancellationToken ct)
    {
        PatientStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<PatientStatus>(status, ignoreCase: true, out var s))
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
        HttpContext http, [FromServices] IPatientsService service,
        [FromBody] PatientCreateRequest request, CancellationToken ct)
    {
        var outcome = await service.CreateAsync(request, http.User, ct);
        http.Response.Headers.ETag = outcome.ETag;
        return Results.Created($"/api/v1/patients/{outcome.Patient.Id}", outcome.Patient);
    }

    private static async Task<IResult> GetByIdAsync(
        HttpContext http, [FromServices] IPatientsService service, Guid id, CancellationToken ct)
    {
        var outcome = await service.GetByIdAsync(id, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "Not Found", "Patient was not found.");
        http.Response.Headers.ETag = outcome.ETag;
        return Results.Ok(outcome.Patient);
    }

    private static async Task<IResult> PatchAsync(
        HttpContext http, [FromServices] IPatientsService service, Guid id,
        [FromBody] PatientPatchRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        var outcome = await service.PatchAsync(id, request, ifMatch, http.User, ct);
        http.Response.Headers.ETag = outcome.ETag;
        return Results.Ok(outcome.Patient);
    }

    private static async Task<IResult> ActivateAsync(
        HttpContext http, [FromServices] IPatientsService service, Guid id, CancellationToken ct)
    {
        var outcome = await service.ActivateAsync(id, http.User, ct);
        http.Response.Headers.ETag = outcome.ETag;
        return Results.Ok(outcome.Patient);
    }

    private static async Task<IResult> ArchiveAsync(
        HttpContext http, [FromServices] IPatientsService service, Guid id, CancellationToken ct)
    {
        var outcome = await service.ArchiveAsync(id, http.User, ct);
        http.Response.Headers.ETag = outcome.ETag;
        return Results.Ok(outcome.Patient);
    }

    private static async Task<IResult> ListBranchesAsync(
        [FromServices] IPatientsService service, Guid id, CancellationToken ct)
    {
        var items = await service.ListBranchesAsync(id, ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> LinkBranchAsync(
        HttpContext http, [FromServices] IPatientsService service, Guid id,
        [FromBody] PatientBranchLinkRequest request, CancellationToken ct)
    {
        var link = await service.LinkBranchAsync(id, request, http.User, ct);
        return Results.Created($"/api/v1/patients/{id}/branches/{request.BranchId}", link);
    }

    private static async Task<IResult> UnlinkBranchAsync(
        HttpContext http, [FromServices] IPatientsService service, Guid id, Guid branchId, CancellationToken ct)
    {
        await service.UnlinkBranchAsync(id, branchId, http.User, ct);
        return Results.NoContent();
    }
}
