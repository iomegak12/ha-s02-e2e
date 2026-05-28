using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nexus.Branches.Api.Features.Branches.Models;
using Nexus.Branches.Api.Features.Branches.Repository;
using Nexus.Branches.Api.Infrastructure.Audit;
using Nexus.Branches.Api.Infrastructure.Auth;
using Nexus.Branches.Api.Infrastructure.Errors;
using Nexus.Branches.Api.Infrastructure.Observability;
using Nexus.Branches.Api.Infrastructure.Pagination;

namespace Nexus.Branches.Api.Features.Branches.Service;

/// <summary>Outcome wrapper used by endpoints to set the response <c>ETag</c> header.</summary>
public sealed record BranchWithETag(BranchDto Branch, string ETag);

/// <summary>
/// Business surface for the 5 Branches operations. Encapsulates code-duplicate
/// detection, ETag concurrency, soft-delete semantics, and audit-event emission.
/// </summary>
public interface IBranchesService
{
    Task<PagedBranchResponse> ListAsync(bool? isActive, string? codeContains, int page, int size, CancellationToken ct);
    Task<BranchWithETag?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<BranchWithETag> CreateAsync(BranchCreateRequest request, ClaimsPrincipal principal, CancellationToken ct);
    Task<BranchWithETag> PatchAsync(Guid id, BranchPatchRequest request, string? ifMatch, ClaimsPrincipal principal, CancellationToken ct);
    Task DeactivateAsync(Guid id, ClaimsPrincipal principal, CancellationToken ct);
}

/// <inheritdoc />
public sealed class BranchesService : IBranchesService
{
    private readonly IBranchRepository _repo;
    private readonly IAuditPublisher _audit;
    private readonly TimeProvider _clock;
    private readonly ILogger<BranchesService> _logger;

    public BranchesService(
        IBranchRepository repo,
        IAuditPublisher audit,
        TimeProvider clock,
        ILogger<BranchesService> logger)
    {
        _repo = repo;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PagedBranchResponse> ListAsync(bool? isActive, string? codeContains, int page, int size, CancellationToken ct)
    {
        var result = await _repo.QueryAsync(new BranchQuery(isActive, codeContains, page, size), ct);
        return new PagedBranchResponse
        {
            Page = result.Page,
            Size = result.Size,
            TotalCount = result.Total,
            Items = result.Items.Select(BranchDto.From).ToList(),
        };
    }

    /// <inheritdoc />
    public async Task<BranchWithETag?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var b = await _repo.GetByIdNoTrackingAsync(id, ct);
        return b is null ? null : new BranchWithETag(BranchDto.From(b), FormatETag(b.RowVersion));
    }

    /// <inheritdoc />
    public async Task<BranchWithETag> CreateAsync(BranchCreateRequest request, ClaimsPrincipal principal, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var existing = await _repo.GetActiveByCodeAsync(code, ct);
        if (existing is not null)
        {
            throw new DomainException(
                ErrorCodes.BranchCodeDuplicate, StatusCodes.Status409Conflict,
                "Conflict", $"A branch with code '{code}' already exists.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var entity = new Branch
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Name = request.Name.Trim(),
            City = request.City.Trim(),
            IsActive = request.IsActive ?? true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        try
        {
            await _repo.AddAsync(entity, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Concurrent insert won the race — collapse to the spec's 409.
            throw new DomainException(
                ErrorCodes.BranchCodeDuplicate, StatusCodes.Status409Conflict,
                "Conflict", $"A branch with code '{code}' already exists.");
        }

        BranchMetrics.BranchesCreated.Add(1);

        await EmitAuditAsync(principal, entity, action: "BranchCreated",
            summary: $"Branch {entity.Code} created", diff: SerializeFull(entity), ct);

        return new BranchWithETag(BranchDto.From(entity), FormatETag(entity.RowVersion));
    }

    /// <inheritdoc />
    public async Task<BranchWithETag> PatchAsync(Guid id, BranchPatchRequest request, string? ifMatch, ClaimsPrincipal principal, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null)
        {
            throw new DomainException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "Not Found", "Branch was not found.");
        }

        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            var expected = FormatETag(entity.RowVersion);
            if (!ETagsMatch(expected, ifMatch))
            {
                throw new DomainException(
                    ErrorCodes.EtagMismatch, StatusCodes.Status409Conflict,
                    "Conflict", "The branch was modified by another request.");
            }
        }

        var before = SerializeFull(entity);

        if (request.Name is not null) entity.Name = request.Name.Trim();
        if (request.City is not null) entity.City = request.City.Trim();
        if (request.IsActive is not null) entity.IsActive = request.IsActive.Value;
        entity.UpdatedAtUtc = _clock.GetUtcNow().UtcDateTime;

        try
        {
            await _repo.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(
                ErrorCodes.EtagMismatch, StatusCodes.Status409Conflict,
                "Conflict", "The branch was modified by another request.");
        }

        BranchMetrics.BranchesUpdated.Add(1);

        var after = SerializeFull(entity);
        await EmitAuditAsync(principal, entity, action: "BranchUpdated",
            summary: $"Branch {entity.Code} updated",
            diff: JsonSerializer.Serialize(new { before, after }), ct);

        return new BranchWithETag(BranchDto.From(entity), FormatETag(entity.RowVersion));
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(Guid id, ClaimsPrincipal principal, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null)
        {
            throw new DomainException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "Not Found", "Branch was not found.");
        }

        if (!entity.IsActive) return; // idempotent

        entity.IsActive = false;
        entity.UpdatedAtUtc = _clock.GetUtcNow().UtcDateTime;
        await _repo.SaveChangesAsync(ct);

        BranchMetrics.BranchesDeactivated.Add(1);

        await EmitAuditAsync(principal, entity, action: "BranchDeactivated",
            summary: $"Branch {entity.Code} deactivated", diff: null, ct);
    }

    // -------------------- helpers --------------------

    private static string FormatETag(byte[] rowVersion)
    {
        var token = rowVersion is { Length: > 0 }
            ? Convert.ToBase64String(rowVersion)
            : Guid.NewGuid().ToString("N");
        return $"\"{token}\"";
    }

    private static bool ETagsMatch(string expected, string incoming)
    {
        var a = expected.Trim().Trim('W', '/').Trim('"');
        var b = incoming.Trim().Trim('W', '/').Trim('"');
        return string.Equals(a, b, StringComparison.Ordinal);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("IX_Branches_Code_Active", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
    }

    private static string SerializeFull(Branch b) => JsonSerializer.Serialize(new
    {
        b.Id,
        b.Code,
        b.Name,
        b.City,
        b.IsActive,
    });

    private async Task EmitAuditAsync(ClaimsPrincipal principal, Branch entity, string action, string summary, string? diff, CancellationToken ct)
    {
        try
        {
            var user = CurrentUser.From(principal);
            var dto = new AuditEventDto
            {
                EntityType = "Branch",
                EntityId = entity.Id,
                EntityCode = entity.Code,
                Action = action,
                ActorId = user.Id,
                ActorUsername = user.Username,
                OccurredAtUtc = _clock.GetUtcNow().UtcDateTime,
                Summary = summary,
                DiffJson = diff,
            };
            await _audit.PublishAsync(dto, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit publish failed for {Action} {EntityId} (fail-open)", action, entity.Id);
        }
    }
}
