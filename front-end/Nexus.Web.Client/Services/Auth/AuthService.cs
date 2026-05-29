using System.Net.Http.Json;
using System.Text.Json;
using Nexus.Web.Client.Models.Auth;
using Nexus.Web.Client.Models.Common;
using Nexus.Web.Client.State;

namespace Nexus.Web.Client.Services.Auth;

public sealed class AuthService : IAuthService
{
    private readonly HttpClient              _http;
    private readonly NexusAuthStateProvider  _authState;

    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    public AuthService(HttpClient http, NexusAuthStateProvider authState)
    {
        _http      = http;
        _authState = authState;
    }

    public async Task<ApiResponse<SessionInfo>> LoginAsync(LoginRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/session/login", request);

        if (response.IsSuccessStatusCode)
        {
            var info = await response.Content.ReadFromJsonAsync<SessionInfo>(_jsonOpts);
            if (info is not null)
            {
                _authState.NotifyLoggedIn(info);
                return ApiResponse<SessionInfo>.Ok(info);
            }
        }

        return ApiResponse<SessionInfo>.Fail(await ReadProblemAsync(response));
    }

    public async Task<ApiResponse<SessionInfo>> RefreshAsync()
    {
        var response = await _http.PostAsync("/api/v1/session/refresh", content: null);

        if (response.IsSuccessStatusCode)
        {
            var info = await response.Content.ReadFromJsonAsync<SessionInfo>(_jsonOpts);
            if (info is not null)
            {
                _authState.NotifyLoggedIn(info);
                return ApiResponse<SessionInfo>.Ok(info);
            }
        }

        return ApiResponse<SessionInfo>.Fail(await ReadProblemAsync(response));
    }

    public async Task<ApiResponse<bool>> LogoutAsync()
    {
        var response = await _http.PostAsync("/api/v1/session/logout", content: null);

        _authState.NotifyLoggedOut();

        if (response.IsSuccessStatusCode) return ApiResponse<bool>.Ok(true);

        return ApiResponse<bool>.Fail(await ReadProblemAsync(response));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<ProblemDetails> ReadProblemAsync(HttpResponseMessage response)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOpts);
            return problem ?? UnknownError((int)response.StatusCode);
        }
        catch
        {
            return UnknownError((int)response.StatusCode);
        }
    }

    private static ProblemDetails UnknownError(int status) => new()
    {
        Title  = "An unexpected error occurred",
        Detail = "Please try again. If the problem persists, contact support.",
        Status = status,
    };
}
