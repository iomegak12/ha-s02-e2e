using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.Patients.Api.Configuration;

namespace Nexus.Patients.Api.Infrastructure.Persistence;

/// <summary>
/// DI wiring for <see cref="PatientDbContext"/> and the optional startup migration.
/// </summary>
public static class PersistenceRegistration
{
    /// <summary>
    /// Registers the DbContext against <c>ConnectionStrings:Default</c>. If the
    /// connection string is empty, no registration is added (integration tests
    /// substitute their own provider).
    /// </summary>
    public static void Add(WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        builder.Services.AddSingleton<AuditedTimestampInterceptor>();
        builder.Services.AddDbContext<PatientDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
            options.AddInterceptors(sp.GetRequiredService<AuditedTimestampInterceptor>());
        });
    }

    /// <summary>Optionally applies pending migrations on startup.</summary>
    public static async Task ApplyMigrationsAsync(WebApplication app)
    {
        var persistence = app.Services.GetRequiredService<IOptions<PersistenceOptions>>().Value;
        if (!persistence.AutoMigrate) return;

        var connectionString = app.Configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            app.Logger.LogWarning("Persistence:AutoMigrate is true but ConnectionStrings:Default is empty; skipping migration");
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PatientDbContext>();
        try
        {
            await db.Database.MigrateAsync();
            app.Logger.LogInformation("Applied EF Core migrations to NexusPatients database");
        }
        catch (Exception ex) when (!persistence.FailFast)
        {
            app.Logger.LogError(ex, "Migration failed; continuing because Persistence:FailFast is false");
        }
    }
}
