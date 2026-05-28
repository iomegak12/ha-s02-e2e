namespace Nexus.Doctors.Api.Infrastructure.Errors;

/// <summary>Canonical machine-readable error codes emitted by the Doctor service.</summary>
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
    public const string DoctorValidation = "DOCTOR_VALIDATION";

    /// <summary>409 — license number already held by another non-Deactivated doctor.</summary>
    public const string DuplicateLicense = "DUPLICATE_LICENSE";

    /// <summary>409 — attempted illegal lifecycle transition.</summary>
    public const string LifecycleInvalidTransition = "LIFECYCLE_INVALID_TRANSITION";

    /// <summary>409 — verification blocked because required documents are not all Verified.</summary>
    public const string LifecycleBlocked = "LIFECYCLE_BLOCKED";

    /// <summary>422 — an Active doctor must have exactly one primary active branch link.</summary>
    public const string PrimaryBranchRequired = "PRIMARY_BRANCH_REQUIRED";

    /// <summary>422 — referenced branch does not exist.</summary>
    public const string BranchUnknown = "BRANCH_UNKNOWN";

    /// <summary>409 — doctor is already linked to this branch (live link).</summary>
    public const string AlreadyLinked = "ALREADY_LINKED";

    /// <summary>409 — <c>If-Match</c> ETag did not match the current row version.</summary>
    public const string EtagMismatch = "ETAG_MISMATCH";

    /// <summary>409 — document upload / delete blocked because doctor is Verified or beyond.</summary>
    public const string DocumentLocked = "DOCUMENT_LOCKED";

    /// <summary>413 — uploaded document exceeds <c>Documents:MaxFileBytes</c>.</summary>
    public const string DocumentTooLarge = "DOCUMENT_TOO_LARGE";

    /// <summary>415 — uploaded document's content type is not in the allowed list.</summary>
    public const string DocumentUnsupportedType = "DOCUMENT_UNSUPPORTED_TYPE";

    /// <summary>409 — another active document for this doctor already has this SHA-256.</summary>
    public const string DocumentDuplicate = "DOCUMENT_DUPLICATE";

    /// <summary>503 — downstream Branches service unavailable.</summary>
    public const string DependencyUnavailable = "DEPENDENCY_UNAVAILABLE";

    public const string TypeUriBase = "https://errors.nexusha.local/";
}
