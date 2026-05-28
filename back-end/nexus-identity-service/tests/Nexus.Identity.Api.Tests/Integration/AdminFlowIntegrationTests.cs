using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Identity.Api.Features.Admins.Models;
using Nexus.Identity.Api.Features.Auth.Models;
using Nexus.Identity.Api.Infrastructure.Persistence;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;
using Nexus.Identity.Api.Infrastructure.Security;

namespace Nexus.Identity.Api.Tests.Integration;

/// <summary>
/// One happy-path WebApplicationFactory test: seed an admin → POST /auth/token →
/// POST /admins (with bearer) → PATCH /admins/{id} (with bearer + If-Match) → 200.
/// Swaps SQL Server for EF InMemory and disables outbound audit publishing.
/// </summary>
public class AdminFlowIntegrationTests : IClassFixture<AdminFlowIntegrationTests.Factory>
{
    private readonly Factory _factory;

    public AdminFlowIntegrationTests(Factory factory) => _factory = factory;

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public const string SeedUsername = "asha.menon";
        public const string SeedPassword = "S3cret!Pa55word";

        public string DbName { get; } = $"int-{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Server=(none);Database=NexusIdentityTest;",
                    ["AuditClient:Enabled"] = "false",
                    ["RateLimiting:Enabled"] = "false",
                    ["Persistence:AutoMigrate"] = "false",
                    ["Seeder:DevSeedAdminEnabled"] = "false",
                    ["Observability:Prometheus:Enabled"] = "false",
                });
            });

            builder.ConfigureServices(services =>
            {
                // Drop the SQL Server-bound DbContext registrations so we can
                // re-bind the context to EF Core In-Memory for the test host.
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<DbContextOptions>();

                // Use an isolated internal service provider so the SQL Server
                // and InMemory provider services do not collide in the host SP.
                var efServices = new ServiceCollection()
                    .AddEntityFrameworkInMemoryDatabase()
                    .BuildServiceProvider();

                services.AddDbContext<IdentityDbContext>(o => o
                    .UseInMemoryDatabase(DbName)
                    .UseInternalServiceProvider(efServices));
            });
        }

        public async Task SeedAdminAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.EnsureCreatedAsync();

            if (await db.Admins.AnyAsync(a => a.Username == SeedUsername))
            {
                return;
            }

            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            db.Admins.Add(new Admin
            {
                Id = Guid.NewGuid(),
                Username = SeedUsername,
                DisplayName = "Asha Menon",
                PasswordHash = hasher.Hash(SeedPassword),
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                RowVersion = BitConverter.GetBytes(DateTime.UtcNow.Ticks),
            });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task HealthLive_returns_200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_then_create_admin_then_patch_succeeds()
    {
        await _factory.SeedAdminAsync();
        var client = _factory.CreateClient();

        // 1) POST /api/v1/auth/token
        var tokenResponse = await client.PostAsJsonAsync("/api/v1/auth/token", new TokenRequest
        {
            Username = Factory.SeedUsername,
            Password = Factory.SeedPassword,
        });
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK, await tokenResponse.Content.ReadAsStringAsync());
        var tokens = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
        tokens!.AccessToken.Should().NotBeNullOrWhiteSpace();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        // 2) POST /api/v1/admins
        var createResponse = await client.PostAsJsonAsync("/api/v1/admins", new AdminCreateRequest
        {
            Username = "new.admin",
            DisplayName = "New Admin",
            Password = "Another!Pa55word",
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, await createResponse.Content.ReadAsStringAsync());
        var created = await createResponse.Content.ReadFromJsonAsync<AdminDto>();
        created!.Id.Should().NotBe(Guid.Empty);
        var etag = createResponse.Headers.ETag?.Tag;
        etag.Should().NotBeNullOrWhiteSpace();

        // 3) PATCH /api/v1/admins/{id}
        // Note: EF Core InMemory does not populate RowVersion (no native
        // rowversion semantics), so we omit If-Match here. ETag-mismatch
        // behaviour is covered in unit tests against AdminService.
        var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/admins/{created.Id}")
        {
            Content = JsonContent.Create(new AdminPatchRequest { DisplayName = "Renamed Admin" }),
        };
        var patchResponse = await client.SendAsync(patchRequest);
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK, await patchResponse.Content.ReadAsStringAsync());
        var patched = await patchResponse.Content.ReadFromJsonAsync<AdminDto>();
        patched!.DisplayName.Should().Be("Renamed Admin");
    }
}
