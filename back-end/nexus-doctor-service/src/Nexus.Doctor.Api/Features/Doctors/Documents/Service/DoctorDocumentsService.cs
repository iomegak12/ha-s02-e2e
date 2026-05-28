using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.Doctors.Api.Configuration;
using Nexus.Doctors.Api.Features.Doctors.Documents.Models;
using Nexus.Doctors.Api.Features.Doctors.Documents.Repository;
using Nexus.Doctors.Api.Features.Doctors.Models;
using Nexus.Doctors.Api.Features.Doctors.Repository;
using Nexus.Doctors.Api.Infrastructure.Audit;
using Nexus.Doctors.Api.Infrastructure.Auth;
using Nexus.Doctors.Api.Infrastructure.Documents;
using Nexus.Doctors.Api.Infrastructure.Errors;
using Nexus.Doctors.Api.Infrastructure.Observability;

namespace Nexus.Doctors.Api.Features.Doctors.Documents.Service;

/// <summary>
/// Business surface for the 5 doctor-document operations: list / upload (streaming) /
/// get-metadata / review / soft-delete. Enforces the document-locked invariant
/// (uploads + deletes refused once the doctor reaches <c>Verified</c>) and SHA-256
/// dedup via the DB unique index.
/// </summary>
public interface IDoctorDocumentsService
{
    Task<IReadOnlyList<DocumentDto>> ListAsync(Guid doctorId, CancellationToken ct);
    Task<DocumentDto> UploadAsync(Guid doctorId, string kind, string fileName, string contentType, Stream content, ClaimsPrincipal principal, CancellationToken ct);
    Task<DocumentDto?> GetAsync(Guid doctorId, Guid documentId, CancellationToken ct);
    Task<DocumentDto> ReviewAsync(Guid doctorId, Guid documentId, DocumentReviewRequest request, ClaimsPrincipal principal, CancellationToken ct);
    Task DeleteAsync(Guid doctorId, Guid documentId, ClaimsPrincipal principal, CancellationToken ct);
}

/// <inheritdoc />
public sealed class DoctorDocumentsService : IDoctorDocumentsService
{
    private readonly IDoctorRepository _doctors;
    private readonly IDoctorDocumentRepository _documents;
    private readonly IDocumentStorage _storage;
    private readonly IAuditPublisher _audit;
    private readonly DocumentsOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<DoctorDocumentsService> _logger;

    public DoctorDocumentsService(
        IDoctorRepository doctors,
        IDoctorDocumentRepository documents,
        IDocumentStorage storage,
        IAuditPublisher audit,
        IOptions<DocumentsOptions> options,
        TimeProvider clock,
        ILogger<DoctorDocumentsService> logger)
    {
        _doctors = doctors;
        _documents = documents;
        _storage = storage;
        _audit = audit;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DocumentDto>> ListAsync(Guid doctorId, CancellationToken ct)
    {
        await EnsureDoctorExistsAsync(doctorId, ct);
        var rows = await _documents.ListAsync(doctorId, includeDeleted: false, ct);
        return rows.Select(DocumentDto.From).ToList();
    }

    public async Task<DocumentDto> UploadAsync(Guid doctorId, string kind, string fileName, string contentType, Stream content, ClaimsPrincipal principal, CancellationToken ct)
    {
        var doctor = await _doctors.GetByIdAsync(doctorId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "Not Found", "Doctor was not found.");

        EnsureNotLocked(doctor.Status);

        if (!DocumentKinds.All.Contains(kind))
        {
            throw new DomainException(
                ErrorCodes.DoctorValidation, StatusCodes.Status422UnprocessableEntity,
                "Validation failed", $"kind must be one of {string.Join(", ", DocumentKinds.All)}.");
        }

        var ct1 = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        if (!_options.AllowedContentTypes.Contains(ct1))
        {
            throw new DomainException(
                ErrorCodes.DocumentUnsupportedType, StatusCodes.Status415UnsupportedMediaType,
                "Unsupported Media Type", $"Content type '{ct1}' is not in the allowed list.");
        }

        // Stream into storage under a temporary key, then move/rename once we know the SHA.
        var storageKey = $"doctors/{doctorId}/{Guid.CreateVersion7()}/{Sanitize(fileName)}";

        // Limit-aware copy: count bytes during the storage stream call by wrapping the source.
        var limited = new LimitStream(content, _options.MaxFileBytes);
        StoredDocument stored;
        try
        {
            stored = await _storage.StoreAsync(storageKey, limited, ct);
        }
        catch (DomainException) { throw; }

        if (stored.SizeBytes > _options.MaxFileBytes)
        {
            await _storage.DeleteAsync(storageKey, ct);
            throw new DomainException(
                ErrorCodes.DocumentTooLarge, StatusCodes.Status413PayloadTooLarge,
                "Payload Too Large", $"File exceeds the {_options.MaxFileBytes / (1024 * 1024)}MB limit.");
        }

        var existing = await _documents.FindActiveBySha256Async(doctorId, stored.Sha256Hex, ct);
        if (existing is not null)
        {
            await _storage.DeleteAsync(storageKey, ct);
            return DocumentDto.From(existing);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var doc = new DoctorDocument
        {
            Id = Guid.CreateVersion7(),
            DoctorId = doctorId,
            Kind = kind,
            FileName = fileName,
            ContentType = ct1,
            Sha256 = stored.Sha256Hex,
            SizeBytes = stored.SizeBytes,
            StorageKey = storageKey,
            Status = DocumentStatus.Uploaded,
            IsRequired = kind == DocumentKinds.MedicalLicense,
            UploadedAtUtc = now,
        };

        try
        {
            await _documents.AddAsync(doc, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await _storage.DeleteAsync(storageKey, ct);
            var concurrent = await _documents.FindActiveBySha256Async(doctorId, stored.Sha256Hex, ct);
            if (concurrent is not null) return DocumentDto.From(concurrent);
            throw new DomainException(
                ErrorCodes.DocumentDuplicate, StatusCodes.Status409Conflict,
                "Conflict", "A document with this content already exists for this doctor.");
        }

        DoctorMetrics.DocumentsUploaded.Add(1,
            new KeyValuePair<string, object?>("kind", kind),
            new KeyValuePair<string, object?>("result", "stored"));

        await EmitAuditAsync(principal, doctor, doc, "DoctorDocument.Uploaded",
            $"Doctor {doctor.PublicCode} document {kind} uploaded",
            JsonSerializer.Serialize(new { doc.Id, doc.Kind, doc.ContentType, doc.SizeBytes, doc.Sha256 }), ct);

        return DocumentDto.From(doc);
    }

    public async Task<DocumentDto?> GetAsync(Guid doctorId, Guid documentId, CancellationToken ct)
    {
        await EnsureDoctorExistsAsync(doctorId, ct);
        var doc = await _documents.GetAsync(doctorId, documentId, ct);
        if (doc is null || doc.DeletedAtUtc is not null) return null;
        return DocumentDto.From(doc);
    }

    public async Task<DocumentDto> ReviewAsync(Guid doctorId, Guid documentId, DocumentReviewRequest request, ClaimsPrincipal principal, CancellationToken ct)
    {
        var doctor = await _doctors.GetByIdAsync(doctorId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "Not Found", "Doctor was not found.");

        var doc = await _documents.GetAsync(doctorId, documentId, ct);
        if (doc is null || doc.DeletedAtUtc is not null)
        {
            throw new DomainException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "Not Found", "Document was not found.");
        }

        if (!Enum.TryParse<DocumentStatus>(request.Status, ignoreCase: true, out var target)
            || target == DocumentStatus.Uploaded)
        {
            throw new DomainException(
                ErrorCodes.DoctorValidation, StatusCodes.Status422UnprocessableEntity,
                "Validation failed", "status must be Verified or Rejected.");
        }

        if (target == DocumentStatus.Rejected && string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            throw new DomainException(
                ErrorCodes.DoctorValidation, StatusCodes.Status422UnprocessableEntity,
                "Validation failed", "rejectionReason is required when status is Rejected.");
        }

        if (doc.Status != DocumentStatus.Uploaded)
        {
            throw new DomainException(
                ErrorCodes.LifecycleInvalidTransition, StatusCodes.Status409Conflict,
                "Conflict", $"Cannot transition document from {doc.Status} to {target}.");
        }

        var user = CurrentUser.From(principal);
        var now = _clock.GetUtcNow().UtcDateTime;
        doc.Status = target;
        doc.ReviewedAtUtc = now;
        doc.ReviewedByUsername = user.Username;
        doc.ReviewNote = target == DocumentStatus.Rejected ? request.RejectionReason : null;
        await _documents.SaveChangesAsync(ct);

        DoctorMetrics.DocumentsReviewed.Add(1,
            new KeyValuePair<string, object?>("decision", target.ToString()));

        await EmitAuditAsync(principal, doctor, doc, "DoctorDocument.Reviewed",
            $"Document {doc.Id} {target}",
            JsonSerializer.Serialize(new { doc.Id, decision = target.ToString(), doc.ReviewNote }), ct);

        return DocumentDto.From(doc);
    }

    public async Task DeleteAsync(Guid doctorId, Guid documentId, ClaimsPrincipal principal, CancellationToken ct)
    {
        var doctor = await _doctors.GetByIdAsync(doctorId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "Not Found", "Doctor was not found.");

        EnsureNotLocked(doctor.Status);

        var doc = await _documents.GetAsync(doctorId, documentId, ct);
        if (doc is null || doc.DeletedAtUtc is not null)
        {
            throw new DomainException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "Not Found", "Document was not found.");
        }

        if (doc.Status == DocumentStatus.Verified)
        {
            throw new DomainException(
                ErrorCodes.DocumentLocked, StatusCodes.Status409Conflict,
                "Conflict", "Verified documents cannot be removed.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        doc.DeletedAtUtc = now;
        await _documents.SaveChangesAsync(ct);

        try { await _storage.DeleteAsync(doc.StorageKey, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Storage delete failed for {Key}", doc.StorageKey); }

        await EmitAuditAsync(principal, doctor, doc, "DoctorDocument.Deleted",
            $"Document {doc.Id} deleted",
            JsonSerializer.Serialize(new { doc.Id, doc.Kind }), ct);
    }

    // -------------------- helpers --------------------

    private async Task EnsureDoctorExistsAsync(Guid doctorId, CancellationToken ct)
    {
        var doctor = await _doctors.GetByIdNoTrackingAsync(doctorId, ct);
        if (doctor is null)
            throw new DomainException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "Not Found", "Doctor was not found.");
    }

    private static void EnsureNotLocked(DoctorStatus status)
    {
        if (status is DoctorStatus.Verified or DoctorStatus.Approved or DoctorStatus.Active)
        {
            throw new DomainException(
                ErrorCodes.DocumentLocked, StatusCodes.Status409Conflict,
                "Conflict", $"Documents are locked while the doctor is {status}.");
        }
    }

    private static string Sanitize(string fileName)
    {
        var name = Path.GetFileName(fileName ?? "file");
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "file" : name;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("UX_DoctorDocuments_Doctor_Sha", StringComparison.OrdinalIgnoreCase);
    }

    private async Task EmitAuditAsync(ClaimsPrincipal principal, Doctor doctor, DoctorDocument doc, string action, string summary, string? diff, CancellationToken ct)
    {
        try
        {
            var user = CurrentUser.From(principal);
            await _audit.PublishAsync(new AuditEventDto
            {
                EntityType = "DoctorDocument",
                EntityId = doc.Id,
                EntityCode = doctor.PublicCode,
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
            _logger.LogWarning(ex, "Audit publish failed for {Action} {Id} (fail-open)", action, doc.Id);
        }
    }

    /// <summary>Wraps a source stream and throws once <paramref name="MaxBytes"/> is exceeded.</summary>
    private sealed class LimitStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _max;
        private long _read;

        public LimitStream(Stream inner, long max) { _inner = inner; _max = max; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read > _max) throw TooLarge();
            var n = _inner.Read(buffer, offset, count);
            _read += n;
            return n;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_read > _max) throw TooLarge();
            var n = await _inner.ReadAsync(buffer, ct);
            _read += n;
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private static DomainException TooLarge() =>
            new(ErrorCodes.DocumentTooLarge, StatusCodes.Status413PayloadTooLarge,
                "Payload Too Large", "File exceeds the configured size limit.");
    }
}
