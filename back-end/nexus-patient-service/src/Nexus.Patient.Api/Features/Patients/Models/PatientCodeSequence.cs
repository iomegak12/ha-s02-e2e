namespace Nexus.Patients.Api.Features.Patients.Models;

/// <summary>
/// Per-branch / per-year monotonic counter used to mint the <c>PAT-YYYY-BRN-NNNNNN</c>
/// public code. One row exists per <c>(BranchCode, Year)</c> pair; <see cref="LastValue"/>
/// is incremented inside the same EF transaction as the new patient insert.
/// </summary>
public sealed class PatientCodeSequence
{
    /// <summary>Branch code (upper-case, ≤ 8 chars).</summary>
    public string BranchCode { get; set; } = string.Empty;

    /// <summary>Year (calendar UTC).</summary>
    public short Year { get; set; }

    /// <summary>Last value handed out for this branch/year pair.</summary>
    public int LastValue { get; set; }
}
