using System.Text.Json.Serialization;

namespace Nexus.Patients.Api.Infrastructure.Audit;

/// <summary>
/// Wire shape POSTed to <c>nexus-audit-service</c> at <c>/api/v1/audit</c>.
/// Matches the spec at <c>specs/audit.openapi.json</c> (<c>appendAuditEntry</c>).
/// </summary>
public sealed class AuditEventDto
{
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = string.Empty;
    [JsonPropertyName("entityId")] public Guid EntityId { get; init; }
    [JsonPropertyName("entityCode")] public string? EntityCode { get; init; }
    [JsonPropertyName("action")] public string Action { get; init; } = string.Empty;
    [JsonPropertyName("actorId")] public Guid ActorId { get; init; }
    [JsonPropertyName("actorUsername")] public string ActorUsername { get; init; } = string.Empty;
    [JsonPropertyName("occurredAtUtc")] public DateTime OccurredAtUtc { get; init; }
    [JsonPropertyName("summary")] public string Summary { get; init; } = string.Empty;
    [JsonPropertyName("diffJson")] public string? DiffJson { get; init; }
}
