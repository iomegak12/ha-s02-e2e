namespace Nexus.Doctors.Api.Features.Doctors.Models;

/// <summary>Association of a doctor to a branch. No DB-level FK to the Branch service.</summary>
public sealed class DoctorBranchLink
{
    /// <summary>Doctor that owns the link.</summary>
    public Guid DoctorId { get; set; }

    /// <summary>External branch identifier.</summary>
    public Guid BranchId { get; set; }

    /// <summary>Whether this is the doctor's primary branch.</summary>
    public bool IsPrimary { get; set; }

    /// <summary>When the link was first created.</summary>
    public DateTime LinkedAtUtc { get; set; }

    /// <summary>Set when the link is soft-deleted.</summary>
    public DateTime? UnlinkedAtUtc { get; set; }

    /// <summary>Navigation back to the doctor.</summary>
    public Doctor? Doctor { get; set; }
}
