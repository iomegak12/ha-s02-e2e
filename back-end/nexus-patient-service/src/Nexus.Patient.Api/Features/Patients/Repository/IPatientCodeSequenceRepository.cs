namespace Nexus.Patients.Api.Features.Patients.Repository;

/// <summary>
/// Atomic counter access for the patient public-code generator. Phase 5 calls
/// <see cref="NextAsync"/> inside the business transaction.
/// </summary>
public interface IPatientCodeSequenceRepository
{
    /// <summary>
    /// Reserve and return the next value for the given <paramref name="branchCode"/> +
    /// <paramref name="year"/> pair. Creates the row on first use.
    /// </summary>
    Task<int> NextAsync(string branchCode, short year, CancellationToken ct);
}
