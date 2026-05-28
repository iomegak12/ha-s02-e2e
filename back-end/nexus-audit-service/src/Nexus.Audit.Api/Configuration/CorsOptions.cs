namespace Nexus.Audit.Api.Configuration;

/// <summary>CORS policy configuration. Bound from <c>Cors</c>.</summary>
public sealed class CorsOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Cors";

    /// <summary>Allowed origins (use <c>["*"]</c> for any).</summary>
    public string[] AllowedOrigins { get; init; } = new[] { "*" };

    /// <summary>Allowed headers.</summary>
    public string[] AllowedHeaders { get; init; } = new[] { "*" };

    /// <summary>Allowed HTTP methods.</summary>
    public string[] AllowedMethods { get; init; } = new[] { "*" };

    /// <summary>Whether to allow credentials. Cannot be combined with <c>*</c> origins.</summary>
    public bool AllowCredentials { get; init; } = false;
}
