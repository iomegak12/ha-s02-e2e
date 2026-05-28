using Nexus.Audit.Api.Infrastructure.Errors;

namespace Nexus.Audit.Api.Infrastructure.Idempotency;

/// <summary>
/// Endpoint filter that enforces the <c>Idempotency-Key</c> contract on
/// <c>appendAuditEntry</c>:
/// <list type="bullet">
///   <item>Missing or empty header → <c>400 IDEMPOTENCY_KEY_MISSING</c>.</item>
///   <item>Header longer than 128 chars → <c>400 IDEMPOTENCY_KEY_MISSING</c> (same code; treated as a malformed request).</item>
///   <item>Otherwise stores the resolved <see cref="IdempotencyContext"/> in <see cref="HttpContext.Items"/>.</item>
/// </list>
/// </summary>
public sealed class IdempotencyKeyFilter : IEndpointFilter
{
    /// <summary>Header carrying the caller-supplied idempotency key.</summary>
    public const string HeaderName = "Idempotency-Key";

    /// <summary>Maximum key length (matches DB column width).</summary>
    public const int MaxKeyLength = 128;

    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (!http.Request.Headers.TryGetValue(HeaderName, out var header))
        {
            throw new DomainException(
                code: ErrorCodes.IdempotencyKeyMissing,
                status: StatusCodes.Status400BadRequest,
                title: "Missing Idempotency-Key",
                detail: $"The '{HeaderName}' request header is required on this endpoint.");
        }

        var key = header.ToString().Trim();
        if (string.IsNullOrEmpty(key))
        {
            throw new DomainException(
                code: ErrorCodes.IdempotencyKeyMissing,
                status: StatusCodes.Status400BadRequest,
                title: "Missing Idempotency-Key",
                detail: $"The '{HeaderName}' header was present but empty.");
        }

        if (key.Length > MaxKeyLength)
        {
            throw new DomainException(
                code: ErrorCodes.IdempotencyKeyMissing,
                status: StatusCodes.Status400BadRequest,
                title: "Invalid Idempotency-Key",
                detail: $"The '{HeaderName}' header must not exceed {MaxKeyLength} characters.");
        }

        var sourceService = SourceServiceResolver.Resolve(http);
        http.Items[IdempotencyContext.HttpItemsKey] = new IdempotencyContext(key, sourceService);

        return next(context);
    }
}
