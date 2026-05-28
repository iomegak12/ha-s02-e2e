using System.Reflection;
using Spectre.Console;

namespace Nexus.Branches.Api.Infrastructure.Startup;

/// <summary>
/// Writes the three configuration panels (Configuration / Dependencies / Toggles)
/// to the console after the host has been built and before <c>app.Run()</c>.
/// Panels are intentionally sparse in Phase 0 and filled in as features land.
/// </summary>
public static class StartupSummaryWriter
{
    /// <summary>Emit the panels.</summary>
    public static void Write(WebApplication app)
    {
        try
        {
            WriteCore(app);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[startup-summary] failed to render: {ex.Message}");
        }
    }

    private static void WriteCore(WebApplication app)
    {
        var cfg = app.Configuration;
        var env = app.Environment;
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var baseUrl = cfg["Kestrel:Endpoints:Http:Url"] ?? "http://+:11000";

        var configTable = new Table().Border(TableBorder.Rounded).AddColumns("[bold]Setting[/]", "[bold]Value[/]");
        configTable.AddRow("Service", "nexus-branch");
        configTable.AddRow("Version", version);
        configTable.AddRow("Environment", env.EnvironmentName);
        configTable.AddRow("Listening", baseUrl);
        AnsiConsole.Write(new Panel(configTable).Header("[green]Configuration[/]"));

        var depsTable = new Table().Border(TableBorder.Rounded).AddColumns("[bold]Dependency[/]", "[bold]Endpoint[/]", "[bold]Status[/]");
        depsTable.AddRow("SQL Server", cfg.GetConnectionString("Default") ?? "(unset)", "[grey]added in Phase 1[/]");
        depsTable.AddRow("Identity (JWKS)", cfg["Auth:Authority"] ?? "(unset)", "[grey]added in Phase 2[/]");
        depsTable.AddRow("Audit publisher", cfg["AuditClient:BaseUrl"] ?? "(unset)", "[grey]added in Phase 3[/]");
        depsTable.AddRow("OTLP endpoint", cfg["Observability:Otlp:Endpoint"] ?? "(disabled)", "[grey]added in Phase 2[/]");
        AnsiConsole.Write(new Panel(depsTable).Header("[yellow]Dependencies[/]"));

        var togglesTable = new Table().Border(TableBorder.Rounded).AddColumns("[bold]Feature[/]", "[bold]State[/]");
        togglesTable.AddRow("Rate limiting", "[grey]Phase 2[/]");
        togglesTable.AddRow("CORS", "[grey]Phase 2[/]");
        togglesTable.AddRow("Swagger UI", "[grey]Phase 4[/]");
        togglesTable.AddRow("ReDoc UI", "[grey]Phase 4[/]");
        togglesTable.AddRow("Prometheus", "[grey]Phase 2[/]");
        togglesTable.AddRow("Health", "[grey]Phase 2[/]");
        AnsiConsole.Write(new Panel(togglesTable).Header("[blue]Toggles[/]"));
    }
}
