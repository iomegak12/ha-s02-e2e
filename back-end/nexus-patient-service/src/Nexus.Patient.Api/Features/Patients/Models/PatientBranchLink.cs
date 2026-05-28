namespace Nexus.Patients.Api.Features.Patients.Models;

/// <summary>
/// Association of a patient to a branch. The branch lives in the Branch service;
/// there is no DB-level foreign key (LLD §9). Application-level validation against
/// the live <c>BranchesClient</c> guards correctness at link time.
/// </summary>
public sealed class PatientBranchLink
{
    /// <summary>Patient that owns the link.</summary>
    public Guid PatientId { get; set; }

    /// <summary>External branch identifier.</summary>
    public Guid BranchId { get; set; }

    /// <summary>Whether this is the patient's primary branch. Exactly one primary per active patient.</summary>
    public bool IsPrimary { get; set; }

    /// <summary>When the link was first created.</summary>
    public DateTime LinkedAtUtc { get; set; }

    /// <summary>Set when the link is soft-deleted. <c>null</c> while active.</summary>
    public DateTime? UnlinkedAtUtc { get; set; }

    /// <summary>Navigation property back to the patient.</summary>
    public Patient? Patient { get; set; }
}
