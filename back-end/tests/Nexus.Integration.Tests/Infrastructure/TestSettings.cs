using Microsoft.Extensions.Configuration;

namespace Nexus.IntegrationTests.Infrastructure;

/// <summary>
/// Strongly-typed view of <c>appsettings.json</c> for the integration test suite.
/// Env-var overrides win over JSON.
/// </summary>
public sealed record TestSettings(
    string IdentityBaseUrl,
    string AuditBaseUrl,
    string BranchesBaseUrl,
    string PatientsBaseUrl,
    string DoctorsBaseUrl,
    string AdminUsername,
    string AdminPassword,
    int HealthWaitSeconds)
{
    public static TestSettings Load()
    {
        var cfg = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        return new TestSettings(
            IdentityBaseUrl: cfg["IdentityBaseUrl"] ?? "http://localhost:8000",
            AuditBaseUrl: cfg["AuditBaseUrl"] ?? "http://localhost:12000",
            BranchesBaseUrl: cfg["BranchesBaseUrl"] ?? "http://localhost:11000",
            PatientsBaseUrl: cfg["PatientsBaseUrl"] ?? "http://localhost:9000",
            DoctorsBaseUrl: cfg["DoctorsBaseUrl"] ?? "http://localhost:10000",
            AdminUsername: cfg["AdminUsername"] ?? "admin",
            AdminPassword: cfg["AdminPassword"] ?? "Nexus_DevPass1!",
            HealthWaitSeconds: int.TryParse(cfg["HealthWaitSeconds"], out var s) ? s : 60);
    }
}
