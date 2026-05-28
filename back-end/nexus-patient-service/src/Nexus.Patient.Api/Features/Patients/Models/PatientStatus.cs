namespace Nexus.Patients.Api.Features.Patients.Models;

/// <summary>Lifecycle states for <see cref="Patient"/> per LLD §6.1.</summary>
public enum PatientStatus : byte
{
    /// <summary>Initial state on creation.</summary>
    Draft = 0,

    /// <summary>Activated patient, eligible for clinical workflows.</summary>
    Active = 1,

    /// <summary>Archived patient. Terminal state — no re-activation allowed.</summary>
    Archived = 2,
}
