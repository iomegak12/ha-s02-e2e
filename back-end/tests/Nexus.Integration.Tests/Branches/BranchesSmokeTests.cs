using Nexus.IntegrationTests.Infrastructure;

namespace Nexus.IntegrationTests.Branches;

[Collection("stack")]
public sealed class BranchesSmokeTests
{
    private readonly StackFixture _fx;
    public BranchesSmokeTests(StackFixture fx) => _fx = fx;

    [Fact]
    public async Task Health_live_returns_200()
    {
        using var http = _fx.Client(_fx.Settings.BranchesBaseUrl);
        var response = await http.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Swagger_exposes_all_5_branches_operationIds()
    {
        using var http = _fx.Client(_fx.Settings.BranchesBaseUrl);
        var doc = await http.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");

        var operationIds = doc.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject())
            .Where(m => m.Value.TryGetProperty("operationId", out _))
            .Select(m => m.Value.GetProperty("operationId").GetString())
            .ToList();

        operationIds.Should().Contain(new[] { "listBranches", "createBranch", "getBranchById", "patchBranch", "deactivateBranch" });
    }

    [Fact]
    public async Task List_branches_requires_bearer_token()
    {
        using var http = _fx.Client(_fx.Settings.BranchesBaseUrl);
        var response = await http.GetAsync("/api/v1/branches");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_can_list_branches()
    {
        using var http = _fx.AuthClient(_fx.Settings.BranchesBaseUrl);
        var response = await http.GetAsync("/api/v1/branches?page=1&size=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
    }
}
