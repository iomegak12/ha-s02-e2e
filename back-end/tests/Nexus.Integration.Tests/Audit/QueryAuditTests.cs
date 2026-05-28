using Nexus.IntegrationTests.Infrastructure;

namespace Nexus.IntegrationTests.Audit;

[Collection("stack")]
public sealed class QueryAuditTests
{
    private readonly StackFixture _fx;
    public QueryAuditTests(StackFixture fx) => _fx = fx;

    private async Task<Guid> AppendOneAsync(HttpClient http, AuditRequestBuilder builder, string sourceService)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/audit/")
        {
            Content = JsonContent.Create(builder.Build()),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.AdminAccessToken);
        req.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        req.Headers.TryAddWithoutValidation("X-Source-Service", sourceService);

        var response = await http.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var entry = await response.Content.ReadFromJsonAsync<JsonElement>();
        return entry.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task GetById_returns_200_for_an_appended_entry()
    {
        using var http = _fx.Client(_fx.Settings.AuditBaseUrl);
        var id = await AppendOneAsync(http, new AuditRequestBuilder { Summary = "get-by-id-test" }, "nexus-itest");

        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/audit/{id}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.AdminAccessToken);
        var response = await http.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(id);
        body.GetProperty("summary").GetString().Should().Be("get-by-id-test");
    }

    [Fact]
    public async Task GetById_unknown_returns_404_NOT_FOUND()
    {
        using var http = _fx.AuthClient(_fx.Settings.AuditBaseUrl);

        var response = await http.GetAsync($"/api/v1/audit/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Query_filters_by_source_service()
    {
        using var http = _fx.Client(_fx.Settings.AuditBaseUrl);
        var marker = $"itest-{Guid.NewGuid():N}".Substring(0, 24);

        await AppendOneAsync(http, new AuditRequestBuilder { Summary = $"src-test-{marker}" }, marker);
        await AppendOneAsync(http, new AuditRequestBuilder { Summary = "noise" }, "nexus-noise");

        using var auth = _fx.AuthClient(_fx.Settings.AuditBaseUrl);
        var response = await auth.GetAsync($"/api/v1/audit/?sourceService={marker}&size=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<JsonElement>();
        paged.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("sourceService").GetString())
            .Should().OnlyContain(s => s == marker);
    }

    [Fact]
    public async Task Query_filters_by_entityType_and_action()
    {
        using var http = _fx.Client(_fx.Settings.AuditBaseUrl);
        var entityId = Guid.NewGuid();

        await AppendOneAsync(http, new AuditRequestBuilder
        {
            EntityType = "Doctor",
            EntityId = entityId,
            Action = "Reviewed",
            Summary = "doctor-reviewed-marker",
        }, "nexus-itest");

        using var auth = _fx.AuthClient(_fx.Settings.AuditBaseUrl);
        var response = await auth.GetAsync($"/api/v1/audit/?entityType=Doctor&action=Reviewed&entityId={entityId}&size=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = paged.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        items.EnumerateArray().Should().OnlyContain(i =>
            i.GetProperty("entityType").GetString() == "Doctor" &&
            i.GetProperty("action").GetString() == "Reviewed");
    }

    [Fact]
    public async Task Query_respects_page_and_size()
    {
        using var http = _fx.AuthClient(_fx.Settings.AuditBaseUrl);

        var response = await http.GetAsync("/api/v1/audit/?page=1&size=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<JsonElement>();
        paged.GetProperty("page").GetInt32().Should().Be(1);
        paged.GetProperty("size").GetInt32().Should().Be(1);
        paged.GetProperty("items").GetArrayLength().Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Query_without_bearer_returns_401()
    {
        using var http = _fx.Client(_fx.Settings.AuditBaseUrl);

        var response = await http.GetAsync("/api/v1/audit");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
