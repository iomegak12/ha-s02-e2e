using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Nexus.Identity.Api.Features.Admins.Models;
using Nexus.Identity.Api.Features.Admins.Repository;
using Nexus.Identity.Api.Infrastructure.Audit;
using Nexus.Identity.Api.Infrastructure.Errors;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;
using Nexus.Identity.Api.Infrastructure.Security;

namespace Nexus.Identity.Api.Features.Admins.Service;

/// <inheritdoc cref="IAdminService" />
public sealed class AdminService : IAdminService
{
    private readonly IAdminRepository _repository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditPublisher _auditPublisher;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Create the service.</summary>
    public AdminService(
        IAdminRepository repository,
        IPasswordHasher passwordHasher,
        TimeProvider timeProvider,
        IAuditPublisher auditPublisher,
        IHttpContextAccessor httpContextAccessor)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
        _timeProvider = timeProvider;
        _auditPublisher = auditPublisher;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public async Task<PagedAdminResponse> ListAsync(int page, int size, bool? isActive, string? sort, CancellationToken cancellationToken)
    {
        var (field, descending) = ParseSort(sort);

        var (entities, total) = await _repository
            .ListAsync(page, size, isActive, field, descending, cancellationToken)
            .ConfigureAwait(false);

        return new PagedAdminResponse
        {
            Page = page,
            Size = size,
            TotalCount = (int)total,
            Items = entities.Select(ToDto).ToList(),
        };
    }

    /// <inheritdoc />
    public async Task<(AdminDto Admin, string ETag)?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        return (ToDto(entity), FormatETag(entity.RowVersion));
    }

    /// <inheritdoc />
    public async Task<(AdminDto Admin, string ETag)> CreateAsync(AdminCreateRequest request, CancellationToken cancellationToken)
    {
        if (await _repository.ExistsByUsernameAsync(request.Username, cancellationToken).ConfigureAwait(false))
        {
            throw DuplicateUsername(request.Username);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var entity = new Admin
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            DisplayName = request.DisplayName,
            PasswordHash = _passwordHasher.Hash(request.Password),
            IsActive = request.IsActive ?? true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        await _repository.AddAsync(entity, cancellationToken).ConfigureAwait(false);

        try
        {
            await _repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw DuplicateUsername(request.Username);
        }

        await PublishAuditAsync(entity, "Created", $"Admin '{entity.Username}' created.", cancellationToken).ConfigureAwait(false);

        return (ToDto(entity), FormatETag(entity.RowVersion));
    }

    /// <inheritdoc />
    public async Task<(AdminDto Admin, string ETag)> PatchAsync(Guid id, AdminPatchRequest request, string? ifMatch, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            throw NotFound(id);
        }

        if (!string.IsNullOrWhiteSpace(ifMatch) && !ETagMatches(ifMatch, entity.RowVersion))
        {
            throw new DomainException(
                ErrorCodes.EtagMismatch,
                StatusCodes.Status409Conflict,
                "ETag Mismatch",
                "The supplied If-Match value does not match the current resource version.");
        }

        var diff = new Dictionary<string, object?>();

        if (request.DisplayName is not null && !string.Equals(entity.DisplayName, request.DisplayName, StringComparison.Ordinal))
        {
            diff["displayName"] = new { from = entity.DisplayName, to = request.DisplayName };
            entity.DisplayName = request.DisplayName;
        }

        var statusChanged = false;
        if (request.IsActive is { } active && entity.IsActive != active)
        {
            diff["isActive"] = new { from = entity.IsActive, to = active };
            entity.IsActive = active;
            statusChanged = true;
        }

        entity.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            await _repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(
                ErrorCodes.EtagMismatch,
                StatusCodes.Status409Conflict,
                "ETag Mismatch",
                "The resource was modified by another request. Reload and retry.");
        }

        if (diff.Count > 0)
        {
            var action = statusChanged ? "StatusChanged" : "Updated";
            var summary = $"Admin '{entity.Username}' {(statusChanged ? (entity.IsActive ? "reactivated" : "deactivated") : "updated")}.";
            await PublishAuditAsync(entity, action, summary, cancellationToken, diff).ConfigureAwait(false);
        }

        return (ToDto(entity), FormatETag(entity.RowVersion));
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            throw NotFound(id);
        }

        if (!entity.IsActive)
        {
            return;
        }

        entity.IsActive = false;
        entity.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        await _repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await PublishAuditAsync(
            entity,
            "StatusChanged",
            $"Admin '{entity.Username}' deactivated.",
            cancellationToken,
            new { isActive = new { from = true, to = false } })
            .ConfigureAwait(false);
    }

    private static AdminDto ToDto(Admin entity) => new()
    {
        Id = entity.Id,
        Username = entity.Username,
        DisplayName = entity.DisplayName,
        IsActive = entity.IsActive,
        CreatedAtUtc = DateTime.SpecifyKind(entity.CreatedAtUtc, DateTimeKind.Utc),
    };

    /// <summary>Format an ETag header value from a SQL <c>rowversion</c>.</summary>
    public static string FormatETag(byte[] rowVersion)
        => "\"" + Convert.ToBase64String(rowVersion ?? Array.Empty<byte>()) + "\"";

    private static bool ETagMatches(string ifMatch, byte[] rowVersion)
    {
        if (rowVersion is null || rowVersion.Length == 0)
        {
            return false;
        }

        var supplied = ifMatch.Trim().Trim('"');
        var current = Convert.ToBase64String(rowVersion);
        return string.Equals(supplied, current, StringComparison.Ordinal);
    }

    private static (AdminSortField Field, bool Descending) ParseSort(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return (AdminSortField.CreatedAtUtc, true);
        }

        var parts = sort.Split(':', 2, StringSplitOptions.TrimEntries);
        var fieldRaw = parts[0];
        var directionRaw = parts.Length > 1 ? parts[1] : "asc";

        var field = fieldRaw switch
        {
            "createdAtUtc" => AdminSortField.CreatedAtUtc,
            "username" => AdminSortField.Username,
            "displayName" => AdminSortField.DisplayName,
            _ => throw new DomainException(
                ErrorCodes.AdminValidation,
                StatusCodes.Status422UnprocessableEntity,
                "Validation Failed",
                "The 'sort' parameter references an unknown field.",
                new[] { new ProblemError("sort", $"Unknown sort field '{fieldRaw}'.") }),
        };

        var descending = directionRaw.Equals("desc", StringComparison.OrdinalIgnoreCase);
        if (!descending && !directionRaw.Equals("asc", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                ErrorCodes.AdminValidation,
                StatusCodes.Status422UnprocessableEntity,
                "Validation Failed",
                "The 'sort' parameter direction must be 'asc' or 'desc'.",
                new[] { new ProblemError("sort", $"Unknown sort direction '{directionRaw}'.") });
        }

        return (field, descending);
    }

    private static DomainException NotFound(Guid id) => new(
        ErrorCodes.NotFound,
        StatusCodes.Status404NotFound,
        "Not Found",
        $"Admin '{id}' was not found.");

    private static DomainException DuplicateUsername(string username) => new(
        ErrorCodes.AdminUsernameDuplicate,
        StatusCodes.Status409Conflict,
        "Username Already Exists",
        $"An admin with username '{username}' already exists.");

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // SQL Server: 2627 (unique constraint), 2601 (duplicate key).
        var inner = ex.InnerException;
        var message = inner?.Message ?? string.Empty;
        return message.Contains("2627", StringComparison.Ordinal)
            || message.Contains("2601", StringComparison.Ordinal)
            || message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
    }

    private Task PublishAuditAsync(
        Admin entity,
        string action,
        string summary,
        CancellationToken cancellationToken,
        object? diff = null)
    {
        var (actorId, actorUsername) = ResolveActor();
        var entry = new AuditEntryDto
        {
            EntityType = "Admin",
            EntityId = entity.Id.ToString(),
            EntityCode = entity.Username,
            Action = action,
            ActorId = actorId,
            ActorUsername = actorUsername,
            OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            Summary = summary,
            Diff = diff,
        };

        // Fire-and-forget: publisher swallows failures internally. We must NOT
        // block the user response on the audit pipeline. Use CancellationToken.None
        // so the publish is not cancelled when the user request completes; the
        // resilience handler enforces its own time bounds.
        // The publisher reads HttpContext synchronously before its first await,
        // so the caller bearer is captured while the context is still alive.
        _ = _auditPublisher.PublishAsync(entry, CancellationToken.None);
        _ = cancellationToken; // reserved for future direct-await usage
        return Task.CompletedTask;
    }

    private (string ActorId, string ActorUsername) ResolveActor()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return (Guid.Empty.ToString(), "system");
        }

        var actorId = user.FindFirstValue("sub")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Guid.Empty.ToString();
        var actorUsername = user.FindFirstValue("preferred_username")
            ?? user.Identity?.Name
            ?? "unknown";
        return (actorId, actorUsername);
    }
}
