using System.Diagnostics;

namespace Nexus.IntegrationTests.Infrastructure;

/// <summary>
/// Suite-wide fixture. Waits for both services' <c>/health/ready</c> to flip to 200
/// (capped at <see cref="TestSettings.HealthWaitSeconds"/>) and pre-acquires an
/// admin JWT so individual tests do not pay the login cost.
/// </summary>
public sealed class StackFixture : IAsyncLifetime
{
    public TestSettings Settings { get; }
    public string AdminAccessToken { get; private set; } = string.Empty;
    public string AdminRefreshToken { get; private set; } = string.Empty;

    public StackFixture()
    {
        Settings = TestSettings.Load();
    }

    public async Task InitializeAsync()
    {
        await WaitForHealthyAsync(Settings.IdentityBaseUrl);
        await WaitForHealthyAsync(Settings.AuditBaseUrl);
        await WaitForHealthyAsync(Settings.BranchesBaseUrl);
        await WaitForHealthyAsync(Settings.PatientsBaseUrl);
        await WaitForHealthyAsync(Settings.DoctorsBaseUrl);

        var (access, refresh) = await IssueTokenAsync();
        AdminAccessToken = access;
        AdminRefreshToken = refresh;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Returns a bare <see cref="HttpClient"/> rooted at the given service.</summary>
    public HttpClient Client(string baseUrl) =>
        new() { BaseAddress = new Uri(baseUrl) };

    /// <summary>Returns a client carrying the pre-acquired admin bearer.</summary>
    public HttpClient AuthClient(string baseUrl)
    {
        var c = Client(baseUrl);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminAccessToken);
        return c;
    }

    private async Task WaitForHealthyAsync(string baseUrl)
    {
        var deadline = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(Settings.HealthWaitSeconds);
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(5) };
        Exception? last = null;

        while (deadline.Elapsed < timeout)
        {
            try
            {
                var response = await http.GetAsync("/health/ready");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new InvalidOperationException(
            $"Service at {baseUrl} did not become healthy within {Settings.HealthWaitSeconds}s. " +
            $"Last error: {last?.Message ?? "(no exception — last response was non-success)"}");
    }

    private async Task<(string AccessToken, string RefreshToken)> IssueTokenAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri(Settings.IdentityBaseUrl) };
        var body = new { username = Settings.AdminUsername, password = Settings.AdminPassword };

        var response = await http.PostAsJsonAsync("/api/v1/auth/token", body);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Token issuance failed ({(int)response.StatusCode}): {raw}");
        }

        using var doc = JsonDocument.Parse(raw);
        var access = doc.RootElement.GetProperty("accessToken").GetString() ?? "";
        var refresh = doc.RootElement.GetProperty("refreshToken").GetString() ?? "";
        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh))
        {
            throw new InvalidOperationException($"Token issuance returned empty tokens: {raw}");
        }
        return (access, refresh);
    }
}

/// <summary>Marker for the shared collection (one fixture instance per assembly).</summary>
[CollectionDefinition("stack")]
public sealed class StackCollection : ICollectionFixture<StackFixture> { }
