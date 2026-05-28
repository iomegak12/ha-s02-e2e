namespace Nexus.Doctors.Api.Infrastructure.Auth;

/// <summary>Named authorization policies.</summary>
public static class AuthPolicies
{
    /// <summary>Requires an authenticated principal with the <c>Admin</c> role.</summary>
    public const string AdminOnly = "AdminOnly";
}
