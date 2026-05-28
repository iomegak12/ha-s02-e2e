using System.Security.Claims;

namespace Nexus.Audit.Api.Infrastructure.Auth;

/// <summary>
/// Convenience projection of the authenticated principal's claims, used by the
/// audit service to stamp <c>ActorId</c> / <c>ActorUsername</c> on appended entries
/// when the caller omits them.
/// </summary>
/// <param name="Id">Value of the <c>sub</c> claim parsed as a GUID; <see cref="Guid.Empty"/> if absent.</param>
/// <param name="Username">Value of <c>preferred_username</c> (or <c>name</c>) claim; empty if absent.</param>
/// <param name="IsAdmin">Whether the principal carries the <c>Admin</c> role.</param>
public sealed record CurrentUser(Guid Id, string Username, bool IsAdmin)
{
    /// <summary>Builds a <see cref="CurrentUser"/> from a <see cref="ClaimsPrincipal"/>.</summary>
    public static CurrentUser From(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirst("sub")?.Value;
        var id = Guid.TryParse(sub, out var parsed) ? parsed : Guid.Empty;
        var username = principal.FindFirst("preferred_username")?.Value
                       ?? principal.FindFirst("name")?.Value
                       ?? string.Empty;
        var isAdmin = principal.IsInRole("Admin");
        return new CurrentUser(id, username, isAdmin);
    }
}
