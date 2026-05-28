using Spectre.Console;

namespace Nexus.Doctors.Api.Infrastructure.Startup;

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
            AnsiConsole.Write(new FigletText("Nexus Doctors").Color(Color.Cyan1));
        }
        catch
        {
            Console.WriteLine("=== Nexus Doctors ===");
        }
    }
}
