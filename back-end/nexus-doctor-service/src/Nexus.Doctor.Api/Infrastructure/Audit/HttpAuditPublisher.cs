using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Nexus.Doctors.Api.Configuration;

namespace Nexus.Doctors.Api.Infrastructure.Audit;

/// <summary>
/// Typed-<see cref="HttpClient"/> publisher that POSTs to <c>/api/v1/audit</c>.
/// Forwards the caller's bearer (if any) and sets <c>Idempotency-Key</c> +
/// <c>X-Source-Service</c>. All exceptions are caught — callers should never see
/// them; success / failure is signalled via <see cref="TryPublishAsync"/>.
/// </summary>
public sealed class HttpAuditPublisher
{
    /// <summary>Name used to register the typed <see cref="HttpClient"/>.</summary>
    public const string HttpClientName = "AuditClient";

    private readonly HttpClient _client;
    private readonly IHttpContextAccessor _http;
    private readonly IOptions<AuditClientOptions> _options;
    private readonly ILogger<HttpAuditPublisher> _logger;

    public HttpAuditPublisher(
        HttpClient client,
        IHttpContextAccessor http,
        IOptions<AuditClientOptions> options,
        ILogger<HttpAuditPublisher> logger)
    {
        _client = client;
        _http = http;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Attempt to publish. Returns <c>true</c> on 2xx, <c>false</c> on any other
    /// outcome (timeout, network failure, 4xx, 5xx). Never throws (except on the
    /// caller-cancellation token).
    /// </summary>
    public Task<bool> TryPublishAsync(AuditEventDto entry, CancellationToken ct)
        => TryPublishAsync(entry, Guid.CreateVersion7().ToString(), ct);

    /// <summary>
    /// Attempt to publish using a caller-supplied idempotency key (used by the
    /// reconciler so retries use the same key).
    /// </summary>
    public async Task<bool> TryPublishAsync(AuditEventDto entry, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/audit")
            {
                Content = JsonContent.Create(entry),
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            request.Headers.TryAddWithoutValidation("X-Source-Service", _options.Value.SourceService);

            var bearer = ExtractCallerBearer();
            if (!string.IsNullOrEmpty(bearer))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            }

            using var response = await _client.SendAsync(request, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            _logger.LogWarning(
                "Audit publish failed: {Status} {Reason} for {Action} {EntityType} {EntityId}",
                (int)response.StatusCode, response.ReasonPhrase, entry.Action, entry.EntityType, entry.EntityId);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Audit publish threw for {Action} {EntityType} {EntityId} (will outbox)",
                entry.Action, entry.EntityType, entry.EntityId);
            return false;
        }
    }

    private string? ExtractCallerBearer()
    {
        var http = _http.HttpContext;
        if (http is null) return null;

        var header = http.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)) return null;

        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
    }
}
