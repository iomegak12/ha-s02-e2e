namespace Nexus.Audit.Api.Infrastructure.Auth;

/// <summary>
/// Named authorization policies registered by <see cref="AuthRegistration"/>.
/// </summary>
public static class AuthPolicies
{
    /// <summary>Requires an authenticated principal with the <c>Admin</c> role.</summary>
    public const string AdminOnly = "AdminOnly";
}
