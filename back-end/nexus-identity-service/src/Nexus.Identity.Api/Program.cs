namespace Nexus.Identity.Api;

/// <summary>
/// Composition root for the Nexus HA Identity &amp; Admin service.
/// </summary>
public static class Program
{
    /// <summary>
    /// Application entry point. Phase 0 wires only a liveness endpoint;
    /// later phases add observability, persistence, auth, and feature controllers.
    /// </summary>
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var app = builder.Build();

        app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));

        app.Run();
    }
}
