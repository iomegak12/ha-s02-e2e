namespace Nexus.Doctors.Api.Features.Doctors.Models;

/// <summary>Lifecycle states for <see cref="Doctor"/> per LLD §6.2.</summary>
public enum DoctorStatus : byte
{
    /// <summary>Initial state on creation.</summary>
    Pending = 0,

    /// <summary>All required verification documents are accepted.</summary>
    Verified = 1,

    /// <summary>Admin has approved the doctor for activation.</summary>
    Approved = 2,

    /// <summary>Active in the system. Re-activation from Deactivated is permitted.</summary>
    Active = 3,

    /// <summary>Soft-disabled. Re-activation moves the doctor back to <see cref="Active"/>.</summary>
    Deactivated = 4,
}
