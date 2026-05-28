namespace Nexus.Audit.Api.Infrastructure.Idempotency;

/// <summary>
/// Resolved per-request idempotency context: the caller-supplied <c>Idempotency-Key</c>
/// header value and the lower-cased source service identifier. Stashed in
/// <c>HttpContext.Items</c> by <see cref="IdempotencyKeyFilter"/> and consumed by the
/// audit service when it materialises an entry.
/// </summary>
/// <param name="IdempotencyKey">The literal value of the caller's <c>Idempotency-Key</c> header.</param>
/// <param name="SourceService">Lower-cased identifier of the calling service.</param>
public sealed record IdempotencyContext(string IdempotencyKey, string SourceService)
{
    /// <summary><see cref="HttpContext.Items"/> key.</summary>
    public const string HttpItemsKey = "nexus.audit.idempotency-context";
}
