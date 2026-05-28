using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nexus.Doctors.Api.Features.Doctors.Models;
using Nexus.Doctors.Api.Features.Doctors.Repository;
using Nexus.Doctors.Api.Infrastructure.Audit;
using Nexus.Doctors.Api.Infrastructure.Auth;
using Nexus.Doctors.Api.Infrastructure.Branches;
using Nexus.Doctors.Api.Infrastructure.Errors;
using Nexus.Doctors.Api.Infrastructure.Observability;

namespace Nexus.Doctors.Api.Features.Doctors.Service;

/// <summary>Outcome wrapper carrying the doctor projection and its ETag header value.</summary>
public sealed record DoctorWithETag(DoctorDto Doctor, string ETag);

/// <summary>
/// Business surface for the 11 non-document Doctor operations.
/// Documents (5 more ops) come in Phase 7.
/// </summary>
public interface IDoctorsService
{
    Task<PagedDoctorResponse> ListAsync(DoctorStatus? status, Guid? branchId, string? q, int page, int size, CancellationToken ct);
    Task<DoctorWithETag?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<DoctorWithETag> CreateAsync(DoctorCreateRequest request, ClaimsPrincipal principal, CancellationToken ct);
    Task<DoctorWithETag> PatchAsync(Guid id, DoctorPatchRequest request, string? ifMatch, ClaimsPrincipal principal, CancellationToken ct);
    Task<DoctorWithETag> VerifyAsync(Guid id, ClaimsPrincipal principal, CancellationToken ct);
    Task<DoctorWithETag> ApproveAsync(Guid id, ClaimsPrincipal principal, CancellationToken ct);
    Task<DoctorWithETag> ActivateAsync(Guid id, ClaimsPrincipal principal, CancellationToken ct);
    Task<DoctorWithETag> DeactivateAsync(Guid id, ClaimsPrincipal principal, CancellationToken ct);
    Task<IReadOnlyList<DoctorBranchLinkDto>> ListBranchesAsync(Guid id, CancellationToken ct);
    Task<DoctorBranchLinkDto> LinkBranchAsync(Guid id, DoctorBranchLinkRequest req, ClaimsPrincipal principal, CancellationToken ct);
    Task UnlinkBranchAsync(Guid id, Guid branchId, ClaimsPrincipal principal, CancellationToken ct);
}

/// <inheritdoc />
public sealed class DoctorsService : IDoctorsService
{
    private readonly IDoctorRepository _repo;
    private readonly IPublicCodeGenerator _codeGen;
    private readonly IBranchesClient _branches;
    private readonly IAuditPublisher _audit;
    private readonly TimeProvider _clock;
    private readonly ILogger<DoctorsService> _logger;

    public DoctorsService(
        IDoctorRepository repo,
        IPublicCodeGenerator codeGen,
        IBranchesClient branches,
        IAuditPublisher audit,
        TimeProvider clock,
        ILogger<DoctorsService> logger)
    {
        _repo = repo;
        _codeGen = codeGen;
        _branches = branches;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<PagedDoctorResponse> ListAsync(DoctorStatus? status, Guid? branchId, string? q, int page, int size, CancellationToken ct)
    {
        var result = await _repo.QueryAsync(new DoctorQuery(status, branchId, q, page, size), ct);
        return new PagedDoctorResponse
        {
            Page = result.Page,
            Size = result.Size,
            TotalCount = result.Total,
            Items = result.Items.Select(DoctorDto.From).ToList(),
        };
    }

    public async Task<DoctorWithETag?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var d = await _repo.GetByIdNoTrackingAsync(id, ct);
        return d is null ? null : new DoctorWithETag(DoctorDto.From(d), FormatETag(d.RowVersion));
    }

    public async Task<DoctorWithETag> CreateAsync(DoctorCreateRequest request, ClaimsPrincipal principal, CancellationToken ct)
    {
        var branch = await _branches.GetBranchAsync(request.PrimaryBranchId, ct);
        if (branch is null || !branch.IsActive)
        {
            throw new DomainException(
                ErrorCodes.BranchUnknown, StatusCodes.Status422UnprocessableEntity,
                "Validation failed", "primaryBranchId does not refer to an active branch.");
        }

        var license = request.LicenseNumber.Trim();
        var existing = await _repo.FindActiveByLicenseAsync(license, ct);
        if (existing is not null)
        {
            throw new DomainException(
                ErrorCodes.DuplicateLicense, StatusCodes.Status409Conflict,
                "Conflict", $"A doctor with license '{license}' already exists.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var year = (short)now.Year;
        var publicCode = await _codeGen.NextAsync(branch.Code, year, ct);

        var entity = new Doctor
        {
            Id = Guid.CreateVersion7(),
            PublicCode = publicCode,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            FullName = $"{request.FirstName.Trim()} {request.LastName.Trim()}",
            Phone = request.PrimaryPhone.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Specialisation = request.Specialty.Trim(),
            LicenseNumber = license,
            PrimaryBranchId = request.PrimaryBranchId,
            Status = DoctorStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        try
        {
            await _repo.AddAsync(entity, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new DomainException(
                ErrorCodes.DuplicateLicense, StatusCodes.Status409Conflict,
                "Conflict", $"A doctor with license '{license}' already exists.");
        }

        DoctorMetrics.DoctorsCreated.Add(1);
        await EmitAuditAsync(principal, entity, "Doctor.Created", $"Doctor {entity.PublicCode} created", SerializeFull(entity), ct);

        return new DoctorWithETag(DoctorDto.From(entity), FormatETag(entity.RowVersion));
    }

    public async Task<DoctorWithETag> PatchAsync(Guid id, DoctorPatchRequest request, string? ifMatch, ClaimsPrincipal principal, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(id, ct) ?? throw NotFound();

        if (!string.IsNullOrWhiteSpace(ifMatch) && !ETagsMatch(FormatETag(entity.RowVersion), ifMatch))
            throw EtagMismatch();

        if (request.PrimaryBranchId is { } newBranchId && newBranchId != entity.PrimaryBranchId)
        {
            var branch = await _branches.GetBranchAsync(newBranchId, ct);
            if (branch is null || !branch.IsActive)
            {
                throw new DomainException(
                    ErrorCodes.BranchUnknown, StatusCodes.Status422UnprocessableEntity,
                    "Validation failed", "primaryBranchId does not refer to an active branch.");
            }
            entity.PrimaryBranchId = newBranchId;
        }

        var before = SerializeFull(entity);

        if (request.FirstName is not null) entity.FirstName = request.FirstName.Trim();
        if (request.LastName is not null) entity.LastName = request.LastName.Trim();
        if (request.FirstName is not null || request.LastName is not null)
            entity.FullName = $"{entity.FirstName} {entity.LastName}";
        if (request.Specialty is not null) entity.Specialisation = request.Specialty.Trim();
        if (request.PrimaryPhone is not null) entity.Phone = request.PrimaryPhone.Trim();
        if (request.Email is not null) entity.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        entity.UpdatedAtUtc = _clock.GetUtcNow().UtcDateTime;

        try
        {
            await _repo.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { throw EtagMismatch(); }

        await EmitAuditAsync(principal, entity, "Doctor.Updated", $"Doctor {entity.PublicCode} updated",
            JsonSerializer.Serialize(new { before, after = SerializeFull(entity) }), ct);

        return new DoctorWithETag(DoctorDto.From(entity), FormatETag(entity.RowVersion));
    }

    public async Task<DoctorWithETag> VerifyAsync(Guid id, ClaimsPrincipal principal, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(id, ct) ?? throw NotFound();
        if (entity.Status == DoctorStatus.Verified)
            return new DoctorWithETag(DoctorDto.From(entity), FormatETag(entity.RowVersion));
        if (entity.Status != DoctorStatus.Pending)
        {
            throw new DomainException(
                ErrorCodes.LifecycleInvalidTransition, StatusCodes.Status409Conflict,
                "Conflict", $"Cannot transition from {entity.Status} to Verified.");
        }

        // Document-gated verification: every IsRequired document must be Verified.
        var liveDocs = entity.Documents.Where(d => d.DeletedAtUtc == null).ToList();
        var required = liveDocs.Where(d => d.IsRequired).ToList();
        if (required.Count == 0 || required.Any(d => d.Status != DocumentStatus.Verified))
        {
            throw new DomainException(
                ErrorCodes.LifecycleBlocked, StatusCodes.Status409Conflict,
                "Conflict", "All required documents must be Verified before the doctor can be verified.");
        }

        return await TransitionToAsync(entity, DoctorStatus.Verified, principal, ct);
    }

    public Task<DoctorWithETag> ApproveAsync(Guid id, ClaimsPrincipal principal, CancellationToken ct)
        => TransitionAsync(id, DoctorStatus.Verified, DoctorStatus.Approved, principal, ct);

    public async Task<DoctorWithETag> ActivateAsync(Guid id, ClaimsPrincipal principal, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(id, ct) ?? throw NotFound();

        // Re-activation edge: Deactivated → Active allowed.
        if (entity.Status == DoctorStatus.Active)
            return new DoctorWithETag(DoctorDto.From(entity), FormatETag(entity.RowVersion));
        if (entity.Status != DoctorStatus.Approved && entity.Status != DoctorStatus.Deactivated)
        {
            throw new DomainException(
                ErrorCodes.LifecycleInvalidTransition, StatusCodes.Status409Conflict,
                "Conflict", $"Cannot transition from {entity.Status} to Active.");
        }

        // Auto-create primary branch link from PrimaryBranchId if no live primary link exists.
        var activeLinks = entity.BranchLinks.Where(l => l.UnlinkedAtUtc == null).ToList();
        if (!activeLinks.Any(l => l.BranchId == entity.PrimaryBranchId && l.IsPrimary))
        {
            entity.BranchLinks.Add(new DoctorBranchLink
            {
                DoctorId = entity.Id,
                BranchId = entity.PrimaryBranchId,
                IsPrimary = true,
                LinkedAtUtc = _clock.GetUtcNow().UtcDateTime,
            });
        }

        return await TransitionToAsync(entity, DoctorStatus.Active, principal, ct);
    }

    public Task<DoctorWithETag> DeactivateAsync(Guid id, ClaimsPrincipal principal, CancellationToken ct)
        => TransitionAsync(id, DoctorStatus.Active, DoctorStatus.Deactivated, principal, ct);

    public async Task<IReadOnlyList<DoctorBranchLinkDto>> ListBranchesAsync(Guid id, CancellationToken ct)
    {
        var doctor = await _repo.GetByIdNoTrackingAsync(id, ct) ?? throw NotFound();
        return doctor.BranchLinks
            .Where(l => l.UnlinkedAtUtc == null)
            .Select(l => new DoctorBranchLinkDto
            {
                BranchId = l.BranchId,
                IsPrimary = l.IsPrimary,
                LinkedAtUtc = l.LinkedAtUtc,
            })
            .ToList();
    }

    public async Task<DoctorBranchLinkDto> LinkBranchAsync(Guid id, DoctorBranchLinkRequest req, ClaimsPrincipal principal, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(id, ct) ?? throw NotFound();

        var branch = await _branches.GetBranchAsync(req.BranchId, ct);
        if (branch is null || !branch.IsActive)
        {
            throw new DomainException(
                ErrorCodes.BranchUnknown, StatusCodes.Status422UnprocessableEntity,
                "Validation failed", "branchId does not refer to an active branch.");
        }

        var existing = entity.BranchLinks.FirstOrDefault(l => l.BranchId == req.BranchId);
        if (existing is not null && existing.UnlinkedAtUtc == null)
        {
            throw new DomainException(
                ErrorCodes.AlreadyLinked, StatusCodes.Status409Conflict,
                "Conflict", "Doctor is already linked to this branch.");
        }

        if (req.IsPrimary)
        {
            foreach (var l in entity.BranchLinks.Where(l => l.UnlinkedAtUtc == null))
                l.IsPrimary = false;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        if (existing is not null)
        {
            existing.UnlinkedAtUtc = null;
            existing.IsPrimary = req.IsPrimary;
            existing.LinkedAtUtc = now;
        }
        else
        {
            entity.BranchLinks.Add(new DoctorBranchLink
            {
                DoctorId = entity.Id,
                BranchId = req.BranchId,
                IsPrimary = req.IsPrimary,
                LinkedAtUtc = now,
            });
        }

        if (req.IsPrimary) entity.PrimaryBranchId = req.BranchId;
        entity.UpdatedAtUtc = now;
        await _repo.SaveChangesAsync(ct);

        DoctorMetrics.BranchesLinked.Add(1);
        await EmitAuditAsync(principal, entity, "DoctorBranch.Linked",
            $"Doctor {entity.PublicCode} linked to branch {branch.Code}",
            JsonSerializer.Serialize(new { req.BranchId, branch.Code, req.IsPrimary }), ct);

        return new DoctorBranchLinkDto
        {
            BranchId = req.BranchId,
            BranchCode = branch.Code,
            IsPrimary = req.IsPrimary,
            LinkedAtUtc = now,
        };
    }

    public async Task UnlinkBranchAsync(Guid id, Guid branchId, ClaimsPrincipal principal, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(id, ct) ?? throw NotFound();

        var link = entity.BranchLinks.FirstOrDefault(l => l.BranchId == branchId && l.UnlinkedAtUtc == null);
        if (link is null) return; // idempotent

        if (link.IsPrimary && entity.Status == DoctorStatus.Active)
        {
            throw new DomainException(
                ErrorCodes.PrimaryBranchRequired, StatusCodes.Status409Conflict,
                "Conflict", "Reassign the primary branch before unlinking this one.");
        }

        link.UnlinkedAtUtc = _clock.GetUtcNow().UtcDateTime;
        entity.UpdatedAtUtc = link.UnlinkedAtUtc.Value;
        await _repo.SaveChangesAsync(ct);

        await EmitAuditAsync(principal, entity, "DoctorBranch.Unlinked",
            $"Doctor {entity.PublicCode} unlinked from branch {branchId}",
            JsonSerializer.Serialize(new { branchId }), ct);
    }

    // -------------------- helpers --------------------

    private async Task<DoctorWithETag> TransitionAsync(Guid id, DoctorStatus expected, DoctorStatus target, ClaimsPrincipal principal, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(id, ct) ?? throw NotFound();
        if (entity.Status == target)
            return new DoctorWithETag(DoctorDto.From(entity), FormatETag(entity.RowVersion));
        if (entity.Status != expected)
        {
            throw new DomainException(
                ErrorCodes.LifecycleInvalidTransition, StatusCodes.Status409Conflict,
                "Conflict", $"Cannot transition from {entity.Status} to {target}.");
        }
        return await TransitionToAsync(entity, target, principal, ct);
    }

    private async Task<DoctorWithETag> TransitionToAsync(Doctor entity, DoctorStatus target, ClaimsPrincipal principal, CancellationToken ct)
    {
        var from = entity.Status;
        entity.Status = target;
        entity.UpdatedAtUtc = _clock.GetUtcNow().UtcDateTime;
        await _repo.SaveChangesAsync(ct);

        DoctorMetrics.StateTransitions.Add(1,
            new KeyValuePair<string, object?>("from", from.ToString()),
            new KeyValuePair<string, object?>("to", target.ToString()));

        await EmitAuditAsync(principal, entity, "Doctor.StateChanged",
            $"Doctor {entity.PublicCode} {from} → {target}", null, ct);

        return new DoctorWithETag(DoctorDto.From(entity), FormatETag(entity.RowVersion));
    }

    private static DomainException NotFound() =>
        new(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "Not Found", "Doctor was not found.");

    private static DomainException EtagMismatch() =>
        new(ErrorCodes.EtagMismatch, StatusCodes.Status409Conflict, "Conflict",
            "If-Match header does not match current resource version.");

    private static string FormatETag(byte[] rowVersion)
    {
        var token = rowVersion is { Length: > 0 } ? Convert.ToBase64String(rowVersion) : Guid.NewGuid().ToString("N");
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
            || msg.Contains("UX_Doctors_License_Active", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
    }

    private static string SerializeFull(Doctor d) => JsonSerializer.Serialize(new
    {
        d.Id, d.PublicCode, d.FirstName, d.LastName, d.Phone, d.Email,
        Specialty = d.Specialisation, d.LicenseNumber, d.PrimaryBranchId,
        Status = d.Status.ToString(),
    });

    private async Task EmitAuditAsync(ClaimsPrincipal principal, Doctor entity, string action, string summary, string? diff, CancellationToken ct)
    {
        try
        {
            var user = CurrentUser.From(principal);
            await _audit.PublishAsync(new AuditEventDto
            {
                EntityType = "Doctor",
                EntityId = entity.Id,
                EntityCode = entity.PublicCode,
                Action = action,
                ActorId = user.Id,
                ActorUsername = user.Username,
                OccurredAtUtc = _clock.GetUtcNow().UtcDateTime,
                Summary = summary,
                DiffJson = diff,
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit publish failed for {Action} {EntityId} (fail-open)", action, entity.Id);
        }
    }
}
