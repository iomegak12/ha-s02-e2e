using Bogus;
using Nexus.Audit.Api.Features.Audit.Models;

namespace Nexus.Audit.Api.Tests.Fakers;

/// <summary>Bogus faker for fully-populated <see cref="AuditEntry"/> instances.</summary>
public static class AuditEntryFaker
{
    private static readonly string[] EntityTypes =
        ["Patient", "Doctor", "Branch", "Admin", "DoctorDocument"];

    /// <summary>Create a faker configured with realistic defaults.</summary>
    public static Faker<AuditEntry> Create(string? sourceService = null) =>
        new Faker<AuditEntry>()
            .RuleFor(e => e.Id, _ => Guid.NewGuid())
            .RuleFor(e => e.EntityType, f => f.PickRandom(EntityTypes))
            .RuleFor(e => e.EntityId, _ => Guid.NewGuid())
            .RuleFor(e => e.EntityCode, f => f.Random.Bool(0.3f) ? f.Random.AlphaNumeric(6).ToUpperInvariant() : null)
            .RuleFor(e => e.Action, f => f.PickRandom(AuditActions.All.ToArray()))
            .RuleFor(e => e.ActorId, _ => Guid.NewGuid())
            .RuleFor(e => e.ActorUsername, f => f.Internet.UserName())
            .RuleFor(e => e.OccurredAtUtc, f => f.Date.RecentOffset(7).UtcDateTime)
            .RuleFor(e => e.ReceivedAtUtc, (_, e) => e.OccurredAtUtc.AddSeconds(1))
            .RuleFor(e => e.Summary, f => f.Lorem.Sentence(8))
            .RuleFor(e => e.DiffJson, f => f.Random.Bool(0.2f) ? "{}" : null)
            .RuleFor(e => e.SourceService, _ => sourceService ?? "nexus-patient")
            .RuleFor(e => e.IdempotencyKey, _ => Guid.NewGuid().ToString());
}
