using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Identity.Api.Infrastructure.Audit;

namespace Nexus.Identity.Api.Tests.Infrastructure.Audit;

public class HttpAuditPublisherTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return Responder?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("network down");
    }

    private static (HttpAuditPublisher Sut, CapturingHandler Handler) NewSut(string? bearer = null)
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://audit.local") };
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        if (bearer is not null)
        {
            accessor.HttpContext!.Request.Headers.Authorization = "Bearer " + bearer;
        }
        var sut = new HttpAuditPublisher(client, accessor, NullLogger<HttpAuditPublisher>.Instance);
        return (sut, handler);
    }

    private static AuditEntryDto NewEntry() => new()
    {
        EntityType = "Admin",
        EntityId = Guid.NewGuid().ToString(),
        EntityCode = "asha.menon",
        Action = "Created",
        ActorId = Guid.NewGuid().ToString(),
        ActorUsername = "system",
        OccurredAtUtc = DateTime.UtcNow,
        Summary = "Admin 'asha.menon' created.",
    };

    [Fact]
    public async Task PublishAsync_posts_to_api_v1_audit()
    {
        var (sut, handler) = NewSut();
        await sut.PublishAsync(NewEntry(), CancellationToken.None);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/api/v1/audit");
    }

    [Fact]
    public async Task PublishAsync_sets_uuid_v7_idempotency_key()
    {
        var (sut, handler) = NewSut();
        await sut.PublishAsync(NewEntry(), CancellationToken.None);

        handler.LastRequest!.Headers.TryGetValues("Idempotency-Key", out var values).Should().BeTrue();
        var raw = values!.Single();
        Guid.TryParse(raw, out var parsed).Should().BeTrue();
        // UUIDv7: version nibble is 7 (the 13th hex char of dashed form).
        raw[14].Should().Be('7');
    }

    [Fact]
    public async Task PublishAsync_forwards_caller_bearer_token()
    {
        var (sut, handler) = NewSut(bearer: "abc.def.ghi");
        await sut.PublishAsync(NewEntry(), CancellationToken.None);

        var auth = handler.LastRequest!.Headers.Authorization;
        auth.Should().NotBeNull();
        auth!.Scheme.Should().Be("Bearer");
        auth.Parameter.Should().Be("abc.def.ghi");
    }

    [Fact]
    public async Task PublishAsync_swallows_non_success_status()
    {
        var (sut, handler) = NewSut();
        handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var act = () => sut.PublishAsync(NewEntry(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_swallows_HttpRequestException()
    {
        var client = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://audit.local") };
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var sut = new HttpAuditPublisher(client, accessor, NullLogger<HttpAuditPublisher>.Instance);

        var act = () => sut.PublishAsync(NewEntry(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_swallows_when_cancelled()
    {
        var (sut, _) = NewSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => sut.PublishAsync(NewEntry(), cts.Token);

        await act.Should().NotThrowAsync();
    }
}
