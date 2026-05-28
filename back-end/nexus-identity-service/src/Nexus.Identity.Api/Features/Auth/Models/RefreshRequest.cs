namespace Nexus.Identity.Api.Features.Auth.Models;

/// <summary>
/// Request body for <c>POST /api/v1/auth/refresh</c> and <c>POST /api/v1/auth/revoke</c>.
/// </summary>
public sealed class RefreshRequest
{
    /// <summary>Opaque refresh token previously issued by this service.</summary>
    public string RefreshToken { get; init; } = string.Empty;
}
