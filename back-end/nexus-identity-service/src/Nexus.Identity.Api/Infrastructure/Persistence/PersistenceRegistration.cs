using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.Identity.Api.Configuration;

namespace Nexus.Identity.Api.Infrastructure.Persistence;

/// <summary>
/// DI wiring for <see cref="IdentityDbContext"/> and related health checks.
/// </summary>
public static class PersistenceRegistration
{
    /// <summary>Registers the DbContext, interceptor, and SQL Server health check.</summary>
    public static void Add(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<RowVersionInterceptor>();

        builder.Services.AddDbContext<IdentityDbContext>((sp, options) =>
        {
            var connectionString = builder.Configuration.GetConnectionString("Default") ?? string.Empty;
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
            options.AddInterceptors(sp.GetRequiredService<RowVersionInterceptor>());
        });
    }

    /// <summary>
    /// Optionally applies pending migrations on startup. Honours <c>Persistence:AutoMigrate</c>
    /// and <c>Persistence:FailFast</c> from configuration.
    /// </summary>
    public static async Task ApplyMigrationsAsync(WebApplication app)
    {
        var persistence = app.Services.GetRequiredService<IOptions<PersistenceOptions>>().Value;
        if (!persistence.AutoMigrate)
        {
            return;
        }

        var connectionString = app.Configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            app.Logger.LogWarning("Persistence:AutoMigrate is true but ConnectionStrings:Default is empty; skipping migration");
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        try
        {
            await db.Database.MigrateAsync();
            app.Logger.LogInformation("Applied EF Core migrations to NexusIdentity database");
        }
        catch (Exception ex) when (!persistence.FailFast)
        {
            app.Logger.LogError(ex, "Migration failed; continuing because Persistence:FailFast is false");
        }
    }
}
