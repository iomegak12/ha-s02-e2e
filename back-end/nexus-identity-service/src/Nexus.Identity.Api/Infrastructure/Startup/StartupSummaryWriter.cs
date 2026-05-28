using System.Reflection;
using Nexus.Identity.Api.Configuration;
using Spectre.Console;

namespace Nexus.Identity.Api.Infrastructure.Startup;

/// <summary>
/// Writes the three configuration panels (Configuration / Dependencies / Toggles)
/// to the console after the host has been built and before <c>app.Run()</c>.
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
            // Spectre may not support the current console; degrade gracefully.
            Console.WriteLine($"[startup-summary] failed to render: {ex.Message}");
        }
    }

    private static void WriteCore(WebApplication app)
    {
        var cfg = app.Configuration;
        var env = app.Environment;
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        var jwt = cfg.GetSection(JwtIssuerOptions.SectionName).Get<JwtIssuerOptions>() ?? new();
        var cors = cfg.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new();
        var rl = cfg.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new();
        var obs = cfg.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>() ?? new();
        var audit = cfg.GetSection(AuditClientOptions.SectionName).Get<AuditClientOptions>() ?? new();
        var baseUrl = cfg["Kestrel:Endpoints:Http:Url"] ?? "http://+:8000";

        var configTable = new Table().Border(TableBorder.Rounded).AddColumns("[bold]Setting[/]", "[bold]Value[/]");
        configTable.AddRow("Service", "nexus-identity");
        configTable.AddRow("Version", version);
        configTable.AddRow("Environment", env.EnvironmentName);
        configTable.AddRow("Listening", baseUrl);
        configTable.AddRow("JWT issuer", jwt.Issuer);
        configTable.AddRow("JWT audience", jwt.Audience);
        configTable.AddRow("Access lifetime (min)", jwt.AccessTokenLifetimeMinutes.ToString());
        configTable.AddRow("Refresh lifetime (days)", jwt.RefreshTokenLifetimeDays.ToString());
        configTable.AddRow("Key rotation (days)", jwt.RotationDays.ToString());
        AnsiConsole.Write(new Panel(configTable).Header("[green]Configuration[/]"));

        var depsTable = new Table().Border(TableBorder.Rounded).AddColumns("[bold]Dependency[/]", "[bold]Endpoint[/]", "[bold]Status[/]");
        depsTable.AddRow("SQL Server", cfg.GetConnectionString("Default") ?? "(unset)", "[grey]added in Phase 2[/]");
        depsTable.AddRow("Audit service", audit.BaseUrl, audit.Enabled ? "[green]enabled[/]" : "[yellow]disabled[/]");
        depsTable.AddRow("OTLP endpoint", string.IsNullOrWhiteSpace(obs.Otlp.Endpoint) ? "(disabled)" : obs.Otlp.Endpoint, "[grey]exporter[/]");
        AnsiConsole.Write(new Panel(depsTable).Header("[yellow]Dependencies[/]"));

        var togglesTable = new Table().Border(TableBorder.Rounded).AddColumns("[bold]Feature[/]", "[bold]State[/]");
        togglesTable.AddRow("Rate limiting", rl.Enabled
            ? $"[green]ON[/] ({rl.PermitLimit}/{rl.WindowSeconds}s)"
            : "[grey]OFF[/]");
        togglesTable.AddRow("CORS origins", string.Join(", ", cors.AllowedOrigins));
        togglesTable.AddRow("Swagger UI", "/swagger");
        togglesTable.AddRow("ReDoc UI", "/redoc");
        togglesTable.AddRow("Prometheus", obs.Prometheus.Enabled ? "/metrics" : "[grey]disabled[/]");
        togglesTable.AddRow("Health", "/health, /health/live, /health/ready");
        AnsiConsole.Write(new Panel(togglesTable).Header("[blue]Toggles[/]"));
    }
}
