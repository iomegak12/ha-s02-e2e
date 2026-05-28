using Nexus.IntegrationTests.Infrastructure;

namespace Nexus.IntegrationTests.Audit;

[Collection("stack")]
public sealed class HealthAndDocsTests
{
    private readonly StackFixture _fx;
    public HealthAndDocsTests(StackFixture fx) => _fx = fx;

    [Fact]
    public async Task Health_live_returns_200()
    {
        using var http = _fx.Client(_fx.Settings.AuditBaseUrl);
        var response = await http.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_ready_returns_200()
    {
        using var http = _fx.Client(_fx.Settings.AuditBaseUrl);
        var response = await http.GetAsync("/health/ready");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Swagger_json_exposes_all_three_audit_operationIds()
    {
        using var http = _fx.Client(_fx.Settings.AuditBaseUrl);

        var response = await http.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        var operationIds = doc.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject())
            .Where(m => m.Value.TryGetProperty("operationId", out _))
            .Select(m => m.Value.GetProperty("operationId").GetString())
            .ToList();

        operationIds.Should().Contain("appendAuditEntry");
        operationIds.Should().Contain("queryAuditEntries");
        operationIds.Should().Contain("getAuditEntryById");
    }

    [Fact]
    public async Task Metrics_endpoint_exposes_custom_audit_counters_after_an_append()
    {
        using var http = _fx.Client(_fx.Settings.AuditBaseUrl);

        // Push one append so the counters get exported.
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/audit/")
        {
            Content = JsonContent.Create(new AuditRequestBuilder().Build()),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.AdminAccessToken);
        req.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        req.Headers.TryAddWithoutValidation("X-Source-Service", "nexus-itest");
        var append = await http.SendAsync(req);
        append.StatusCode.Should().Be(HttpStatusCode.Created);

        // Prometheus scrape endpoint is unauthenticated.
        var metricsResp = await http.GetAsync("/metrics");
        metricsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await metricsResp.Content.ReadAsStringAsync();
        body.Should().Contain("nexus_audit_entries_appended_total");
    }
}
