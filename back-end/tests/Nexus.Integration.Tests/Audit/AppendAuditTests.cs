using Nexus.IntegrationTests.Infrastructure;

namespace Nexus.IntegrationTests.Audit;

[Collection("stack")]
public sealed class AppendAuditTests
{
    private readonly StackFixture _fx;
    public AppendAuditTests(StackFixture fx) => _fx = fx;

    private HttpRequestMessage BuildAppend(object body, string idempotencyKey, string sourceService = "nexus-itest")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/audit/")
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.AdminAccessToken);
        req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        req.Headers.TryAddWithoutValidation("X-Source-Service", sourceService);
        return req;
    }

    [Fact]
    public async Task Happy_path_returns_201_with_Location_and_no_replay_header()
    {
        using var http = _fx.Client(_fx.Settings.AuditBaseUrl);
        var key = Guid.NewGuid().ToString();
        var builder = new AuditRequestBuilder();

        var response = await http.SendAsync(BuildAppend(builder.Build(), key));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Contains("X-Idempotent-Replay").Should().BeFalse();

        var entry = await response.Content.ReadFromJsonAsync<JsonElement>();
        entry.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
        entry.GetProperty("sourceService").GetString().Should().Be("nexus-itest");
        entry.GetProperty("idempotencyKey").GetString().Should().Be(key);
        entry.GetProperty("receivedAtUtc").GetDateTime().Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Replay_returns_200_with_X_Idempotent_Replay_true_and_same_id()
    {
        using var http = _fx.Client(_fx.Settings.AuditBaseUrl);
        var key = Guid.NewGuid().ToString();
        var body = new AuditRequestBuilder { Summary = "replay-test" }.Build();

        var first = await http.SendAsync(BuildAppend(body, key));
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstEntry = await first.Content.ReadFromJsonAsync<JsonElement>();
        var id = firstEntry.GetProperty("id").GetGuid();

        var second = await http.SendAsync(BuildAppend(body, key));

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Headers.GetValues("X-Idempotent-Replay").Single().Should().Be("true");
        var secondEntry = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondEntry.GetProperty("id").GetGuid().Should().Be(id);
    }

    [Fact]
    public async Task Same_key_different_payload_returns_409_IDEMPOTENCY_CONFLICT()
    {
        using var http = _fx.Client(_fx.Settings.AuditBaseUrl);
        var key = Guid.NewGuid().ToString();

        var first = await http.SendAsync(BuildAppend(new AuditRequestBuilder { Summary = "first" }.Build(), key));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await http.SendAsync(BuildAppend(new AuditRequestBuilder { Summary = "second" }.Build(), key));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("IDEMPOTENCY_CONFLICT");
    }

    [Fact]
    public async Task Missing_idempotency_key_returns_400_IDEMPOTENCY_KEY_MISSING()
    {
        using var http = _fx.Client(_fx.Settings.AuditBaseUrl);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/audit/")
        {
            Content = JsonContent.Create(new AuditRequestBuilder().Build()),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.AdminAccessToken);

        var response = await http.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("IDEMPOTENCY_KEY_MISSING");
    }

    [Fact]
    public async Task Future_occurredAt_returns_422_AUDIT_VALIDATION()
    {
        using var http = _fx.Client(_fx.Settings.AuditBaseUrl);
        var body = new AuditRequestBuilder { OccurredAtUtc = DateTime.UtcNow.AddDays(1) }.Build();

        var response = await http.SendAsync(BuildAppend(body, Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("AUDIT_VALIDATION");
    }

    [Fact]
    public async Task Unknown_action_returns_422_AUDIT_VALIDATION()
    {
        using var http = _fx.Client(_fx.Settings.AuditBaseUrl);
        var body = new AuditRequestBuilder { Action = "Frobnicated" }.Build();

        var response = await http.SendAsync(BuildAppend(body, Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Without_bearer_returns_401()
    {
        using var http = _fx.Client(_fx.Settings.AuditBaseUrl);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/audit/")
        {
            Content = JsonContent.Create(new AuditRequestBuilder().Build()),
        };
        req.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await http.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Source_service_is_lowercased_on_write()
    {
        using var http = _fx.Client(_fx.Settings.AuditBaseUrl);
        var key = Guid.NewGuid().ToString();
        var req = BuildAppend(new AuditRequestBuilder().Build(), key, sourceService: "NEXUS-MIXED-CASE");

        var response = await http.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("sourceService").GetString().Should().Be("nexus-mixed-case");
    }
}
