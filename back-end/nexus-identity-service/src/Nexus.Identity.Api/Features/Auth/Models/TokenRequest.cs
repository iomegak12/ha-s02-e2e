namespace Nexus.Identity.Api.Features.Auth.Models;

/// <summary>
/// Request body for <c>POST /api/v1/auth/token</c>.
/// </summary>
public sealed class TokenRequest
{
    /// <summary>Admin username.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Admin password (plaintext at the wire, never logged).</summary>
    public string Password { get; init; } = string.Empty;
}
