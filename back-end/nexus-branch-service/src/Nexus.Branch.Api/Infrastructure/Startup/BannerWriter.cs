using Spectre.Console;

namespace Nexus.Branches.Api.Infrastructure.Startup;

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
            AnsiConsole.Write(new FigletText("Nexus Branches").Color(Color.Cyan1));
        }
        catch
        {
            // Non-interactive console (Docker, CI). Skip silently.
            Console.WriteLine("=== Nexus Branches ===");
        }
    }
}
