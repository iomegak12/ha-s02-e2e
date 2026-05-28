using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.Identity.Api.Configuration;
using Nexus.Identity.Api.Infrastructure.Persistence;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;
using Nexus.Identity.Api.Infrastructure.Security;

namespace Nexus.Identity.Api.Infrastructure.Startup;

/// <summary>
/// Dev-only one-shot seeder that ensures a default admin exists so integration
/// tests (and first-time developers) can issue tokens immediately. No-ops when:
/// <list type="bullet">
///   <item>the host is not <c>Development</c>, or</item>
///   <item><c>Seeder:DevSeedAdminEnabled</c> is <c>false</c>, or</item>
///   <item>the configured username already exists in <c>Admins</c>.</item>
/// </list>
/// Idempotent — safe to run on every boot.
/// </summary>
public sealed class AdminSeederWorker : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<SeederOptions> _options;
    private readonly IHostEnvironment _env;
    private readonly ILogger<AdminSeederWorker> _logger;

    /// <summary>Create the worker.</summary>
    public AdminSeederWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<SeederOptions> options,
        IHostEnvironment env,
        ILogger<AdminSeederWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _env = env;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = _options.Value;

        if (!_env.IsDevelopment())
        {
            _logger.LogDebug("AdminSeederWorker: skipping (env is {Env})", _env.EnvironmentName);
            return;
        }

        if (!opts.DevSeedAdminEnabled)
        {
            _logger.LogDebug("AdminSeederWorker: skipping (Seeder:DevSeedAdminEnabled is false)");
            return;
        }

        if (string.IsNullOrWhiteSpace(opts.DevSeedAdminUsername) || string.IsNullOrWhiteSpace(opts.DevSeedAdminPassword))
        {
            _logger.LogWarning("AdminSeederWorker: username or password is empty; skipping");
            return;
        }

        try
        {
            await SeedAsync(opts, cancellationToken);
        }
        catch (Exception ex)
        {
            // Don't crash the host: integration tests can fall back to creating an admin via the API.
            _logger.LogError(ex, "AdminSeederWorker: failed to seed default admin");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedAsync(SeederOptions opts, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var username = opts.DevSeedAdminUsername.Trim().ToLowerInvariant();
        var exists = await db.Admins.AsNoTracking().AnyAsync(a => a.Username == username, ct);
        if (exists)
        {
            _logger.LogInformation("AdminSeederWorker: admin '{Username}' already exists; no-op", username);
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        db.Admins.Add(new Admin
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = opts.DevSeedAdminDisplayName,
            PasswordHash = hasher.Hash(opts.DevSeedAdminPassword),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("AdminSeederWorker: seeded admin '{Username}'", username);
    }
}
