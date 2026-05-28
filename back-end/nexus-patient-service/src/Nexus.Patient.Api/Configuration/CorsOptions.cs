namespace Nexus.Patients.Api.Configuration;

/// <summary>CORS policy configuration.</summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; init; } = new[] { "*" };
    public string[] AllowedHeaders { get; init; } = new[] { "*" };
    public string[] AllowedMethods { get; init; } = new[] { "*" };
    public bool AllowCredentials { get; init; } = false;
}
