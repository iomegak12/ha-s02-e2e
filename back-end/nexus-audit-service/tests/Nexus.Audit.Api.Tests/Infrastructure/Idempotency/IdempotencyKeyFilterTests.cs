using Microsoft.AspNetCore.Http;
using Nexus.Audit.Api.Infrastructure.Errors;
using Nexus.Audit.Api.Infrastructure.Idempotency;

namespace Nexus.Audit.Api.Tests.Infrastructure.Idempotency;

public sealed class IdempotencyKeyFilterTests
{
    [Fact]
    public async Task Missing_header_throws_DomainException_400_IDEMPOTENCY_KEY_MISSING()
    {
        var filter = new IdempotencyKeyFilter();
        var ctx = new TestEndpointFilterInvocationContext(new DefaultHttpContext());

        var act = async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.IdempotencyKeyMissing);
        ex.Which.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Empty_header_throws_400()
    {
        var filter = new IdempotencyKeyFilter();
        var http = new DefaultHttpContext();
        http.Request.Headers[IdempotencyKeyFilter.HeaderName] = "";
        var ctx = new TestEndpointFilterInvocationContext(http);

        var act = async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.Code == ErrorCodes.IdempotencyKeyMissing);
    }

    [Fact]
    public async Task Header_too_long_throws_400()
    {
        var filter = new IdempotencyKeyFilter();
        var http = new DefaultHttpContext();
        http.Request.Headers[IdempotencyKeyFilter.HeaderName] = new string('k', IdempotencyKeyFilter.MaxKeyLength + 1);
        var ctx = new TestEndpointFilterInvocationContext(http);

        var act = async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.Code == ErrorCodes.IdempotencyKeyMissing);
    }

    [Fact]
    public async Task Valid_header_stores_IdempotencyContext_with_source_from_X_Source_Service()
    {
        var filter = new IdempotencyKeyFilter();
        var http = new DefaultHttpContext();
        http.Request.Headers[IdempotencyKeyFilter.HeaderName] = "abc-123";
        http.Request.Headers[SourceServiceResolver.HeaderName] = "Nexus-Patient";
        var ctx = new TestEndpointFilterInvocationContext(http);

        var nextCalled = false;
        await filter.InvokeAsync(ctx, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(null);
        });

        nextCalled.Should().BeTrue();
        var resolved = http.Items[IdempotencyContext.HttpItemsKey] as IdempotencyContext;
        resolved.Should().NotBeNull();
        resolved!.IdempotencyKey.Should().Be("abc-123");
        resolved.SourceService.Should().Be("nexus-patient");
    }

    [Fact]
    public async Task Missing_source_service_header_falls_back_to_unknown()
    {
        var filter = new IdempotencyKeyFilter();
        var http = new DefaultHttpContext();
        http.Request.Headers[IdempotencyKeyFilter.HeaderName] = "abc-123";
        var ctx = new TestEndpointFilterInvocationContext(http);

        await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));

        var resolved = (IdempotencyContext)http.Items[IdempotencyContext.HttpItemsKey]!;
        resolved.SourceService.Should().Be(SourceServiceResolver.Unknown);
    }

    private sealed class TestEndpointFilterInvocationContext : EndpointFilterInvocationContext
    {
        public TestEndpointFilterInvocationContext(HttpContext http)
        {
            HttpContext = http;
            Arguments = Array.Empty<object?>();
        }

        public override HttpContext HttpContext { get; }
        public override IList<object?> Arguments { get; }
        public override T GetArgument<T>(int index) => (T)Arguments[index]!;
    }
}
