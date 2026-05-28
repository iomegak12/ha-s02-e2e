namespace Nexus.Doctors.Api.Features.Doctors.Repository;

/// <summary>Atomic counter access for the doctor public-code generator.</summary>
public interface IDoctorCodeSequenceRepository
{
    /// <summary>Reserve and return the next value for the given branch/year pair.</summary>
    Task<int> NextAsync(string branchCode, short year, CancellationToken ct);
}
