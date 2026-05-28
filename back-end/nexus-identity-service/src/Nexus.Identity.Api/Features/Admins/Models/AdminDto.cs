using System.Text.Json.Serialization;

namespace Nexus.Identity.Api.Features.Admins.Models;

/// <summary>
/// Response shape for admin accounts. Matches <c>Admin</c> in <c>specs/identity.openapi.json</c>.
/// </summary>
public sealed class AdminDto
{
    /// <summary>Stable admin id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>Login handle.</summary>
    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Whether the admin can authenticate.</summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; init; }

    /// <summary>UTC creation timestamp.</summary>
    [JsonPropertyName("createdAtUtc")]
    public DateTime CreatedAtUtc { get; init; }
}
