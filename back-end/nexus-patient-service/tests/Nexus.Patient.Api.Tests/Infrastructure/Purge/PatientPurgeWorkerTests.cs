using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.Patients.Api.Configuration;
using Nexus.Patients.Api.Features.Patients.Models;
using Nexus.Patients.Api.Infrastructure.Audit;
using Nexus.Patients.Api.Infrastructure.Persistence;
using Nexus.Patients.Api.Infrastructure.Purge;
using Nexus.Patients.Api.Tests.Infrastructure;
using NSubstitute;

namespace Nexus.Patients.Api.Tests.Infrastructure.Purge;

public sealed class PatientPurgeWorkerTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Patient ArchivedPatient(DateTime archivedAtUtc, string publicCode = "PAT-2026-BLR-000001") => new()
    {
        Id = Guid.NewGuid(),
        PublicCode = publicCode,
        FirstName = "Ravi", LastName = "Kumar", FullName = "Ravi Kumar",
        Phone = $"+91-9{Random.Shared.Next(100000000, 999999999)}",
        Email = $"r{Guid.NewGuid():N}@x.com",
        DateOfBirth = new DateOnly(1990, 1, 1),
        Gender = "Male", PrimaryBranchId = Guid.NewGuid(),
        Status = PatientStatus.Archived,
        ArchivedAtUtc = archivedAtUtc,
        CreatedAtUtc = archivedAtUtc.AddDays(-100),
        UpdatedAtUtc = archivedAtUtc,
    };

    private static (PatientPurgeWorker worker, IAuditPublisher publisher, IAuditRedactorClient redactor, PatientDbContext db)
        Build(DateTimeOffset now, PurgeOptions options)
    {
        var db = InMemoryDb.Create();
        var publisher = Substitute.For<IAuditPublisher>();
        var redactor = Substitute.For<IAuditRedactorClient>();
        redactor.RedactAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(publisher);
        services.AddSingleton(redactor);
        var provider = services.BuildServiceProvider();

        var clock = new FixedClock(now);
        var worker = new PatientPurgeWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            clock,
            NullLogger<PatientPurgeWorker>.Instance);
        return (worker, publisher, redactor, db);
    }

    [Fact]
    public async Task RunOnceAsync_purges_archived_rows_past_retention()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var opts = new PurgeOptions { Enabled = true, RetentionDays = 30, BatchSize = 50 };
        var (worker, publisher, redactor, db) = Build(now, opts);

        var old = ArchivedPatient(now.UtcDateTime.AddDays(-45), "PAT-2026-BLR-000001");
        var young = ArchivedPatient(now.UtcDateTime.AddDays(-5), "PAT-2026-BLR-000002");
        db.Patients.AddRange(old, young);
        await db.SaveChangesAsync();

        var purged = await worker.RunOnceAsync(CancellationToken.None);

        purged.Should().Be(1);
        db.Patients.Should().HaveCount(1);
        db.Patients.Single().Id.Should().Be(young.Id);
        await redactor.Received().RedactAsync("Patient", old.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await publisher.Received().PublishAsync(
            Arg.Is<AuditEventDto>(e => e.Action == "Patient.Purged" && e.EntityId == old.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_is_noop_when_no_due_rows()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var opts = new PurgeOptions { Enabled = true, RetentionDays = 30 };
        var (worker, publisher, redactor, db) = Build(now, opts);
        db.Patients.Add(ArchivedPatient(now.UtcDateTime.AddDays(-5)));
        await db.SaveChangesAsync();

        var purged = await worker.RunOnceAsync(CancellationToken.None);

        purged.Should().Be(0);
        db.Patients.Should().HaveCount(1);
        await publisher.DidNotReceive().PublishAsync(Arg.Any<AuditEventDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_proceeds_when_redactor_fails()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var opts = new PurgeOptions { Enabled = true, RetentionDays = 30 };
        var (worker, publisher, redactor, db) = Build(now, opts);
        redactor.RedactAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new HttpRequestException("audit down"));

        var old = ArchivedPatient(now.UtcDateTime.AddDays(-100));
        db.Patients.Add(old);
        await db.SaveChangesAsync();

        var purged = await worker.RunOnceAsync(CancellationToken.None);

        purged.Should().Be(1);
        db.Patients.Should().BeEmpty();
        await publisher.Received().PublishAsync(
            Arg.Is<AuditEventDto>(e => e.Action == "Patient.Purged"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_respects_batch_size()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var opts = new PurgeOptions { Enabled = true, RetentionDays = 30, BatchSize = 2 };
        var (worker, _, _, db) = Build(now, opts);
        for (var i = 0; i < 5; i++)
            db.Patients.Add(ArchivedPatient(now.UtcDateTime.AddDays(-(50 + i)), $"PAT-2026-BLR-{i:000000}"));
        await db.SaveChangesAsync();

        var purged = await worker.RunOnceAsync(CancellationToken.None);

        purged.Should().Be(2);
        db.Patients.Should().HaveCount(3);
    }

    [Fact]
    public async Task RunOnceAsync_purges_branch_links_alongside_patient()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var opts = new PurgeOptions { Enabled = true, RetentionDays = 30 };
        var (worker, _, _, db) = Build(now, opts);
        var old = ArchivedPatient(now.UtcDateTime.AddDays(-60));
        db.Patients.Add(old);
        db.PatientBranchLinks.Add(new PatientBranchLink
        {
            PatientId = old.Id, BranchId = Guid.NewGuid(),
            IsPrimary = true, LinkedAtUtc = now.UtcDateTime.AddDays(-90),
        });
        await db.SaveChangesAsync();

        await worker.RunOnceAsync(CancellationToken.None);

        db.PatientBranchLinks.Should().BeEmpty();
    }
}
