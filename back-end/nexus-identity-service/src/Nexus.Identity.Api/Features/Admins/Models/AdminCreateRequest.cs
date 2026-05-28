namespace Nexus.Identity.Api.Features.Admins.Models;

/// <summary>
/// Request body for <c>POST /api/v1/admins</c>.
/// </summary>
public sealed class AdminCreateRequest
{
    /// <summary>Login handle. Lowercase, dot/underscore/dash separated.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Initial password (plaintext on the wire).</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Whether the admin is active. Defaults to true.</summary>
    public bool? IsActive { get; init; }
}
