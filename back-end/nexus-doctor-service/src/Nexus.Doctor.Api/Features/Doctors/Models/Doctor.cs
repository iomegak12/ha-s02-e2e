using Nexus.Doctors.Api.Infrastructure.Persistence;

namespace Nexus.Doctors.Api.Features.Doctors.Models;

/// <summary>A doctor record. Documents and branch links are stored in separate tables.</summary>
public sealed class Doctor : IAuditedEntity
{
    /// <summary>Server-assigned identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Human-readable identifier in the form <c>DOC-YYYY-BRN-NNNNNN</c>.</summary>
    public string PublicCode { get; set; } = string.Empty;

    /// <summary>Given name.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Family name.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>Derived <c>FirstName + " " + LastName</c> — kept for full-text search.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Contact phone.</summary>
    public string? Phone { get; set; }

    /// <summary>Contact email.</summary>
    public string? Email { get; set; }

    /// <summary>Medical specialisation (free-form).</summary>
    public string Specialisation { get; set; } = string.Empty;

    /// <summary>Practising medical licence number. Immutable once the doctor reaches Verified.</summary>
    public string LicenseNumber { get; set; } = string.Empty;

    /// <summary>Issuing authority for the licence.</summary>
    public string? LicenseAuthority { get; set; }

    /// <summary>External branch id treated as the doctor's primary branch.</summary>
    public Guid PrimaryBranchId { get; set; }

    /// <summary>Current lifecycle state.</summary>
    public DoctorStatus Status { get; set; } = DoctorStatus.Pending;

    /// <inheritdoc />
    public DateTime CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>SQL Server <c>rowversion</c> for optimistic concurrency / ETag.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    /// <summary>Verification documents.</summary>
    public ICollection<DoctorDocument> Documents { get; set; } = new List<DoctorDocument>();

    /// <summary>Branch associations.</summary>
    public ICollection<DoctorBranchLink> BranchLinks { get; set; } = new List<DoctorBranchLink>();
}
