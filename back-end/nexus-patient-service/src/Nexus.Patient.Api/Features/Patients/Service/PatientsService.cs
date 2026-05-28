using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nexus.Patients.Api.Features.Patients.Models;
using Nexus.Patients.Api.Features.Patients.Repository;
using Nexus.Patients.Api.Infrastructure.Audit;
using Nexus.Patients.Api.Infrastructure.Auth;
using Nexus.Patients.Api.Infrastructure.Branches;
using Nexus.Patients.Api.Infrastructure.Errors;
using Nexus.Patients.Api.Infrastructure.Observability;

namespace Nexus.Patients.Api.Features.Patients.Service;

/// <summary>Outcome wrapper carrying both the patient projection and its ETag header value.</summary>
public sealed record PatientWithETag(PatientDto Patient, string ETag);

/// <summary>Business surface for the 9 Patient operations.</summary>
public interface IPatientsService
{
    Task<PagedPatientResponse> ListAsync(PatientStatus? status, Guid? branchId, string? q, int page, int size, CancellationToken ct);
    Task<PatientWithETag?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<PatientWithETag> CreateAsync(PatientCreateRequest request, ClaimsPrincipal principal, CancellationToken ct);
    Task<PatientWithETag> PatchAsync(Guid id, PatientPatchRequest request, string? ifMatch, ClaimsPrincipal principal, CancellationToken ct);
    Task<PatientWithETag> ActivateAsync(Guid id, ClaimsPrincipal principal, CancellationToken ct);
    Task<PatientWithETag> ArchiveAsync(Guid id, ClaimsPrincipal principal, CancellationToken ct);
    Task<IReadOnlyList<PatientBranchLinkDto>> ListBranchesAsync(Guid id, CancellationToken ct);
    Task<PatientBranchLinkDto> LinkBranchAsync(Guid id, PatientBranchLinkRequest req, ClaimsPrincipal principal, CancellationToken ct);
    Task UnlinkBranchAsync(Guid id, Guid branchId, ClaimsPrincipal principal, CancellationToken ct);
}

/// <inheritdoc />
public sealed class PatientsService : IPatientsService
{
    private readonly IPatientRepository _repo;
    private readonly IPublicCodeGenerator _codeGen;
    private readonly IBranchesClient _branches;
    private readonly IAuditPublisher _audit;
    private readonly TimeProvider _clock;
    private readonly ILogger<PatientsService> _logger;

    public PatientsService(
        IPatientRepository repo,
        IPublicCodeGenerator codeGen,
        IBranchesClient branches,
        IAuditPublisher audit,
        TimeProvider clock,
        ILogger<PatientsService> logger)
    {
        _repo = repo;
        _codeGen = codeGen;
        _branches = branches;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PagedPatientResponse> ListAsync(PatientStatus? status, Guid? branchId, string? q, int page, int size, CancellationToken ct)
    {
        var result = await _repo.QueryAsync(new PatientQuery(status, branchId, q, page, size), ct);
        return new PagedPatientResponse
        {
            Page = result.Page,
            Size = result.Size,
            TotalCount = result.Total,
            Items = result.Items.Select(PatientDto.From).ToList(),
        };
    }

    /// <inheritdoc />
    public async Task<PatientWithETag?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var p = await _repo.GetByIdNoTrackingAsync(id, ct);
        return p is null ? null : new PatientWithETag(PatientDto.From(p), FormatETag(p.RowVersion));
    }

    /// <inheritdoc />
    public async Task<PatientWithETag> CreateAsync(PatientCreateRequest request, ClaimsPrincipal principal, CancellationToken ct)
    {
        var branch = await _branches.GetBranchAsync(request.PrimaryBranchId, ct);
        if (branch is null || !branch.IsActive)
        {
            throw new DomainException(
                ErrorCodes.BranchUnknown, StatusCodes.Status422UnprocessableEntity,
                "Validation failed", "primaryBranchId does not refer to an active branch.");
        }

        var dob = DateOnly.ParseExact(request.DateOfBirth, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        await EnsureNoDuplicateAsync(request.PrimaryPhone, request.Email, dob, ct);

        var now = _clock.GetUtcNow().UtcDateTime;
        var year = (short)now.Year;
        var publicCode = await _codeGen.NextAsync(branch.Code, year, ct);

        var entity = new Patient
        {
            Id = Guid.CreateVersion7(),
            PublicCode = publicCode,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            FullName = $"{request.FirstName.Trim()} {request.LastName.Trim()}",
            Phone = request.PrimaryPhone.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            DateOfBirth = dob,
            Gender = request.Gender,
            AddressLine1 = request.AddressLine1.Trim(),
            AddressLine2 = request.AddressLine2?.Trim(),
            City = request.City.Trim(),
            State = request.State.Trim(),
            PostalCode = request.PostalCode.Trim(),
            Country = request.Country.Trim().ToUpperInvariant(),
            PrimaryBranchId = request.PrimaryBranchId,
            Status = PatientStatus.Draft,
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
                ErrorCodes.DuplicateIdentity, StatusCodes.Status409Conflict,
                "Conflict", "Patient with the same phone+DOB or email+DOB already exists.");
        }

        PatientMetrics.PatientsCreated.Add(1);
        await EmitAuditAsync(principal, entity, "Patient.Created", $"Patient {entity.PublicCode} created", SerializeFull(entity), ct);

        return new PatientWithETag(PatientDto.From(entity), FormatETag(entity.RowVersion));
    }

    /// <inheritdoc />
    public async Task<PatientWithETag> PatchAsync(Guid id, PatientPatchRequest request, string? ifMatch, ClaimsPrincipal principal, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(id, ct) ?? throw NotFound();

        if (!string.IsNullOrWhiteSpace(ifMatch) && !ETagsMatch(FormatETag(entity.RowVersion), ifMatch))
        {
            throw EtagMismatch();
        }

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
        if (request.PrimaryPhone is not null) entity.Phone = request.PrimaryPhone.Trim();
        if (request.Email is not null) entity.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (request.AddressLine1 is not null) entity.AddressLine1 = request.AddressLine1.Trim();
        if (request.AddressLine2 is not null) entity.AddressLine2 = request.AddressLine2.Trim();
        if (request.City is not null) entity.City = request.City.Trim();
        if (request.State is not null) entity.State = request.State.Trim();
        if (request.PostalCode is not null) entity.PostalCode = request.PostalCode.Trim();
        if (request.Country is not null) entity.Country = request.Country.Trim().ToUpperInvariant();
        entity.UpdatedAtUtc = _clock.GetUtcNow().UtcDateTime;

        try
        {
            await _repo.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { throw EtagMismatch(); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new DomainException(
                ErrorCodes.DuplicateIdentity, StatusCodes.Status409Conflict,
                "Conflict", "Patient with the same phone+DOB or email+DOB already exists.");
        }

        await EmitAuditAsync(principal, entity, "Patient.Updated", $"Patient {entity.PublicCode} updated",
            JsonSerializer.Serialize(new { before, after = SerializeFull(entity) }), ct);

        return new PatientWithETag(PatientDto.From(entity), FormatETag(entity.RowVersion));
    }

    /// <inheritdoc />
    public async Task<PatientWithETag> ActivateAsync(Guid id, ClaimsPrincipal principal, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(id, ct) ?? throw NotFound();

        if (entity.Status == PatientStatus.Active)
        {
            return new PatientWithETag(PatientDto.From(entity), FormatETag(entity.RowVersion));
        }
        if (entity.Status != PatientStatus.Draft)
        {
            throw new DomainException(
                ErrorCodes.LifecycleInvalidTransition, StatusCodes.Status409Conflict,
                "Conflict", $"Cannot transition from {entity.Status} to Active.");
        }

        // Primary-branch invariant: ensure a live primary link exists for the patient's PrimaryBranchId.
        var activeLinks = entity.BranchLinks.Where(l => l.UnlinkedAtUtc == null).ToList();
        if (!activeLinks.Any(l => l.BranchId == entity.PrimaryBranchId && l.IsPrimary))
        {
            // Auto-create primary link from the patient's PrimaryBranchId.
            entity.BranchLinks.Add(new PatientBranchLink
            {
                PatientId = entity.Id,
                BranchId = entity.PrimaryBranchId,
                IsPrimary = true,
                LinkedAtUtc = _clock.GetUtcNow().UtcDateTime,
            });
        }

        var from = entity.Status;
        entity.Status = PatientStatus.Active;
        entity.UpdatedAtUtc = _clock.GetUtcNow().UtcDateTime;
        await _repo.SaveChangesAsync(ct);

        PatientMetrics.StateTransitions.Add(1,
            new KeyValuePair<string, object?>("from", from.ToString()),
            new KeyValuePair<string, object?>("to", entity.Status.ToString()));

        await EmitAuditAsync(principal, entity, "Patient.StateChanged",
            $"Patient {entity.PublicCode} {from} → {entity.Status}", null, ct);

        return new PatientWithETag(PatientDto.From(entity), FormatETag(entity.RowVersion));
    }

    /// <inheritdoc />
    public async Task<PatientWithETag> ArchiveAsync(Guid id, ClaimsPrincipal principal, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(id, ct) ?? throw NotFound();
        if (entity.Status == PatientStatus.Archived)
        {
            return new PatientWithETag(PatientDto.From(entity), FormatETag(entity.RowVersion));
        }

        var from = entity.Status;
        var now = _clock.GetUtcNow().UtcDateTime;
        entity.Status = PatientStatus.Archived;
        entity.ArchivedAtUtc = now;
        entity.UpdatedAtUtc = now;
        await _repo.SaveChangesAsync(ct);

        PatientMetrics.StateTransitions.Add(1,
            new KeyValuePair<string, object?>("from", from.ToString()),
            new KeyValuePair<string, object?>("to", entity.Status.ToString()));

        await EmitAuditAsync(principal, entity, "Patient.StateChanged",
            $"Patient {entity.PublicCode} {from} → {entity.Status}", null, ct);

        return new PatientWithETag(PatientDto.From(entity), FormatETag(entity.RowVersion));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PatientBranchLinkDto>> ListBranchesAsync(Guid id, CancellationToken ct)
    {
        var patient = await _repo.GetByIdNoTrackingAsync(id, ct) ?? throw NotFound();
        return patient.BranchLinks
            .Where(l => l.UnlinkedAtUtc == null)
            .Select(l => new PatientBranchLinkDto
            {
                BranchId = l.BranchId,
                IsPrimary = l.IsPrimary,
                LinkedAtUtc = l.LinkedAtUtc,
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<PatientBranchLinkDto> LinkBranchAsync(Guid id, PatientBranchLinkRequest req, ClaimsPrincipal principal, CancellationToken ct)
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
                "Conflict", "Patient is already linked to this branch.");
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
            entity.BranchLinks.Add(new PatientBranchLink
            {
                PatientId = entity.Id,
                BranchId = req.BranchId,
                IsPrimary = req.IsPrimary,
                LinkedAtUtc = now,
            });
        }

        if (req.IsPrimary) entity.PrimaryBranchId = req.BranchId;
        entity.UpdatedAtUtc = now;
        await _repo.SaveChangesAsync(ct);

        PatientMetrics.BranchesLinked.Add(1);
        await EmitAuditAsync(principal, entity, "PatientBranch.Linked",
            $"Patient {entity.PublicCode} linked to branch {branch.Code}",
            JsonSerializer.Serialize(new { req.BranchId, branch.Code, req.IsPrimary }), ct);

        return new PatientBranchLinkDto
        {
            BranchId = req.BranchId,
            BranchCode = branch.Code,
            IsPrimary = req.IsPrimary,
            LinkedAtUtc = now,
        };
    }

    /// <inheritdoc />
    public async Task UnlinkBranchAsync(Guid id, Guid branchId, ClaimsPrincipal principal, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(id, ct) ?? throw NotFound();

        var link = entity.BranchLinks.FirstOrDefault(l => l.BranchId == branchId && l.UnlinkedAtUtc == null);
        if (link is null) return; // idempotent

        if (link.IsPrimary && entity.Status == PatientStatus.Active)
        {
            throw new DomainException(
                ErrorCodes.PrimaryBranchRequired, StatusCodes.Status409Conflict,
                "Conflict", "Reassign the primary branch before unlinking this one.");
        }

        link.UnlinkedAtUtc = _clock.GetUtcNow().UtcDateTime;
        entity.UpdatedAtUtc = link.UnlinkedAtUtc.Value;
        await _repo.SaveChangesAsync(ct);

        await EmitAuditAsync(principal, entity, "PatientBranch.Unlinked",
            $"Patient {entity.PublicCode} unlinked from branch {branchId}",
            JsonSerializer.Serialize(new { branchId }), ct);
    }

    // -------------------- helpers --------------------

    private async Task EnsureNoDuplicateAsync(string phone, string? email, DateOnly dob, CancellationToken ct)
    {
        var phoneHit = await _repo.FindByPhoneDobAsync(phone, dob, ct);
        if (phoneHit is not null)
            throw new DomainException(ErrorCodes.DuplicateIdentity, StatusCodes.Status409Conflict, "Conflict",
                "Patient with the same phone+DOB already exists.");

        if (!string.IsNullOrWhiteSpace(email))
        {
            var emailHit = await _repo.FindByEmailDobAsync(email, dob, ct);
            if (emailHit is not null)
                throw new DomainException(ErrorCodes.DuplicateIdentity, StatusCodes.Status409Conflict, "Conflict",
                    "Patient with the same email+DOB already exists.");
        }
    }

    private static DomainException NotFound() =>
        new(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "Not Found", "Patient was not found.");

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
            || msg.Contains("UX_Patients_Phone_DOB", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("UX_Patients_Email_DOB", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
    }

    private static string SerializeFull(Patient p) => JsonSerializer.Serialize(new
    {
        p.Id, p.PublicCode, p.FirstName, p.LastName, p.Phone, p.Email, p.DateOfBirth,
        p.Gender, p.AddressLine1, p.AddressLine2, p.City, p.State, p.PostalCode, p.Country,
        p.PrimaryBranchId, Status = p.Status.ToString(),
    });

    private async Task EmitAuditAsync(ClaimsPrincipal principal, Patient entity, string action, string summary, string? diff, CancellationToken ct)
    {
        try
        {
            var user = CurrentUser.From(principal);
            await _audit.PublishAsync(new AuditEventDto
            {
                EntityType = "Patient",
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
