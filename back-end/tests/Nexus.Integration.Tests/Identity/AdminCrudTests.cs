using Nexus.IntegrationTests.Infrastructure;

namespace Nexus.IntegrationTests.Identity;

[Collection("stack")]
public sealed class AdminCrudTests
{
    private readonly StackFixture _fx;
    public AdminCrudTests(StackFixture fx) => _fx = fx;

    private static string UniqueUsername() =>
        $"itest_{Guid.NewGuid():N}".Substring(0, 24);

    [Fact]
    public async Task List_admins_returns_200_and_seeded_admin_appears()
    {
        using var http = _fx.AuthClient(_fx.Settings.IdentityBaseUrl);

        var response = await http.GetAsync("/api/v1/admins/?size=100");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items");
        items.EnumerateArray()
            .Select(a => a.GetProperty("username").GetString())
            .Should().Contain(_fx.Settings.AdminUsername);
    }

    [Fact]
    public async Task List_without_bearer_returns_401()
    {
        using var http = _fx.Client(_fx.Settings.IdentityBaseUrl);

        var response = await http.GetAsync("/api/v1/admins");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_returns_201_then_GetById_returns_200_and_PATCH_updates()
    {
        using var http = _fx.AuthClient(_fx.Settings.IdentityBaseUrl);
        var username = UniqueUsername();

        var createResp = await http.PostAsJsonAsync("/api/v1/admins/", new
        {
            username,
            displayName = "Integration Test User",
            password = "Pw1!secret-long-enough",
            isActive = true,
        });

        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        createResp.Headers.Location.Should().NotBeNull();
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var getResp = await http.GetAsync($"/api/v1/admins/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        getResp.Headers.ETag.Should().NotBeNull();
        var etag = getResp.Headers.ETag!.Tag;
        var fetched = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        fetched.GetProperty("username").GetString().Should().Be(username);
        fetched.GetProperty("displayName").GetString().Should().Be("Integration Test User");

        var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/admins/{id}")
        {
            Content = JsonContent.Create(new { displayName = "Renamed", isActive = false }),
        };
        patchReq.Headers.TryAddWithoutValidation("If-Match", etag);
        patchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.AdminAccessToken);
        var patchResp = await http.SendAsync(patchReq);
        patchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var patched = await patchResp.Content.ReadFromJsonAsync<JsonElement>();
        patched.GetProperty("displayName").GetString().Should().Be("Renamed");
        patched.GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Create_with_duplicate_username_returns_409()
    {
        using var http = _fx.AuthClient(_fx.Settings.IdentityBaseUrl);
        var username = UniqueUsername();
        var body = new
        {
            username,
            displayName = "Dup",
            password = "Pw1!secret-long-enough",
            isActive = true,
        };

        var first = await http.PostAsJsonAsync("/api/v1/admins/", body);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await http.PostAsJsonAsync("/api/v1/admins/", body);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_with_invalid_body_returns_422()
    {
        using var http = _fx.AuthClient(_fx.Settings.IdentityBaseUrl);

        var response = await http.PostAsJsonAsync("/api/v1/admins/", new
        {
            username = "",
            displayName = "",
            password = "x",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task GetById_unknown_returns_404()
    {
        using var http = _fx.AuthClient(_fx.Settings.IdentityBaseUrl);

        var response = await http.GetAsync($"/api/v1/admins/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_supports_size_paging()
    {
        using var http = _fx.AuthClient(_fx.Settings.IdentityBaseUrl);

        var response = await http.GetAsync("/api/v1/admins/?page=1&size=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("size").GetInt32().Should().Be(1);
        body.GetProperty("items").GetArrayLength().Should().BeLessThanOrEqualTo(1);
    }
}
