using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Web.Host.Auth;

namespace Nexus.Web.Host.Controllers;

[ApiController]
[Route("api/v1/me")]
[Authorize(AuthenticationSchemes = "NexusCookie")]
public sealed class MeController : ControllerBase
{
    private readonly ITokenStore _tokenStore;
    private readonly ILogger<MeController> _logger;

    public MeController(ITokenStore tokenStore, ILogger<MeController> logger)
    {
        _tokenStore = tokenStore;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/v1/me
    /// Returns the current session admin identity decoded from the server-side JWT payload.
    /// The JWT is never forwarded to the browser — only the decoded claims are returned.
    /// </summary>
    [HttpGet]
    public IActionResult GetMe()
    {
        var sessionId = User.FindFirstValue("nexus.session_id");
        if (sessionId is null) return Unauthorized();

        var entry = _tokenStore.Get(sessionId);
        if (entry is null)
        {
            _logger.LogWarning("Session {SessionId} has a valid cookie but no token in store", sessionId);
            return Unauthorized();
        }

        // Decode JWT payload (Base64 — no signature verification needed here; Identity already validated)
        AdminClaims? admin;
        try
        {
            admin = DecodeJwtPayload(entry.AccessToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decode JWT payload for session {SessionId}", sessionId);
            return StatusCode(500);
        }

        if (admin is null) return StatusCode(500);

        return Ok(new
        {
            id            = admin.Sub,
            username      = admin.Username,
            role          = admin.Role,
            sessionExpiry = entry.AccessTokenExpiry
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AdminClaims? DecodeJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) return null;

        var payload = parts[1];
        // Fix Base64 padding
        payload = payload.Replace('-', '+').Replace('_', '/');
        var pad = payload.Length % 4;
        if (pad == 2) payload += "==";
        else if (pad == 3) payload += "=";

        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        return JsonSerializer.Deserialize<AdminClaims>(json);
    }

    private sealed class AdminClaims
    {
        [JsonPropertyName("sub")]      public string Sub      { get; init; } = "";
        [JsonPropertyName("username")] public string Username { get; init; } = "";
        [JsonPropertyName("role")]     public string Role     { get; init; } = "";
    }
}
