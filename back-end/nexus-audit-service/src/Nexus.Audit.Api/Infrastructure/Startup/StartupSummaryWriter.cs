using System.Reflection;
using Nexus.Audit.Api.Configuration;
using Spectre.Console;

namespace Nexus.Audit.Api.Infrastructure.Startup;

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
        var baseUrl = cfg["Kestrel:Endpoints:Http:Url"] ?? "http://+:12000";

        var auth = cfg.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        var idem = cfg.GetSection(IdempotencyOptions.SectionName).Get<IdempotencyOptions>() ?? new IdempotencyOptions();

        var configTable = new Table().Border(TableBorder.Rounded).AddColumns("[bold]Setting[/]", "[bold]Value[/]");
        configTable.AddRow("Service", "nexus-audit");
        configTable.AddRow("Version", version);
        configTable.AddRow("Environment", env.EnvironmentName);
        configTable.AddRow("Listening", baseUrl);
        configTable.AddRow("JWT issuer", auth.Issuer);
        configTable.AddRow("JWT audience", auth.Audience);
        configTable.AddRow("Idempotency retention (days)", idem.RetentionDays.ToString());
        configTable.AddRow("Clock skew (min)", idem.MaxClockSkewMinutes.ToString());
        AnsiConsole.Write(new Panel(configTable).Header("[green]Configuration[/]"));

        var depsTable = new Table().Border(TableBorder.Rounded).AddColumns("[bold]Dependency[/]", "[bold]Endpoint[/]", "[bold]Status[/]");
        var connString = cfg.GetConnectionString("Default");
        depsTable.AddRow(
            "SQL Server",
            string.IsNullOrWhiteSpace(connString) ? "(unset)" : "(configured)",
            string.IsNullOrWhiteSpace(connString) ? "[yellow]not configured[/]" : "[green]wired[/]");
        depsTable.AddRow("Identity (JWKS)", auth.ResolvedJwksUrl, "[green]wired[/]");
        var otlp = cfg["Observability:Otlp:Endpoint"];
        depsTable.AddRow(
            "OTLP endpoint",
            string.IsNullOrWhiteSpace(otlp) ? "(disabled)" : otlp,
            string.IsNullOrWhiteSpace(otlp) ? "[grey]none[/]" : "[green]exporter[/]");
        AnsiConsole.Write(new Panel(depsTable).Header("[yellow]Dependencies[/]"));

        var rl = cfg.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();
        var cors = cfg.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
        var obs = cfg.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        var togglesTable = new Table().Border(TableBorder.Rounded).AddColumns("[bold]Feature[/]", "[bold]State[/]");
        togglesTable.AddRow("Rate limiting", rl.Enabled
            ? $"[green]ON[/] (read={rl.PermitLimit}/{rl.WindowSeconds}s, append={rl.AppendPermitLimit}/min)"
            : "[grey]OFF[/]");
        togglesTable.AddRow("CORS origins", string.Join(", ", cors.AllowedOrigins));
        togglesTable.AddRow("Swagger UI", "/swagger");
        togglesTable.AddRow("ReDoc UI", "/redoc");
        togglesTable.AddRow("Prometheus", obs.Prometheus.Enabled ? "/metrics" : "[grey]disabled[/]");
        togglesTable.AddRow("Health", "/health, /health/live, /health/ready");
        AnsiConsole.Write(new Panel(togglesTable).Header("[blue]Toggles[/]"));
    }
}
