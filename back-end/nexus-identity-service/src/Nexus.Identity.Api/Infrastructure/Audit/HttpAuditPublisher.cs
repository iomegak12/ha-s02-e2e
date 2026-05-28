using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Nexus.Identity.Api.Infrastructure.Audit;

/// <summary>
/// Typed-<see cref="HttpClient"/> publisher that POSTs audit entries to the Audit service.
/// Forwards the caller's bearer token (if any) and a fresh <c>Idempotency-Key</c> (UUID v7)
/// per call. All failures are logged and swallowed.
/// </summary>
public sealed class HttpAuditPublisher : IAuditPublisher
{
    /// <summary>Name used to register the typed <see cref="HttpClient"/>.</summary>
    public const string HttpClientName = "AuditClient";

    private readonly HttpClient _client;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<HttpAuditPublisher> _logger;

    /// <summary>Create the publisher.</summary>
    public HttpAuditPublisher(
        HttpClient client,
        IHttpContextAccessor httpContextAccessor,
        ILogger<HttpAuditPublisher> logger)
    {
        _client = client;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PublishAsync(AuditEntryDto entry, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/audit")
            {
                Content = JsonContent.Create(entry),
            };

            request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

            var bearer = ExtractCallerBearer();
            if (!string.IsNullOrEmpty(bearer))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            }

            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Audit publish failed: {Status} {Reason} for {Action} {EntityType} {EntityId}",
                    (int)response.StatusCode, response.ReasonPhrase, entry.Action, entry.EntityType, entry.EntityId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Request cancelled by caller — nothing to do.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Audit publish threw for {Action} {EntityType} {EntityId} (swallowed)",
                entry.Action, entry.EntityType, entry.EntityId);
        }
    }

    private string? ExtractCallerBearer()
    {
        var http = _httpContextAccessor.HttpContext;
        if (http is null)
        {
            return null;
        }

        var header = http.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
    }
}
