namespace Nexus.Patients.Api.Infrastructure.Idempotency;

/// <summary>
/// Resolves the caller's source-service identifier: X-Source-Service header → JWT iss claim → "unknown".
/// Always lower-cased.
/// </summary>
public static class SourceServiceResolver
{
    public const string HeaderName = "X-Source-Service";
    public const string Unknown = "unknown";

    public static string Resolve(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var header))
        {
            var headerValue = header.ToString().Trim();
            if (!string.IsNullOrEmpty(headerValue)) return headerValue.ToLowerInvariant();
        }

        var iss = context.User?.FindFirst("iss")?.Value;
        if (!string.IsNullOrWhiteSpace(iss)) return iss.Trim().ToLowerInvariant();

        return Unknown;
    }
}
