using Nexus.Identity.Api.Features.Auth.Models;

namespace Nexus.Identity.Api.Features.Auth.Service;

/// <summary>Use-cases for the Auth feature.</summary>
public interface IAuthService
{
    /// <summary>Authenticate an admin and issue a token pair.</summary>
    Task<TokenResponse> IssueAsync(TokenRequest request, CancellationToken cancellationToken);

    /// <summary>Rotate a refresh token for a new token pair.</summary>
    Task<TokenResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken);

    /// <summary>Revoke a refresh token. Idempotent (succeeds for unknown/already-revoked tokens).</summary>
    Task RevokeAsync(RefreshRequest request, CancellationToken cancellationToken);
}
