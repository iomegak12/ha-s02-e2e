using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Audit.Api.Features.Audit.Models;
using Nexus.Audit.Api.Features.Audit.Repository;
using Nexus.Audit.Api.Features.Audit.Service;
using Nexus.Audit.Api.Infrastructure.Errors;
using Nexus.Audit.Api.Infrastructure.Idempotency;
using Nexus.Audit.Api.Tests.Infrastructure;

namespace Nexus.Audit.Api.Tests.Features.Audit.Service;

public sealed class AuditServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 5, 28, 12, 0, 0, TimeSpan.Zero);

    private static AppendAuditEntryRequest SampleRequest(string summary = "Patient created via test") => new(
        EntityType: "Patient",
        EntityId: new Guid("11111111-1111-1111-1111-111111111111"),
        EntityCode: null,
        Action: AuditActions.Created,
        ActorId: new Guid("22222222-2222-2222-2222-222222222222"),
        ActorUsername: "admin",
        OccurredAtUtc: FixedNow.UtcDateTime.AddSeconds(-30),
        Summary: summary,
        DiffJson: null);

    private static AuditService BuildService(out AuditEntryRepository entries, out IdempotencyRepository idem)
    {
        var db = InMemoryDb.Create();
        entries = new AuditEntryRepository(db);
        idem = new IdempotencyRepository(db);
        var clock = new FixedClock(FixedNow);
        return new AuditService(entries, idem, clock, NullLogger<AuditService>.Instance);
    }

    [Fact]
    public async Task First_call_inserts_new_entry_with_IsReplay_false_and_server_stamped_fields()
    {
        var svc = BuildService(out _, out _);
        var idemCtx = new IdempotencyContext("key-1", "nexus-patient");

        var result = await svc.AppendAsync(SampleRequest(), idemCtx, CancellationToken.None);

        result.IsReplay.Should().BeFalse();
        result.Entry.Id.Should().NotBe(Guid.Empty);
        result.Entry.ReceivedAtUtc.Should().Be(FixedNow.UtcDateTime);
        result.Entry.SourceService.Should().Be("nexus-patient");
        result.Entry.IdempotencyKey.Should().Be("key-1");
    }

    [Fact]
    public async Task Identical_replay_returns_original_entry_with_IsReplay_true()
    {
        var svc = BuildService(out var entries, out _);
        var idemCtx = new IdempotencyContext("key-1", "nexus-patient");

        var first = await svc.AppendAsync(SampleRequest(), idemCtx, CancellationToken.None);
        var second = await svc.AppendAsync(SampleRequest(), idemCtx, CancellationToken.None);

        second.IsReplay.Should().BeTrue();
        second.Entry.Id.Should().Be(first.Entry.Id);
        (await entries.QueryAsync(new AuditQuery(), CancellationToken.None)).Total.Should().Be(1);
    }

    [Fact]
    public async Task Different_payload_with_same_key_throws_IDEMPOTENCY_CONFLICT()
    {
        var svc = BuildService(out _, out _);
        var idemCtx = new IdempotencyContext("key-1", "nexus-patient");

        await svc.AppendAsync(SampleRequest("first summary"), idemCtx, CancellationToken.None);

        var act = async () => await svc.AppendAsync(SampleRequest("second summary"), idemCtx, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.IdempotencyConflict);
        ex.Which.Status.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Same_key_under_different_source_services_does_not_collide()
    {
        var svc = BuildService(out _, out _);

        var a = await svc.AppendAsync(SampleRequest("a"), new IdempotencyContext("shared-key", "nexus-patient"), CancellationToken.None);
        var b = await svc.AppendAsync(SampleRequest("b"), new IdempotencyContext("shared-key", "nexus-doctor"), CancellationToken.None);

        a.IsReplay.Should().BeFalse();
        b.IsReplay.Should().BeFalse();
        a.Entry.Id.Should().NotBe(b.Entry.Id);
    }

    [Fact]
    public void Payload_hash_is_stable_for_identical_requests()
    {
        var h1 = AuditService.ComputePayloadHash(SampleRequest());
        var h2 = AuditService.ComputePayloadHash(SampleRequest());
        h1.Should().Be(h2);
        h1.Should().HaveLength(64);
    }

    [Fact]
    public void Payload_hash_changes_with_summary()
    {
        var h1 = AuditService.ComputePayloadHash(SampleRequest("aaa"));
        var h2 = AuditService.ComputePayloadHash(SampleRequest("bbb"));
        h1.Should().NotBe(h2);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
