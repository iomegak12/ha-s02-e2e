namespace Nexus.Identity.Api.Configuration;

/// <summary>
/// Dev-only admin seeder configuration. Bound from <c>Seeder</c>.
/// Effective only when <c>IHostEnvironment.IsDevelopment()</c> AND <see cref="DevSeedAdminEnabled"/> is <c>true</c>.
/// </summary>
public sealed class SeederOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Seeder";

    /// <summary>Master toggle for the dev seeder.</summary>
    public bool DevSeedAdminEnabled { get; init; } = false;

    /// <summary>Username for the seeded administrator.</summary>
    public string DevSeedAdminUsername { get; init; } = "admin";

    /// <summary>Display name for the seeded administrator.</summary>
    public string DevSeedAdminDisplayName { get; init; } = "Default Admin";

    /// <summary>Password for the seeded administrator. Compose / .env override at runtime.</summary>
    public string DevSeedAdminPassword { get; init; } = "Nexus_DevPass1!";
}
