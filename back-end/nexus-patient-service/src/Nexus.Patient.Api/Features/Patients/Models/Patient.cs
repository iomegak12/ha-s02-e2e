using Nexus.Patients.Api.Infrastructure.Persistence;

namespace Nexus.Patients.Api.Features.Patients.Models;

/// <summary>
/// A patient record. Persisted to <c>Patients</c> in the <c>NexusPatients</c> database.
/// Lifecycle is managed via <see cref="PatientStatus"/>; branch associations live in
/// <see cref="PatientBranchLink"/>.
/// </summary>
public sealed class Patient : IAuditedEntity
{
    /// <summary>Server-assigned identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Human-readable identifier in the form <c>PAT-YYYY-BRN-NNNNNN</c>.</summary>
    public string PublicCode { get; set; } = string.Empty;

    /// <summary>Given name.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Family name.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>Derived <c>FirstName + " " + LastName</c> — kept for full-text search.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Contact phone (E.164 preferred).</summary>
    public string? Phone { get; set; }

    /// <summary>Contact email. Optional.</summary>
    public string? Email { get; set; }

    /// <summary>Date of birth.</summary>
    public DateOnly DateOfBirth { get; set; }

    /// <summary>Gender enum-as-text.</summary>
    public string Gender { get; set; } = string.Empty;

    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public string? Country { get; set; }

    /// <summary>External branch id treated as the patient's primary branch.</summary>
    public Guid PrimaryBranchId { get; set; }

    /// <summary>Current lifecycle state.</summary>
    public PatientStatus Status { get; set; } = PatientStatus.Draft;

    /// <summary>Timestamp the patient transitioned into <see cref="PatientStatus.Archived"/>.</summary>
    public DateTime? ArchivedAtUtc { get; set; }

    /// <inheritdoc />
    public DateTime CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>SQL Server <c>rowversion</c> for optimistic concurrency / ETag.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    /// <summary>Branch associations (live + soft-deleted).</summary>
    public ICollection<PatientBranchLink> BranchLinks { get; set; } = new List<PatientBranchLink>();
}
