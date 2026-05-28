using System.Security.Claims;

namespace Nexus.Doctors.Api.Infrastructure.Auth;

/// <summary>Convenience projection of the authenticated principal's claims.</summary>
public sealed record CurrentUser(Guid Id, string Username, bool IsAdmin)
{
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
