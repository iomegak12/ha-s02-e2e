namespace Nexus.Doctors.Api.Features.Doctors.Models;

/// <summary>
/// Verification document uploaded for a doctor. Streams to <c>IDocumentStorage</c>
/// at upload time (Phase 7). Soft-deleted via <see cref="DeletedAtUtc"/>.
/// </summary>
public sealed class DoctorDocument
{
    /// <summary>Server-assigned identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning doctor.</summary>
    public Guid DoctorId { get; set; }

    /// <summary>Document category — one of <see cref="DocumentKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Caller-supplied original filename (preserved verbatim).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME content type at upload.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>SHA-256 (hex) computed at upload. Filtered-unique per doctor while not soft-deleted.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Relative key inside the storage root; resolves to the on-disk path.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Review state.</summary>
    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;

    /// <summary>Whether this document is mandatory for verification (e.g. medical licence).</summary>
    public bool IsRequired { get; set; }

    /// <summary>Reviewer's note (required on Rejected).</summary>
    public string? ReviewNote { get; set; }

    /// <summary>When the reviewer issued a decision.</summary>
    public DateTime? ReviewedAtUtc { get; set; }

    /// <summary>Reviewer's username (denormalised for traceability).</summary>
    public string? ReviewedByUsername { get; set; }

    /// <summary>When the document was uploaded.</summary>
    public DateTime UploadedAtUtc { get; set; }

    /// <summary>Set when the document is soft-deleted.</summary>
    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>Navigation back to the doctor.</summary>
    public Doctor? Doctor { get; set; }
}

/// <summary>Document review lifecycle.</summary>
public enum DocumentStatus : byte
{
    /// <summary>Just uploaded; awaiting reviewer.</summary>
    Uploaded = 0,

    /// <summary>Reviewer accepted the document.</summary>
    Verified = 1,

    /// <summary>Reviewer rejected the document (a note explains why).</summary>
    Rejected = 2,
}

/// <summary>Canonical document categories per IMPLEMENTATION_PLAN.</summary>
public static class DocumentKinds
{
    public const string MedicalLicense = "MedicalLicense";
    public const string BoardCertification = "BoardCertification";
    public const string GovernmentId = "GovernmentId";
    public const string ProofOfAddress = "ProofOfAddress";
    public const string Other = "Other";

    /// <summary>Ordered set of recognised kinds.</summary>
    public static readonly IReadOnlyCollection<string> All = new[]
    {
        MedicalLicense, BoardCertification, GovernmentId, ProofOfAddress, Other,
    };
}
