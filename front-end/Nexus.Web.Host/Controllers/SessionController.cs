using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Web.Host.Auth;

namespace Nexus.Web.Host.Controllers;

[ApiController]
[Route("api/v1/session")]
public sealed class SessionController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITokenStore _tokenStore;
    private readonly ILogger<SessionController> _logger;

    public SessionController(
        IHttpClientFactory httpClientFactory,
        ITokenStore tokenStore,
        ILogger<SessionController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/v1/session/login
    /// Exchanges credentials with the Identity Service, stores tokens server-side,
    /// and issues an HttpOnly session cookie to the browser.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("Identity");

        HttpResponseMessage identityResponse;
        try
        {
            identityResponse = await client.PostAsJsonAsync(
                "/api/v1/auth/token",
                new { username = request.Username, password = request.Password },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Identity service unreachable during login");
            return StatusCode(503, ProblemDetailsFor(503,
                "Service Unavailable", "Identity service is temporarily unavailable.", "/api/v1/session/login"));
        }

        if (!identityResponse.IsSuccessStatusCode)
        {
            var problem = await identityResponse.Content.ReadFromJsonAsync<ProblemDetailsDto>(cancellationToken: ct);
            return StatusCode((int)identityResponse.StatusCode, problem);
        }

        var tokenResponse = await identityResponse.Content
            .ReadFromJsonAsync<IdentityTokenResponse>(cancellationToken: ct);

        if (tokenResponse is null)
            return StatusCode(502, ProblemDetailsFor(502, "Bad Gateway", "Unexpected response from Identity service.", "/api/v1/session/login"));

        // Build claims principal from the access token payload (decode without validation — Identity already validated it)
        var sessionId = Guid.NewGuid().ToString("N");
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, sessionId),
            new Claim("nexus.session_id", sessionId),
        };

        var identity = new ClaimsIdentity(claims, "NexusCookie");
        var principal = new ClaimsPrincipal(identity);

        var accessExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        // Refresh tokens from Identity are 7 days by default; use a conservative fallback
        var refreshExpiry = DateTimeOffset.UtcNow.AddDays(7);

        _tokenStore.Store(sessionId, new TokenEntry(
            tokenResponse.AccessToken,
            tokenResponse.RefreshToken,
            accessExpiry,
            refreshExpiry));

        await HttpContext.SignInAsync("NexusCookie", principal);

        return Ok(new { message = "Authenticated." });
    }

    /// <summary>
    /// POST /api/v1/session/refresh
    /// Silently rotates the access token using the stored refresh token.
    /// </summary>
    [HttpPost("refresh")]
    [Authorize(AuthenticationSchemes = "NexusCookie")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var sessionId = User.FindFirstValue("nexus.session_id");
        if (sessionId is null) return Unauthorized();

        var entry = _tokenStore.Get(sessionId);
        if (entry is null) return Unauthorized();

        var client = _httpClientFactory.CreateClient("Identity");

        HttpResponseMessage identityResponse;
        try
        {
            identityResponse = await client.PostAsJsonAsync(
                "/api/v1/auth/refresh",
                new { refreshToken = entry.RefreshToken },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Identity service unreachable during token refresh");
            return StatusCode(503, ProblemDetailsFor(503, "Service Unavailable",
                "Identity service is temporarily unavailable.", "/api/v1/session/refresh"));
        }

        if (!identityResponse.IsSuccessStatusCode)
        {
            // Refresh token rejected — clear session
            _tokenStore.Remove(sessionId);
            await HttpContext.SignOutAsync("NexusCookie");
            return Unauthorized();
        }

        var tokenResponse = await identityResponse.Content
            .ReadFromJsonAsync<IdentityTokenResponse>(cancellationToken: ct);

        if (tokenResponse is null) return StatusCode(502);

        var accessExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        var refreshExpiry = entry.RefreshTokenExpiry; // keep original refresh expiry

        _tokenStore.Store(sessionId, new TokenEntry(
            tokenResponse.AccessToken,
            tokenResponse.RefreshToken,
            accessExpiry,
            refreshExpiry));

        return Ok(new { expiresIn = tokenResponse.ExpiresIn });
    }

    /// <summary>
    /// POST /api/v1/session/logout
    /// Revokes the refresh token at the Identity Service, clears server-side token, signs out.
    /// </summary>
    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = "NexusCookie")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var sessionId = User.FindFirstValue("nexus.session_id");

        if (sessionId is not null)
        {
            var entry = _tokenStore.Get(sessionId);
            if (entry is not null)
            {
                try
                {
                    var client = _httpClientFactory.CreateClient("Identity");
                    await client.PostAsJsonAsync(
                        "/api/v1/auth/revoke",
                        new { refreshToken = entry.RefreshToken },
                        ct);
                }
                catch (Exception ex)
                {
                    // Log but do not block logout
                    _logger.LogWarning(ex, "Could not revoke refresh token at Identity service during logout");
                }
                _tokenStore.Remove(sessionId);
            }
        }

        await HttpContext.SignOutAsync("NexusCookie");
        return Ok(new { message = "Signed out." });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static object ProblemDetailsFor(int status, string title, string detail, string instance) =>
        new { type = $"https://errors.nexusha.local/gateway-error", title, status, detail, instance };

    // DTOs

    public sealed record LoginRequest(string Username, string Password);

    private sealed class IdentityTokenResponse
    {
        [JsonPropertyName("accessToken")]  public string AccessToken  { get; init; } = "";
        [JsonPropertyName("tokenType")]    public string TokenType    { get; init; } = "Bearer";
        [JsonPropertyName("expiresIn")]    public int    ExpiresIn    { get; init; } = 900;
        [JsonPropertyName("refreshToken")] public string RefreshToken { get; init; } = "";
    }

    private sealed class ProblemDetailsDto
    {
        [JsonPropertyName("type")]    public string? Type    { get; init; }
        [JsonPropertyName("title")]   public string? Title   { get; init; }
        [JsonPropertyName("status")]  public int?    Status  { get; init; }
        [JsonPropertyName("detail")]  public string? Detail  { get; init; }
        [JsonPropertyName("code")]    public string? Code    { get; init; }
        [JsonPropertyName("traceId")] public string? TraceId { get; init; }
    }
}
