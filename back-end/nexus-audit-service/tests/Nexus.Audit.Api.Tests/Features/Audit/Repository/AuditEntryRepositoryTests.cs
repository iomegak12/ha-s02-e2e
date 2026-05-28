using Nexus.Audit.Api.Features.Audit.Models;
using Nexus.Audit.Api.Features.Audit.Repository;
using Nexus.Audit.Api.Tests.Fakers;
using Nexus.Audit.Api.Tests.Infrastructure;

namespace Nexus.Audit.Api.Tests.Features.Audit.Repository;

public sealed class AuditEntryRepositoryTests
{
    [Fact]
    public async Task AddAsync_then_GetByIdAsync_round_trips_entry()
    {
        await using var db = InMemoryDb.Create();
        var repo = new AuditEntryRepository(db);
        var entry = AuditEntryFaker.Create().Generate();

        await repo.AddAsync(entry, CancellationToken.None);
        var loaded = await repo.GetByIdAsync(entry.Id, CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.EntityType.Should().Be(entry.EntityType);
        loaded.IdempotencyKey.Should().Be(entry.IdempotencyKey);
        loaded.SourceService.Should().Be(entry.SourceService);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_unknown_id()
    {
        await using var db = InMemoryDb.Create();
        var repo = new AuditEntryRepository(db);

        var loaded = await repo.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task RedactByEntityAsync_replaces_summary_and_nulls_diff_for_matching_rows()
    {
        await using var db = InMemoryDb.Create();
        var repo = new AuditEntryRepository(db);
        var faker = AuditEntryFaker.Create();
        var patientId = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
        {
            var e = faker.Generate();
            e.EntityType = "Patient";
            e.EntityId = patientId;
            e.Summary = $"patient ravi kumar action #{i}";
            e.DiffJson = "{\"phone\":\"+91-1234567890\"}";
            await repo.AddAsync(e, CancellationToken.None);
        }
        var other = faker.Generate();
        other.EntityType = "Patient";
        other.EntityId = Guid.NewGuid();
        other.Summary = "other patient";
        await repo.AddAsync(other, CancellationToken.None);

        var redacted = await repo.RedactByEntityAsync("Patient", patientId, "[REDACTED]", CancellationToken.None);

        redacted.Should().Be(3);
        var rows = await repo.QueryAsync(new AuditQuery(EntityType: "Patient", EntityId: patientId, Page: 1, Size: 10), CancellationToken.None);
        rows.Items.Should().OnlyContain(r => r.Summary == "[REDACTED]");
        var stillFine = await repo.GetByIdAsync(other.Id, CancellationToken.None);
        stillFine!.Summary.Should().Be("other patient");
    }

    [Fact]
    public async Task QueryAsync_filters_by_entity_type_and_paginates_desc()
    {
        await using var db = InMemoryDb.Create();
        var repo = new AuditEntryRepository(db);
        var faker = AuditEntryFaker.Create();

        for (var i = 0; i < 5; i++)
        {
            var e = faker.Generate();
            e.EntityType = "Patient";
            e.OccurredAtUtc = DateTime.UtcNow.AddMinutes(-i);
            await repo.AddAsync(e, CancellationToken.None);
        }
        for (var i = 0; i < 3; i++)
        {
            var e = faker.Generate();
            e.EntityType = "Doctor";
            await repo.AddAsync(e, CancellationToken.None);
        }

        var page = await repo.QueryAsync(new AuditQuery(EntityType: "Patient", Page: 1, Size: 2), CancellationToken.None);

        page.Total.Should().Be(5);
        page.Items.Should().HaveCount(2);
        page.Items.Select(i => i.OccurredAtUtc).Should().BeInDescendingOrder();
        page.Items.Should().OnlyContain(i => i.EntityType == "Patient");
    }

    [Fact]
    public async Task QueryAsync_filters_by_actor_and_date_range_and_source_service()
    {
        await using var db = InMemoryDb.Create();
        var repo = new AuditEntryRepository(db);
        var faker = AuditEntryFaker.Create();
        var actorId = Guid.NewGuid();
        var inRange = DateTime.UtcNow.AddHours(-2);
        var tooOld = DateTime.UtcNow.AddDays(-10);

        var hit = faker.Generate();
        hit.ActorId = actorId;
        hit.SourceService = "nexus-patient";
        hit.OccurredAtUtc = inRange;
        await repo.AddAsync(hit, CancellationToken.None);

        var wrongActor = faker.Generate();
        wrongActor.SourceService = "nexus-patient";
        wrongActor.OccurredAtUtc = inRange;
        await repo.AddAsync(wrongActor, CancellationToken.None);

        var wrongDate = faker.Generate();
        wrongDate.ActorId = actorId;
        wrongDate.SourceService = "nexus-patient";
        wrongDate.OccurredAtUtc = tooOld;
        await repo.AddAsync(wrongDate, CancellationToken.None);

        var wrongService = faker.Generate();
        wrongService.ActorId = actorId;
        wrongService.SourceService = "nexus-doctor";
        wrongService.OccurredAtUtc = inRange;
        await repo.AddAsync(wrongService, CancellationToken.None);

        var page = await repo.QueryAsync(
            new AuditQuery(
                ActorId: actorId,
                From: DateTime.UtcNow.AddHours(-3),
                To: DateTime.UtcNow,
                SourceService: "NEXUS-PATIENT"),
            CancellationToken.None);

        page.Total.Should().Be(1);
        page.Items.Single().Id.Should().Be(hit.Id);
    }
}
