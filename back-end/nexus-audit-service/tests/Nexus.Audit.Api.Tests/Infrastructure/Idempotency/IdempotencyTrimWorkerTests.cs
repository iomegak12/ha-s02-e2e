using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.Audit.Api.Configuration;
using Nexus.Audit.Api.Features.Audit.Models;
using Nexus.Audit.Api.Infrastructure.Idempotency;
using Nexus.Audit.Api.Infrastructure.Persistence;

namespace Nexus.Audit.Api.Tests.Infrastructure.Idempotency;

public sealed class IdempotencyTrimWorkerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 5, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TrimOnceAsync_removes_records_older_than_RetentionDays_and_leaves_others()
    {
        var dbName = $"audit-trim-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<AuditDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();

        await using (var seed = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(dbName).Options))
        {
            seed.IdempotencyRecords.Add(Record("nexus-patient", "k-old", FixedNow.AddDays(-10).UtcDateTime));
            seed.IdempotencyRecords.Add(Record("nexus-patient", "k-edge", FixedNow.AddDays(-7).AddSeconds(-1).UtcDateTime));
            seed.IdempotencyRecords.Add(Record("nexus-patient", "k-fresh", FixedNow.AddDays(-1).UtcDateTime));
            await seed.SaveChangesAsync();
        }

        var options = Options.Create(new IdempotencyOptions { RetentionDays = 7, MaxClockSkewMinutes = 5, TrimIntervalMinutes = 60 });
        var monitor = new StaticOptionsMonitor<IdempotencyOptions>(options.Value);
        var worker = new IdempotencyTrimWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            monitor,
            new FixedClock(FixedNow),
            NullLogger<IdempotencyTrimWorker>.Instance);

        var deleted = await worker.TrimOnceAsync(CancellationToken.None);

        deleted.Should().Be(2);
        await using var verify = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(dbName).Options);
        var remaining = await verify.IdempotencyRecords.Select(r => r.IdempotencyKey).ToListAsync();
        remaining.Should().BeEquivalentTo(["k-fresh"]);
    }

    [Fact]
    public async Task TrimOnceAsync_leaves_audit_entries_untouched()
    {
        var dbName = $"audit-trim-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<AuditDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();

        var entryId = Guid.NewGuid();
        await using (var seed = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(dbName).Options))
        {
            seed.AuditEntries.Add(new AuditEntry
            {
                Id = entryId,
                EntityType = "Patient",
                EntityId = Guid.NewGuid(),
                Action = AuditActions.Created,
                ActorId = Guid.NewGuid(),
                ActorUsername = "admin",
                OccurredAtUtc = FixedNow.AddDays(-100).UtcDateTime,
                ReceivedAtUtc = FixedNow.AddDays(-100).UtcDateTime,
                Summary = "ancient",
                SourceService = "nexus-patient",
                IdempotencyKey = "k-old",
            });
            seed.IdempotencyRecords.Add(Record("nexus-patient", "k-old", FixedNow.AddDays(-100).UtcDateTime, entryId));
            await seed.SaveChangesAsync();
        }

        var monitor = new StaticOptionsMonitor<IdempotencyOptions>(new IdempotencyOptions { RetentionDays = 7 });
        var worker = new IdempotencyTrimWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            monitor,
            new FixedClock(FixedNow),
            NullLogger<IdempotencyTrimWorker>.Instance);

        await worker.TrimOnceAsync(CancellationToken.None);

        await using var verify = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(dbName).Options);
        (await verify.AuditEntries.CountAsync()).Should().Be(1);
        (await verify.IdempotencyRecords.CountAsync()).Should().Be(0);
    }

    private static IdempotencyRecord Record(string src, string key, DateTime createdAtUtc, Guid? entryId = null) => new()
    {
        Id = Guid.NewGuid(),
        SourceService = src,
        IdempotencyKey = key,
        EntryId = entryId ?? Guid.NewGuid(),
        PayloadHash = new string('a', 64),
        CreatedAtUtc = createdAtUtc,
    };

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
