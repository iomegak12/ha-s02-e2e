using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Nexus.Patients.Api.Configuration;

namespace Nexus.Patients.Api.Infrastructure.Audit;

/// <summary>
/// Calls the audit service's admin-only PII redact hook
/// (<c>POST /api/v1/audit/redact</c>). Used by the patient purge worker
/// to scrub PII from prior audit entries before a patient is hard-deleted.
/// </summary>
public interface IAuditRedactorClient
{
    /// <summary>Redact all audit entries referring to the given entity. Returns the number of rows redacted.</summary>
    Task<int> RedactAsync(string entityType, Guid entityId, string placeholder, CancellationToken ct);
}

/// <inheritdoc />
public sealed class AuditRedactorClient : IAuditRedactorClient
{
    /// <summary>DI name for the typed HttpClient.</summary>
    public const string HttpClientName = "AuditRedactor";

    private readonly HttpClient _http;
    private readonly AuditClientOptions _options;
    private readonly ILogger<AuditRedactorClient> _logger;

    public AuditRedactorClient(HttpClient http, IOptions<AuditClientOptions> options, ILogger<AuditRedactorClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> RedactAsync(string entityType, Guid entityId, string placeholder, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/audit/redact")
        {
            Content = JsonContent.Create(new RedactBody
            {
                EntityType = entityType,
                EntityId = entityId,
                RedactionPlaceholder = placeholder,
            }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());
        request.Headers.TryAddWithoutValidation("X-Source-Service", _options.SourceService);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Audit redact returned {Status} for {EntityType}/{EntityId}: {Body}",
                (int)response.StatusCode, entityType, entityId, body);
            return 0;
        }

        var result = await response.Content.ReadFromJsonAsync<RedactResponse>(cancellationToken: ct);
        return result?.RowsRedacted ?? 0;
    }

    private sealed class RedactBody
    {
        [JsonPropertyName("entityType")] public string EntityType { get; set; } = string.Empty;
        [JsonPropertyName("entityId")] public Guid EntityId { get; set; }
        [JsonPropertyName("redactionPlaceholder")] public string RedactionPlaceholder { get; set; } = "[REDACTED]";
    }

    private sealed class RedactResponse
    {
        [JsonPropertyName("rowsRedacted")] public int RowsRedacted { get; set; }
    }
}
