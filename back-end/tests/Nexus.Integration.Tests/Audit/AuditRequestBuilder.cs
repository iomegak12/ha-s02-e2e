namespace Nexus.IntegrationTests.Audit;

/// <summary>
/// Helper that builds canonical <c>appendAuditEntry</c> payloads. Tests mutate
/// individual properties as needed and call <see cref="Build"/>.
/// </summary>
public sealed class AuditRequestBuilder
{
    public string EntityType { get; set; } = "Patient";
    public Guid EntityId { get; set; } = Guid.NewGuid();
    public string? EntityCode { get; set; }
    public string Action { get; set; } = "Created";
    public Guid ActorId { get; set; } = Guid.NewGuid();
    public string ActorUsername { get; set; } = "admin";
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow.AddSeconds(-2);
    public string Summary { get; set; } = "Integration test entry";
    public string? DiffJson { get; set; }

    public object Build() => new
    {
        entityType = EntityType,
        entityId = EntityId,
        entityCode = EntityCode,
        action = Action,
        actorId = ActorId,
        actorUsername = ActorUsername,
        occurredAtUtc = OccurredAtUtc,
        summary = Summary,
        diffJson = DiffJson,
    };
}
