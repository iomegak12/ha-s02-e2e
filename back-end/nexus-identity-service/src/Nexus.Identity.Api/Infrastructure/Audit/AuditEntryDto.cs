using System.Text.Json.Serialization;

namespace Nexus.Identity.Api.Infrastructure.Audit;

/// <summary>
/// Wire shape POSTed to <c>/api/v1/audit</c> on the Audit service.
/// Matches §14 of <c>LLD_Nexus_HA.md</c>.
/// </summary>
public sealed class AuditEntryDto
{
    /// <summary>Entity type, e.g. <c>Admin</c>.</summary>
    [JsonPropertyName("entityType")]
    public string EntityType { get; init; } = string.Empty;

    /// <summary>Entity id (GUID string).</summary>
    [JsonPropertyName("entityId")]
    public string EntityId { get; init; } = string.Empty;

    /// <summary>Optional short stable code for the entity (e.g. username).</summary>
    [JsonPropertyName("entityCode")]
    public string? EntityCode { get; init; }

    /// <summary>Action keyword: <c>Created</c>, <c>Updated</c>, <c>StatusChanged</c>, <c>Purged</c>.</summary>
    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    /// <summary>Actor id (GUID string of the acting admin).</summary>
    [JsonPropertyName("actorId")]
    public string ActorId { get; init; } = string.Empty;

    /// <summary>Actor username at time of action.</summary>
    [JsonPropertyName("actorUsername")]
    public string ActorUsername { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the audited action occurred.</summary>
    [JsonPropertyName("occurredAtUtc")]
    public DateTime OccurredAtUtc { get; init; }

    /// <summary>Short human-readable summary line.</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    /// <summary>Optional structured diff describing what changed.</summary>
    [JsonPropertyName("diff")]
    public object? Diff { get; init; }
}
