using Spectre.Console;

namespace Nexus.Identity.Api.Infrastructure.Startup;

/// <summary>
/// Writes the cyan Figlet startup banner to the console.
/// </summary>
public static class BannerWriter
{
    /// <summary>Emit the banner.</summary>
    public static void Write()
    {
        try
        {
            AnsiConsole.Write(new FigletText("Nexus Identity").Color(Color.Cyan1));
        }
        catch
        {
            // Non-interactive console (Docker, CI). Skip silently.
            Console.WriteLine("=== Nexus Identity ===");
        }
    }
}
