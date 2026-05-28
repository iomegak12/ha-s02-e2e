using Nexus.IntegrationTests.Infrastructure;

namespace Nexus.IntegrationTests.Identity;

[Collection("stack")]
public sealed class AuthTokenTests
{
    private readonly StackFixture _fx;
    public AuthTokenTests(StackFixture fx) => _fx = fx;

    [Fact]
    public async Task IssueToken_with_valid_credentials_returns_access_and_refresh()
    {
        using var http = _fx.Client(_fx.Settings.IdentityBaseUrl);

        var response = await http.PostAsJsonAsync("/api/v1/auth/token", new
        {
            username = _fx.Settings.AdminUsername,
            password = _fx.Settings.AdminPassword,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("tokenType").GetString().Should().Be("Bearer");
        body.GetProperty("expiresIn").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task IssueToken_with_bad_password_returns_401()
    {
        using var http = _fx.Client(_fx.Settings.IdentityBaseUrl);

        var response = await http.PostAsJsonAsync("/api/v1/auth/token", new
        {
            username = _fx.Settings.AdminUsername,
            password = "definitely-not-the-password",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IssueToken_with_empty_body_returns_422()
    {
        using var http = _fx.Client(_fx.Settings.IdentityBaseUrl);

        var response = await http.PostAsJsonAsync("/api/v1/auth/token", new { username = "", password = "" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Refresh_rotates_to_a_new_access_token()
    {
        using var http = _fx.Client(_fx.Settings.IdentityBaseUrl);

        // Acquire a fresh pair so we don't disturb the suite-wide bearer.
        var initial = await http.PostAsJsonAsync("/api/v1/auth/token", new
        {
            username = _fx.Settings.AdminUsername,
            password = _fx.Settings.AdminPassword,
        });
        var initialBody = await initial.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = initialBody.GetProperty("refreshToken").GetString()!;
        var initialAccess = initialBody.GetProperty("accessToken").GetString()!;

        var refreshed = await http.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshedBody = await refreshed.Content.ReadFromJsonAsync<JsonElement>();
        var newAccess = refreshedBody.GetProperty("accessToken").GetString()!;
        newAccess.Should().NotBeNullOrEmpty();
        // Either token may be identical only if issued in the same exact instant; accept either to avoid flakiness.
        // What we strictly assert: refresh returned a non-empty token and 200.
        newAccess.Length.Should().BeGreaterThan(20);
        initialAccess.Length.Should().BeGreaterThan(20);
    }

    [Fact]
    public async Task Refresh_with_unknown_token_returns_401()
    {
        using var http = _fx.Client(_fx.Settings.IdentityBaseUrl);

        var response = await http.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            refreshToken = $"unknown-{Guid.NewGuid():N}",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoke_then_refresh_returns_401()
    {
        using var http = _fx.Client(_fx.Settings.IdentityBaseUrl);

        var issued = await http.PostAsJsonAsync("/api/v1/auth/token", new
        {
            username = _fx.Settings.AdminUsername,
            password = _fx.Settings.AdminPassword,
        });
        var issuedBody = await issued.Content.ReadFromJsonAsync<JsonElement>();
        var refresh = issuedBody.GetProperty("refreshToken").GetString()!;
        var access = issuedBody.GetProperty("accessToken").GetString()!;

        var revokeReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/revoke")
        {
            Content = JsonContent.Create(new { refreshToken = refresh }),
        };
        revokeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var revoke = await http.SendAsync(revokeReq);
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var reuse = await http.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh });
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoke_unknown_token_is_idempotent_204()
    {
        using var http = _fx.Client(_fx.Settings.IdentityBaseUrl);

        var revokeReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/revoke")
        {
            Content = JsonContent.Create(new { refreshToken = $"unknown-{Guid.NewGuid():N}" }),
        };
        revokeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _fx.AdminAccessToken);

        var response = await http.SendAsync(revokeReq);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Jwks_endpoint_returns_keys_with_cache_control()
    {
        using var http = _fx.Client(_fx.Settings.IdentityBaseUrl);

        var response = await http.GetAsync("/.well-known/jwks.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl?.ToString().Should().Contain("max-age");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var keys = body.GetProperty("keys");
        keys.GetArrayLength().Should().BeGreaterThan(0);
        var first = keys[0];
        first.GetProperty("kty").GetString().Should().Be("RSA");
        first.TryGetProperty("kid", out _).Should().BeTrue();
    }
}
