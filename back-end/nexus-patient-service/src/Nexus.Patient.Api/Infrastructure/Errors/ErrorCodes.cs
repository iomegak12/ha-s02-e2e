namespace Nexus.Patients.Api.Infrastructure.Errors;

/// <summary>Canonical machine-readable error codes emitted by the Patient service.</summary>
public static class ErrorCodes
{
    public const string BadRequest = "BAD_REQUEST";
    public const string IdempotencyKeyMissing = "IDEMPOTENCY_KEY_MISSING";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string NotFound = "NOT_FOUND";
    public const string RateLimited = "RATE_LIMITED";
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>422 — FluentValidation rejected the request body.</summary>
    public const string PatientValidation = "PATIENT_VALIDATION";

    /// <summary>409 — duplicate phone+DOB or email+DOB detected on create.</summary>
    public const string DuplicateIdentity = "DUPLICATE_IDENTITY";

    /// <summary>409 — attempted illegal lifecycle transition (e.g. Archived → Active).</summary>
    public const string LifecycleInvalidTransition = "LIFECYCLE_INVALID_TRANSITION";

    /// <summary>422 — an Active patient must have exactly one primary active branch link.</summary>
    public const string PrimaryBranchRequired = "PRIMARY_BRANCH_REQUIRED";

    /// <summary>422 — referenced branch does not exist in the Branches service.</summary>
    public const string BranchUnknown = "BRANCH_UNKNOWN";

    /// <summary>409 — patient is already linked to this branch (live link).</summary>
    public const string AlreadyLinked = "ALREADY_LINKED";

    /// <summary>409 — <c>If-Match</c> ETag did not match the current row version.</summary>
    public const string EtagMismatch = "ETAG_MISMATCH";

    /// <summary>503 — downstream Branches service unavailable (circuit open or timeout).</summary>
    public const string DependencyUnavailable = "DEPENDENCY_UNAVAILABLE";

    public const string TypeUriBase = "https://errors.nexusha.local/";
}
