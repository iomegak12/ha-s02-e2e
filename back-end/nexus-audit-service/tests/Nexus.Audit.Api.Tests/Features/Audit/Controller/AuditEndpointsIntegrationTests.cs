using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Audit.Api.Features.Audit.Models;
using Nexus.Audit.Api.Infrastructure.Persistence;

namespace Nexus.Audit.Api.Tests.Features.Audit.Controller;

public sealed class AuditEndpointsIntegrationTests : IClassFixture<AuditEndpointsFactory>
{
    private readonly AuditEndpointsFactory _factory;

    public AuditEndpointsIntegrationTests(AuditEndpointsFactory factory) => _factory = factory;

    private static AppendAuditEntryRequest ValidBody(string summary = "Patient created via test") => new(
        EntityType: "Patient",
        EntityId: Guid.NewGuid(),
        EntityCode: null,
        Action: AuditActions.Created,
        ActorId: Guid.NewGuid(),
        ActorUsername: "admin",
        OccurredAtUtc: DateTime.UtcNow.AddSeconds(-1),
        Summary: summary,
        DiffJson: null);

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin");
        return client;
    }

    [Fact]
    public async Task Append_without_idempotency_key_returns_400_IDEMPOTENCY_KEY_MISSING()
    {
        var client = AdminClient();

        var response = await client.PostAsJsonAsync("/api/v1/audit", ValidBody());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("IDEMPOTENCY_KEY_MISSING");
    }

    [Fact]
    public async Task Append_happy_path_returns_201_with_Location_and_no_replay_header()
    {
        var client = AdminClient();
        var key = Guid.NewGuid().ToString();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/audit")
        {
            Content = JsonContent.Create(ValidBody()),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Source-Service", "nexus-patient");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Contains("X-Idempotent-Replay").Should().BeFalse();
        response.Headers.Location.Should().NotBeNull();
        var body = await response.Content.ReadFromJsonAsync<AuditEntry>();
        body.Should().NotBeNull();
        body!.SourceService.Should().Be("nexus-patient");
        body.IdempotencyKey.Should().Be(key);
    }

    [Fact]
    public async Task Append_replay_returns_200_with_X_Idempotent_Replay_true_and_same_entry()
    {
        var client = AdminClient();
        var key = Guid.NewGuid().ToString();
        var body = ValidBody("replay-summary");

        var first = await client.SendAsync(BuildPost(body, key, "nexus-patient"));
        var firstEntry = await first.Content.ReadFromJsonAsync<AuditEntry>();

        var second = await client.SendAsync(BuildPost(body, key, "nexus-patient"));

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Headers.GetValues("X-Idempotent-Replay").Single().Should().Be("true");
        var secondEntry = await second.Content.ReadFromJsonAsync<AuditEntry>();
        secondEntry!.Id.Should().Be(firstEntry!.Id);
    }

    [Fact]
    public async Task Append_conflict_returns_409_IDEMPOTENCY_CONFLICT()
    {
        var client = AdminClient();
        var key = Guid.NewGuid().ToString();

        var first = await client.SendAsync(BuildPost(ValidBody("first"), key, "nexus-patient"));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var conflict = await client.SendAsync(BuildPost(ValidBody("second"), key, "nexus-patient"));

        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("IDEMPOTENCY_CONFLICT");
    }

    [Fact]
    public async Task Append_validation_error_returns_422_AUDIT_VALIDATION()
    {
        var client = AdminClient();
        var future = ValidBody() with { OccurredAtUtc = DateTime.UtcNow.AddDays(1) };

        var response = await client.SendAsync(BuildPost(future, Guid.NewGuid().ToString(), "nexus-patient"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("AUDIT_VALIDATION");
    }

    [Fact]
    public async Task GetById_returns_404_for_unknown_id()
    {
        var client = AdminClient();

        var response = await client.GetAsync($"/api/v1/audit/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Query_returns_paged_list_filtered_by_source_service()
    {
        var client = AdminClient();
        await client.SendAsync(BuildPost(ValidBody("q-a"), Guid.NewGuid().ToString(), "nexus-patient"));
        await client.SendAsync(BuildPost(ValidBody("q-b"), Guid.NewGuid().ToString(), "nexus-doctor"));

        var response = await client.GetAsync("/api/v1/audit?sourceService=nexus-patient&size=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedAuditEntries>();
        paged.Should().NotBeNull();
        paged!.Items.Should().OnlyContain(i => i.SourceService == "nexus-patient");
    }

    [Fact]
    public async Task SwaggerJson_exposes_all_three_operationIds()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        var raw = doc.GetRawText();
        raw.Should().Contain("appendAuditEntry");
        raw.Should().Contain("queryAuditEntries");
        raw.Should().Contain("getAuditEntryById");
    }

    private static HttpRequestMessage BuildPost(AppendAuditEntryRequest body, string idempotencyKey, string sourceService)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/audit")
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        req.Headers.Add("X-Source-Service", sourceService);
        return req;
    }
}

public sealed class AuditEndpointsFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"audit-itests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = string.Empty,
                ["Persistence:AutoMigrate"] = "false",
                ["Persistence:FailFast"] = "false",
                ["Auth:Authority"] = "http://identity.test",
                ["Auth:Audience"] = "nexus-ha",
                ["Auth:Issuer"] = "https://api.nexusha.local/identity",
            });
        });
        builder.ConfigureTestServices(services =>
        {
            // Strip any EF registrations Production wiring may have added (incl.
            // a SqlServer provider via appsettings.Development.json), then attach
            // an isolated InMemory provider for this test class.
            var efDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AuditDbContext>)
                         || d.ServiceType == typeof(DbContextOptions)
                         || d.ServiceType == typeof(AuditDbContext)
                         || (d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") ?? false))
                .ToList();
            foreach (var d in efDescriptors)
            {
                services.Remove(d);
            }
            services.AddDbContext<AuditDbContext>(o => o.UseInMemoryDatabase(_dbName));

            // Replace JWT signature validation with a deterministic test shim.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var auth = ctx.Request.Headers["Authorization"].ToString();
                        if (string.IsNullOrEmpty(auth))
                        {
                            ctx.NoResult();
                            return Task.CompletedTask;
                        }
                        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            ctx.Fail("not bearer");
                            return Task.CompletedTask;
                        }

                        var marker = auth["Bearer ".Length..].Trim();
                        var claims = new List<Claim>
                        {
                            new("sub", Guid.NewGuid().ToString()),
                            new("preferred_username", "alice"),
                            new("role", string.Equals(marker, "admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "User"),
                        };
                        var identity = new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme, "preferred_username", "role");
                        ctx.Principal = new System.Security.Claims.ClaimsPrincipal(identity);
                        ctx.Success();
                        return Task.CompletedTask;
                    },
                };
            });
        });
    }
}
