namespace Nexus.Audit.Api.Infrastructure.Errors;

/// <summary>
/// Canonical machine-readable error codes emitted by the Audit service.
/// Mirrors the <c>code</c> values defined in <c>specs/audit.openapi.json</c>.
/// </summary>
public static class ErrorCodes
{
    /// <summary>400 — malformed JSON or syntactically invalid request.</summary>
    public const string BadRequest = "BAD_REQUEST";

    /// <summary>400 — <c>appendAuditEntry</c> called without an <c>Idempotency-Key</c> header.</summary>
    public const string IdempotencyKeyMissing = "IDEMPOTENCY_KEY_MISSING";

    /// <summary>401 — bearer token missing or invalid.</summary>
    public const string Unauthenticated = "UNAUTHENTICATED";

    /// <summary>403 — caller is authenticated but lacks the <c>Admin</c> role.</summary>
    public const string Unauthorized = "UNAUTHORIZED";

    /// <summary>404 — addressed audit entry id does not exist.</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>409 — same <c>(SourceService, Idempotency-Key)</c> already used with a different payload.</summary>
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";

    /// <summary>422 — FluentValidation rejected the request body (including clock-skew failures).</summary>
    public const string AuditValidation = "AUDIT_VALIDATION";

    /// <summary>429 — rate limit window exceeded.</summary>
    public const string RateLimited = "RATE_LIMITED";

    /// <summary>500 — unexpected exception bubbled to the middleware.</summary>
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>Base URI used in <c>type</c> when materialising ProblemDetails.</summary>
    public const string TypeUriBase = "https://errors.nexusha.local/";
}
