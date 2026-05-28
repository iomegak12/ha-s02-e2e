using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Nexus.Doctors.Api.Features.Doctors.Documents.Models;
using Nexus.Doctors.Api.Features.Doctors.Documents.Service;
using Nexus.Doctors.Api.Infrastructure.Auth;
using Nexus.Doctors.Api.Infrastructure.Errors;
using Nexus.Doctors.Api.Infrastructure.Idempotency;

namespace Nexus.Doctors.Api.Features.Doctors.Documents.Controller;

/// <summary>
/// 5 document operations: list / upload (multipart streaming) / get / review / delete.
/// Upload uses <see cref="MultipartReader"/> directly — never binds to <c>IFormFile</c>.
/// </summary>
public static class DoctorDocumentsEndpoints
{
    /// <summary>Mount under <c>/api/v1/doctors/{id}/documents</c>.</summary>
    public static IEndpointRouteBuilder MapDoctorDocuments(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/doctors/{id:guid}/documents")
            .WithTags("DoctorDocuments")
            .RequireAuthorization(AuthPolicies.AdminOnly);

        group.MapGet("/", ListAsync).WithName("listDoctorDocuments");

        group.MapPost("/", UploadAsync)
            .WithName("uploadDoctorDocument")
            .DisableAntiforgery()
            .AddEndpointFilter<IdempotencyKeyFilter>();

        group.MapGet("/{docId:guid}", GetAsync).WithName("getDoctorDocument");

        group.MapPatch("/{docId:guid}", ReviewAsync)
            .WithName("reviewDoctorDocument")
            .AddEndpointFilter<IdempotencyKeyFilter>();

        group.MapDelete("/{docId:guid}", DeleteAsync)
            .WithName("deleteDoctorDocument")
            .AddEndpointFilter<IdempotencyKeyFilter>();

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] IDoctorDocumentsService service, Guid id, CancellationToken ct)
        => Results.Ok(await service.ListAsync(id, ct));

    private static async Task<IResult> UploadAsync(
        HttpContext http, [FromServices] IDoctorDocumentsService service, Guid id, CancellationToken ct)
    {
        if (!MultipartRequestHelper.IsMultipart(http.Request.ContentType))
        {
            throw new DomainException(ErrorCodes.BadRequest, StatusCodes.Status400BadRequest,
                "Bad Request", "Expected a multipart/form-data request body.");
        }

        var boundary = MultipartRequestHelper.GetBoundary(MediaTypeHeaderValue.Parse(http.Request.ContentType), 70);
        var reader = new MultipartReader(boundary, http.Request.Body);

        string? kind = null;
        string? fileName = null;
        string? contentType = null;
        Stream? fileStream = null;

        var section = await reader.ReadNextSectionAsync(ct);
        while (section is not null)
        {
            var hasDisp = ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disp);
            if (hasDisp && disp is not null)
            {
                if (MultipartRequestHelper.HasFormDataContentDisposition(disp))
                {
                    var name = disp.Name.Value?.Trim('"');
                    if (string.Equals(name, "kind", StringComparison.OrdinalIgnoreCase))
                    {
                        using var sr = new StreamReader(section.Body);
                        kind = (await sr.ReadToEndAsync(ct)).Trim();
                    }
                }
                else if (MultipartRequestHelper.HasFileContentDisposition(disp))
                {
                    fileName = disp.FileNameStar.Value?.Trim('"')
                               ?? disp.FileName.Value?.Trim('"')
                               ?? "upload.bin";
                    contentType = section.ContentType ?? "application/octet-stream";
                    fileStream = section.Body;
                    break; // file must come last so the rest of the body can stream
                }
            }
            section = await reader.ReadNextSectionAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(kind) || fileStream is null)
        {
            throw new DomainException(ErrorCodes.BadRequest, StatusCodes.Status400BadRequest,
                "Bad Request", "Multipart body must include 'kind' and 'file' parts.");
        }

        var dto = await service.UploadAsync(id, kind!, fileName!, contentType!, fileStream, http.User, ct);
        return Results.Created($"/api/v1/doctors/{id}/documents/{dto.Id}", dto);
    }

    private static async Task<IResult> GetAsync(
        [FromServices] IDoctorDocumentsService service, Guid id, Guid docId, CancellationToken ct)
    {
        var dto = await service.GetAsync(id, docId, ct);
        if (dto is null)
            throw new DomainException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "Not Found", "Document was not found.");
        return Results.Ok(dto);
    }

    private static async Task<IResult> ReviewAsync(
        HttpContext http, [FromServices] IDoctorDocumentsService service,
        Guid id, Guid docId, [FromBody] DocumentReviewRequest request, CancellationToken ct)
    {
        var dto = await service.ReviewAsync(id, docId, request, http.User, ct);
        return Results.Ok(dto);
    }

    private static async Task<IResult> DeleteAsync(
        HttpContext http, [FromServices] IDoctorDocumentsService service,
        Guid id, Guid docId, CancellationToken ct)
    {
        await service.DeleteAsync(id, docId, http.User, ct);
        return Results.NoContent();
    }
}

internal static class MultipartRequestHelper
{
    public static bool IsMultipart(string? contentType) =>
        !string.IsNullOrEmpty(contentType) && contentType.Contains("multipart/", StringComparison.OrdinalIgnoreCase);

    public static string GetBoundary(MediaTypeHeaderValue contentType, int lengthLimit)
    {
        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary))
            throw new InvalidDataException("Missing content-type boundary.");
        if (boundary.Length > lengthLimit)
            throw new InvalidDataException($"Multipart boundary length exceeds {lengthLimit}.");
        return boundary;
    }

    public static bool HasFormDataContentDisposition(ContentDispositionHeaderValue d) =>
        d.DispositionType.Equals("form-data") && string.IsNullOrEmpty(d.FileName.Value) && string.IsNullOrEmpty(d.FileNameStar.Value);

    public static bool HasFileContentDisposition(ContentDispositionHeaderValue d) =>
        d.DispositionType.Equals("form-data") && (!string.IsNullOrEmpty(d.FileName.Value) || !string.IsNullOrEmpty(d.FileNameStar.Value));
}
