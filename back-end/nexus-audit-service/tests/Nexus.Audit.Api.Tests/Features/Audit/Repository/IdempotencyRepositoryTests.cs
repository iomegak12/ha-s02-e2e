using Nexus.Audit.Api.Features.Audit.Models;
using Nexus.Audit.Api.Features.Audit.Repository;
using Nexus.Audit.Api.Tests.Infrastructure;

namespace Nexus.Audit.Api.Tests.Features.Audit.Repository;

public sealed class IdempotencyRepositoryTests
{
    [Fact]
    public async Task FindAsync_returns_null_when_unknown()
    {
        await using var db = InMemoryDb.Create();
        var repo = new IdempotencyRepository(db);

        var hit = await repo.FindAsync("nexus-patient", "key-1", CancellationToken.None);

        hit.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_then_FindAsync_round_trips_record()
    {
        await using var db = InMemoryDb.Create();
        var repo = new IdempotencyRepository(db);
        var record = new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            SourceService = "nexus-patient",
            IdempotencyKey = "key-1",
            EntryId = Guid.NewGuid(),
            PayloadHash = new string('a', 64),
            CreatedAtUtc = DateTime.UtcNow,
        };

        await repo.AddAsync(record, CancellationToken.None);
        var loaded = await repo.FindAsync("nexus-patient", "key-1", CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.EntryId.Should().Be(record.EntryId);
        loaded.PayloadHash.Should().Be(record.PayloadHash);
    }

    [Fact]
    public async Task FindAsync_lowercases_source_service_lookup()
    {
        await using var db = InMemoryDb.Create();
        var repo = new IdempotencyRepository(db);
        await repo.AddAsync(new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            SourceService = "nexus-patient",
            IdempotencyKey = "key-2",
            EntryId = Guid.NewGuid(),
            PayloadHash = new string('b', 64),
            CreatedAtUtc = DateTime.UtcNow,
        }, CancellationToken.None);

        var loaded = await repo.FindAsync("NEXUS-PATIENT", "key-2", CancellationToken.None);

        loaded.Should().NotBeNull();
    }

    [Fact]
    public async Task FindAsync_scopes_by_source_service()
    {
        await using var db = InMemoryDb.Create();
        var repo = new IdempotencyRepository(db);
        await repo.AddAsync(new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            SourceService = "nexus-patient",
            IdempotencyKey = "shared-key",
            EntryId = Guid.NewGuid(),
            PayloadHash = new string('c', 64),
            CreatedAtUtc = DateTime.UtcNow,
        }, CancellationToken.None);

        var otherService = await repo.FindAsync("nexus-doctor", "shared-key", CancellationToken.None);

        otherService.Should().BeNull();
    }
}
