using Nexus.IntegrationTests.Audit;
using Nexus.IntegrationTests.Infrastructure;

namespace Nexus.IntegrationTests.CrossService;

/// <summary>
/// End-to-end proof that the Audit service validates tokens issued by Identity
/// via the live JWKS endpoint. A fresh token is acquired per test (no fixture
/// reuse) so the JWKS code path is exercised against a real round-trip.
/// </summary>
[Collection("stack")]
public sealed class IdentityToAuditFlowTests
{
    private readonly StackFixture _fx;
    public IdentityToAuditFlowTests(StackFixture fx) => _fx = fx;

    private async Task<string> AcquireFreshTokenAsync()
    {
        using var identity = _fx.Client(_fx.Settings.IdentityBaseUrl);
        var response = await identity.PostAsJsonAsync("/api/v1/auth/token", new
        {
            username = _fx.Settings.AdminUsername,
            password = _fx.Settings.AdminPassword,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task Token_from_Identity_authorizes_an_append_against_Audit()
    {
        var access = await AcquireFreshTokenAsync();
        using var audit = _fx.Client(_fx.Settings.AuditBaseUrl);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/audit/")
        {
            Content = JsonContent.Create(new AuditRequestBuilder { Summary = "cross-service flow test" }.Build()),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        req.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        req.Headers.TryAddWithoutValidation("X-Source-Service", "nexus-itest-flow");

        var response = await audit.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var entry = await response.Content.ReadFromJsonAsync<JsonElement>();
        entry.GetProperty("actorUsername").GetString().Should().NotBeNullOrEmpty();
        entry.GetProperty("sourceService").GetString().Should().Be("nexus-itest-flow");
    }

    [Fact]
    public async Task Tampered_token_is_rejected_with_401_by_Audit()
    {
        var access = await AcquireFreshTokenAsync();
        // Flip the last character to break the signature.
        var tampered = access[..^1] + (access[^1] == 'A' ? 'B' : 'A');
        using var audit = _fx.Client(_fx.Settings.AuditBaseUrl);

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/audit");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tampered);

        var response = await audit.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Append_via_Identity_token_then_query_round_trips_through_Audit()
    {
        var access = await AcquireFreshTokenAsync();
        using var audit = _fx.Client(_fx.Settings.AuditBaseUrl);
        var marker = $"flow-{Guid.NewGuid():N}".Substring(0, 24);

        // Append
        var appendReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/audit/")
        {
            Content = JsonContent.Create(new AuditRequestBuilder { Summary = marker }.Build()),
        };
        appendReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        appendReq.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        appendReq.Headers.TryAddWithoutValidation("X-Source-Service", "nexus-itest-flow");
        var appendResp = await audit.SendAsync(appendReq);
        appendResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await appendResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        // Read back
        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/audit/{id}");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var getResp = await audit.SendAsync(getReq);
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        fetched.GetProperty("summary").GetString().Should().Be(marker);
        fetched.GetProperty("sourceService").GetString().Should().Be("nexus-itest-flow");
    }
}
