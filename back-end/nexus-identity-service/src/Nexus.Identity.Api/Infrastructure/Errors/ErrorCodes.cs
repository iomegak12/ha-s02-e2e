namespace Nexus.Identity.Api.Infrastructure.Errors;

/// <summary>
/// Canonical machine-readable error codes emitted by the Identity service.
/// Mirrors the <c>code</c> values defined in <c>specs/identity.openapi.json</c>.
/// </summary>
public static class ErrorCodes
{
    /// <summary>400 — malformed JSON or syntactically invalid request.</summary>
    public const string BadRequest = "BAD_REQUEST";

    /// <summary>401 — bearer token missing or invalid.</summary>
    public const string Unauthenticated = "UNAUTHENTICATED";

    /// <summary>401 — credentials supplied to <c>POST /auth/token</c> did not match.</summary>
    public const string InvalidCredentials = "INVALID_CREDENTIALS";

    /// <summary>401 — refresh token unknown, expired, or already revoked.</summary>
    public const string RefreshInvalid = "REFRESH_INVALID";

    /// <summary>403 — caller is authenticated but lacks the <c>Admin</c> role.</summary>
    public const string Unauthorized = "UNAUTHORIZED";

    /// <summary>404 — addressed admin id does not exist.</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>409 — attempted to create an admin with a duplicate username.</summary>
    public const string AdminUsernameDuplicate = "ADMIN_USERNAME_DUPLICATE";

    /// <summary>409 — <c>If-Match</c> ETag did not match the current row version.</summary>
    public const string EtagMismatch = "ETAG_MISMATCH";

    /// <summary>422 — FluentValidation rejected the request body.</summary>
    public const string AdminValidation = "ADMIN_VALIDATION";

    /// <summary>429 — rate limit window exceeded for the caller IP.</summary>
    public const string RateLimited = "RATE_LIMITED";

    /// <summary>Base URI used in <c>type</c> when materialising ProblemDetails.</summary>
    public const string TypeUriBase = "https://errors.nexusha.local/";
}
