using Microsoft.AspNetCore.Mvc;
using Nexus.Audit.Api.Features.Audit.Models;
using Nexus.Audit.Api.Features.Audit.Service;
using Nexus.Audit.Api.Infrastructure.Auth;
using Nexus.Audit.Api.Infrastructure.Errors;
using Nexus.Audit.Api.Infrastructure.Idempotency;

namespace Nexus.Audit.Api.Features.Audit.Controller;

/// <summary>
/// Maps the three OpenAPI operations defined in <c>specs/audit.openapi.json</c>:
/// <c>appendAuditEntry</c>, <c>queryAuditEntries</c>, <c>getAuditEntryById</c>.
/// </summary>
public static class AuditEndpoints
{
    /// <summary>Register the audit endpoint group on <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/audit")
            .WithTags("Audit")
            .RequireAuthorization(AuthPolicies.AdminOnly);

        group.MapPost("/", AppendAsync)
            .AddEndpointFilter<IdempotencyKeyFilter>()
            .AddEndpointFilter<ValidationFilter<AppendAuditEntryRequest>>()
            .WithName("appendAuditEntry")
            .WithSummary("Append a new audit entry (idempotent).")
            .Accepts<AppendAuditEntryRequest>("application/json")
            .Produces<AuditEntry>(StatusCodes.Status201Created)
            .Produces<AuditEntry>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/", QueryAsync)
            .WithName("queryAuditEntries")
            .WithSummary("Query audit entries with filters and pagination.")
            .Produces<PagedAuditEntries>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/redact", RedactAsync)
            .AddEndpointFilter<IdempotencyKeyFilter>()
            .AddEndpointFilter<ValidationFilter<RedactAuditEntriesRequest>>()
            .WithName("redactAuditEntries")
            .WithSummary("Redact PII for every audit entry referring to a single entity (admin-only).")
            .Accepts<RedactAuditEntriesRequest>("application/json")
            .Produces<RedactAuditEntriesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("getAuditEntryById")
            .WithSummary("Fetch a single audit entry by id.")
            .Produces<AuditEntry>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> AppendAsync(
        AppendAuditEntryRequest body,
        IAuditService svc,
        HttpContext http,
        CancellationToken ct)
    {
        if (http.Items[IdempotencyContext.HttpItemsKey] is not IdempotencyContext ctx)
        {
            throw new DomainException(
                code: ErrorCodes.IdempotencyKeyMissing,
                status: StatusCodes.Status400BadRequest,
                title: "Missing Idempotency-Key",
                detail: "Idempotency context was not resolved by the endpoint filter.");
        }

        var result = await svc.AppendAsync(body, ctx, ct);

        if (result.IsReplay)
        {
            http.Response.Headers["X-Idempotent-Replay"] = "true";
            return Results.Ok(result.Entry);
        }

        return Results.Created($"/api/v1/audit/{result.Entry.Id}", result.Entry);
    }

    private static async Task<IResult> QueryAsync(
        [FromQuery] string? entityType,
        [FromQuery] Guid? entityId,
        [FromQuery] string? action,
        [FromQuery] Guid? actorId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? sourceService,
        [FromQuery] int? page,
        [FromQuery] int? size,
        [FromQuery] string? sort,
        IAuditService svc,
        CancellationToken ct)
    {
        var query = new AuditQuery(
            EntityType: entityType,
            EntityId: entityId,
            Action: action,
            ActorId: actorId,
            From: from,
            To: to,
            SourceService: sourceService,
            Page: page ?? 1,
            Size: size ?? 20,
            Sort: sort);

        var result = await svc.QueryAsync(query, ct);
        return Results.Ok(new PagedAuditEntries(result.Items, result.Page, result.Size, result.Total));
    }

    private static async Task<IResult> RedactAsync(
        RedactAuditEntriesRequest body,
        IAuditService svc,
        CancellationToken ct)
    {
        var placeholder = string.IsNullOrWhiteSpace(body.RedactionPlaceholder)
            ? "[REDACTED]"
            : body.RedactionPlaceholder!.Trim();
        var count = await svc.RedactByEntityAsync(body.EntityType, body.EntityId, placeholder, ct);
        return Results.Ok(new RedactAuditEntriesResponse
        {
            EntityType = body.EntityType,
            EntityId = body.EntityId,
            RowsRedacted = count,
        });
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        IAuditService svc,
        CancellationToken ct)
    {
        var entry = await svc.GetByIdAsync(id, ct);
        if (entry is null)
        {
            throw new DomainException(
                code: ErrorCodes.NotFound,
                status: StatusCodes.Status404NotFound,
                title: "Audit entry not found",
                detail: $"No audit entry exists with id {id}.");
        }

        return Results.Ok(entry);
    }
}
