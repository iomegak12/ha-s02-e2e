namespace Nexus.Branches.Api.Infrastructure.Errors;

/// <summary>Canonical machine-readable error codes emitted by the Branch service.</summary>
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
    public const string BranchValidation = "BRANCH_VALIDATION";

    /// <summary>409 — attempted to create a branch with a code already held by another active row.</summary>
    public const string BranchCodeDuplicate = "BRANCH_CODE_DUPLICATE";

    /// <summary>409 — <c>If-Match</c> ETag did not match the current row version.</summary>
    public const string EtagMismatch = "ETAG_MISMATCH";

    public const string TypeUriBase = "https://errors.nexusha.local/";
}
