namespace Nexus.Doctors.Api.Features.Doctors.Models;

/// <summary>Per-branch / per-year monotonic counter for <c>DOC-YYYY-BRN-NNNNNN</c> codes.</summary>
public sealed class DoctorCodeSequence
{
    public string BranchCode { get; set; } = string.Empty;
    public short Year { get; set; }
    public int LastValue { get; set; }
}
