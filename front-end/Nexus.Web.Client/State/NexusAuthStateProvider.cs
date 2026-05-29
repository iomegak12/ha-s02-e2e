using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Nexus.Web.Client.Models.Auth;
using Nexus.Web.Client.State;

namespace Nexus.Web.Client.State;

/// <summary>
/// Custom AuthenticationStateProvider that determines auth status
/// by calling GET /api/v1/me on the Host (BFF).
/// The Host validates the HttpOnly session cookie server-side; the WASM client
/// never touches a JWT.
/// </summary>
public sealed class NexusAuthStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient  _http;
    private readonly SessionState _session;

    private static readonly AuthenticationState _anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public NexusAuthStateProvider(HttpClient http, SessionState session)
    {
        _http    = http;
        _session = session;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var info = await _http.GetFromJsonAsync<SessionInfo>("/api/v1/me");
            if (info is null) return _anonymous;

            _session.Set(info);
            return BuildState(info);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _session.Clear();
            return _anonymous;
        }
        catch
        {
            _session.Clear();
            return _anonymous;
        }
    }

    public void NotifyLoggedIn(SessionInfo info)
    {
        _session.Set(info);
        NotifyAuthenticationStateChanged(Task.FromResult(BuildState(info)));
    }

    public void NotifyLoggedOut()
    {
        _session.Clear();
        NotifyAuthenticationStateChanged(Task.FromResult(_anonymous));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static AuthenticationState BuildState(SessionInfo info)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, info.Id),
                new Claim(ClaimTypes.Name,           info.Username),
                new Claim(ClaimTypes.Role,           info.Role),
            },
            authenticationType: "NexusCookie");

        return new AuthenticationState(new ClaimsPrincipal(identity));
    }
}
