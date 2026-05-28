namespace Nexus.Audit.Api.Infrastructure.Idempotency;

/// <summary>
/// Resolves the caller's source-service identifier from request state.
/// Preference order:
/// <list type="number">
///   <item><c>X-Source-Service</c> request header (if non-empty).</item>
///   <item>JWT <c>iss</c> claim (populated once <c>UseAuthentication</c> runs).</item>
///   <item>The literal <c>"unknown"</c>.</item>
/// </list>
/// The returned value is always lower-cased and trimmed.
/// </summary>
public static class SourceServiceResolver
{
    /// <summary>Header carrying the optional source-service hint.</summary>
    public const string HeaderName = "X-Source-Service";

    /// <summary>Fallback when no header and no <c>iss</c> claim are available.</summary>
    public const string Unknown = "unknown";

    /// <summary>Resolve the source-service identifier from <paramref name="context"/>.</summary>
    public static string Resolve(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var header))
        {
            var headerValue = header.ToString().Trim();
            if (!string.IsNullOrEmpty(headerValue))
            {
                return headerValue.ToLowerInvariant();
            }
        }

        var iss = context.User?.FindFirst("iss")?.Value;
        if (!string.IsNullOrWhiteSpace(iss))
        {
            return iss.Trim().ToLowerInvariant();
        }

        return Unknown;
    }
}
