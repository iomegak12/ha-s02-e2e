namespace Nexus.Patients.Api.Infrastructure.Idempotency;

/// <summary>Resolved per-request idempotency context, stashed in <see cref="HttpContext.Items"/>.</summary>
public sealed record IdempotencyContext(string IdempotencyKey, string SourceService)
{
    public const string HttpItemsKey = "nexus.patients.idempotency-context";
}
