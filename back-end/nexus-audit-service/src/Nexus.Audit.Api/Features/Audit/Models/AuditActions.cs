namespace Nexus.Audit.Api.Features.Audit.Models;

/// <summary>
/// Allowed values for <see cref="AuditEntry.Action"/>. Stored as strings so callers
/// can extend the set without a schema change; the validator in Phase 2 enforces
/// membership.
/// </summary>
public static class AuditActions
{
    /// <summary>Entity was created.</summary>
    public const string Created = "Created";

    /// <summary>Entity was updated in-place.</summary>
    public const string Updated = "Updated";

    /// <summary>Entity status / lifecycle changed.</summary>
    public const string StatusChanged = "StatusChanged";

    /// <summary>An association was added.</summary>
    public const string Linked = "Linked";

    /// <summary>An association was removed.</summary>
    public const string Unlinked = "Unlinked";

    /// <summary>An admin reviewed an entity (verification / approval).</summary>
    public const string Reviewed = "Reviewed";

    /// <summary>A supporting document was uploaded.</summary>
    public const string Uploaded = "Uploaded";

    /// <summary>Entity was soft-deleted.</summary>
    public const string Deleted = "Deleted";

    /// <summary>Entity was hard-purged per retention policy.</summary>
    public const string Purged = "Purged";

    /// <summary>Canonical, ordered set used by validators and OpenAPI.</summary>
    public static readonly IReadOnlyCollection<string> All = new[]
    {
        Created, Updated, StatusChanged, Linked, Unlinked, Reviewed, Uploaded, Deleted, Purged,

        // Phase 4 (Branches) — flat actions per LLD §14.
        "BranchCreated", "BranchUpdated", "BranchDeactivated",

        // Phase 5 (Patients) — namespaced actions per LLD §14.
        "Patient.Created", "Patient.Updated", "Patient.StateChanged",
        "Patient.Purged",
        "PatientBranch.Linked", "PatientBranch.Unlinked",

        // Phase 6 (Doctors core).
        "Doctor.Created", "Doctor.Updated", "Doctor.StateChanged",
        "DoctorBranch.Linked", "DoctorBranch.Unlinked",

        // Phase 7 (Doctor documents).
        "DoctorDocument.Uploaded", "DoctorDocument.Reviewed", "DoctorDocument.Deleted",
    };

    /// <summary>Returns <c>true</c> if <paramref name="value"/> is one of the canonical actions.</summary>
    public static bool IsKnown(string? value) => value is not null && All.Contains(value);
}
