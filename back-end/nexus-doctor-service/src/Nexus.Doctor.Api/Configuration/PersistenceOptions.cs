namespace Nexus.Doctors.Api.Configuration;

/// <summary>Toggles for EF Core migration application at startup.</summary>
public class PersistenceOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Persistence";

    /// <summary>When true, applies pending migrations on startup (Development default).</summary>
    public bool AutoMigrate { get; set; }

    /// <summary>When true, a database failure during startup aborts the process.</summary>
    public bool FailFast { get; set; }
}
