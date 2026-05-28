using System.Text.Json.Serialization;

namespace Nexus.Identity.Api.Features.Auth.Models;

/// <summary>
/// Successful response from <c>POST /api/v1/auth/token</c> and <c>/refresh</c>.
/// </summary>
public sealed class TokenResponse
{
    /// <summary>Short-lived RS256-signed JWT.</summary>
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>Always <c>Bearer</c>.</summary>
    [JsonPropertyName("tokenType")]
    public string TokenType { get; init; } = "Bearer";

    /// <summary>Access-token lifetime in seconds.</summary>
    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; init; }

    /// <summary>Opaque refresh token (plaintext, only returned at issue/rotate).</summary>
    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; init; } = string.Empty;
}
