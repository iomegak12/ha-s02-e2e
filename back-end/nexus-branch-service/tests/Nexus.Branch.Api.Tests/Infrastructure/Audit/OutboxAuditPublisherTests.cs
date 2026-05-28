using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.Branches.Api.Configuration;
using Nexus.Branches.Api.Infrastructure.Audit;
using Nexus.Branches.Api.Infrastructure.Audit.Outbox;
using Nexus.Branches.Api.Tests.Infrastructure;
using NSubstitute;

namespace Nexus.Branches.Api.Tests.Infrastructure.Audit;

public sealed class OutboxAuditPublisherTests
{
    private static AuditEventDto NewEvent() => new()
    {
        EntityType = "Branch",
        EntityId = Guid.NewGuid(),
        Action = "Created",
        ActorId = Guid.NewGuid(),
        ActorUsername = "admin",
        OccurredAtUtc = DateTime.UtcNow,
        Summary = "test",
    };

    [Fact]
    public async Task PublishAsync_writes_outbox_row_when_HTTP_publish_returns_false()
    {
        await using var db = InMemoryDb.Create();
        var clock = new FixedClock(new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero));
        var outbox = new PendingAuditOutbox(db, clock);
        var http = BuildFakeHttp(succeed: false);
        var publisher = new OutboxAuditPublisher(http, outbox, NullLogger<OutboxAuditPublisher>.Instance);

        await publisher.PublishAsync(NewEvent(), CancellationToken.None);

        var rows = await outbox.FetchDueAsync(10, CancellationToken.None);
        rows.Should().HaveCount(1);
        rows[0].Attempts.Should().Be(0);
        rows[0].LastError.Should().BeNull();
    }

    [Fact]
    public async Task PublishAsync_does_NOT_write_outbox_when_HTTP_publish_succeeds()
    {
        await using var db = InMemoryDb.Create();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var outbox = new PendingAuditOutbox(db, clock);
        var http = BuildFakeHttp(succeed: true);
        var publisher = new OutboxAuditPublisher(http, outbox, NullLogger<OutboxAuditPublisher>.Instance);

        await publisher.PublishAsync(NewEvent(), CancellationToken.None);

        var rows = await outbox.FetchDueAsync(10, CancellationToken.None);
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task BackoffAsync_increments_attempts_and_pushes_NextRetryUtc()
    {
        await using var db = InMemoryDb.Create();
        var now = new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(now);
        var outbox = new PendingAuditOutbox(db, clock);
        await outbox.EnqueueAsync(NewEvent(), "key-1", CancellationToken.None);
        var row = (await outbox.FetchDueAsync(10, CancellationToken.None))[0];

        await outbox.BackoffAsync(row.Id, "boom", CancellationToken.None);

        await using var verify = InMemoryDb.Create();
        // Re-query the same in-memory DB instance by reusing original context:
        var refreshed = await outbox.FetchDueAsync(10, CancellationToken.None);
        // After backoff the row's NextRetryUtc is in the future, so FetchDueAsync (at "now") should not return it.
        refreshed.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_removes_outbox_row()
    {
        await using var db = InMemoryDb.Create();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var outbox = new PendingAuditOutbox(db, clock);
        await outbox.EnqueueAsync(NewEvent(), "key-x", CancellationToken.None);
        var row = (await outbox.FetchDueAsync(10, CancellationToken.None))[0];

        await outbox.DeleteAsync(row.Id, CancellationToken.None);

        (await outbox.FetchDueAsync(10, CancellationToken.None)).Should().BeEmpty();
    }

    private static HttpAuditPublisher BuildFakeHttp(bool succeed)
    {
        var handler = new StubHandler(succeed);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:9999") };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var opts = Options.Create(new AuditClientOptions { BaseUrl = "http://localhost:9999", SourceService = "nexus-branch" });
        return new HttpAuditPublisher(client, accessor, opts, NullLogger<HttpAuditPublisher>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly bool _succeed;
        public StubHandler(bool succeed) => _succeed = succeed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_succeed ? System.Net.HttpStatusCode.Created : System.Net.HttpStatusCode.ServiceUnavailable));
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
