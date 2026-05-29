namespace Nexus.Web.Host.Proxy;

/// <summary>
/// Maps an incoming /api/v1/{prefix}/* path to the name of the named HttpClient
/// registered in DI (matching the keys in appsettings.json Services section).
/// Returns null when the path is handled locally (session/me controllers).
/// </summary>
public static class ServiceRouter
{
    private static readonly (string Prefix, string ClientName)[] Routes =
    [
        ("/api/v1/admins",    "Identity"),
        ("/api/v1/patients",  "Patients"),
        ("/api/v1/doctors",   "Doctors"),
        ("/api/v1/branches",  "Branches"),
        ("/api/v1/audit",     "Audit"),
    ];

    /// <summary>
    /// Returns the named HttpClient key for the given request path,
    /// or null if the request should be handled locally.
    /// </summary>
    public static string? Resolve(string path)
    {
        foreach (var (prefix, clientName) in Routes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return clientName;
        }
        return null;
    }
}
