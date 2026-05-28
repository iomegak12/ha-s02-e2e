using Nexus.Patients.Api.Infrastructure.Errors;

namespace Nexus.Patients.Api.Infrastructure.Idempotency;

/// <summary>
/// Endpoint filter that enforces the <c>Idempotency-Key</c> contract on write endpoints:
/// missing / empty / >128 chars → <c>400 IDEMPOTENCY_KEY_MISSING</c>.
/// </summary>
public sealed class IdempotencyKeyFilter : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    public const int MaxKeyLength = 128;

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (!http.Request.Headers.TryGetValue(HeaderName, out var header))
        {
            throw new DomainException(
                ErrorCodes.IdempotencyKeyMissing, StatusCodes.Status400BadRequest,
                "Missing Idempotency-Key", $"The '{HeaderName}' request header is required on this endpoint.");
        }

        var key = header.ToString().Trim();
        if (string.IsNullOrEmpty(key))
        {
            throw new DomainException(
                ErrorCodes.IdempotencyKeyMissing, StatusCodes.Status400BadRequest,
                "Missing Idempotency-Key", $"The '{HeaderName}' header was present but empty.");
        }

        if (key.Length > MaxKeyLength)
        {
            throw new DomainException(
                ErrorCodes.IdempotencyKeyMissing, StatusCodes.Status400BadRequest,
                "Invalid Idempotency-Key", $"The '{HeaderName}' header must not exceed {MaxKeyLength} characters.");
        }

        var sourceService = SourceServiceResolver.Resolve(http);
        http.Items[IdempotencyContext.HttpItemsKey] = new IdempotencyContext(key, sourceService);

        return next(context);
    }
}
