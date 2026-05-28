using System.Net;
using System.Net.Http.Headers;
using Nexus.Audit.Api.Tests.Features.Audit.Controller;

namespace Nexus.Audit.Api.Tests.Infrastructure.Auth;

/// <summary>
/// Integration tests for the <c>AdminOnly</c> policy. JWT signature validation is
/// short-circuited via the shared <see cref="AuditEndpointsFactory"/> so the
/// authorization layer can be exercised without a live Identity service.
/// </summary>
public sealed class AuthPolicyIntegrationTests : IClassFixture<AuditEndpointsFactory>
{
    private readonly AuditEndpointsFactory _factory;

    public AuthPolicyIntegrationTests(AuditEndpointsFactory factory) => _factory = factory;

    [Fact]
    public async Task Query_without_bearer_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/audit");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Query_with_non_admin_role_returns_403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "user");

        var response = await client.GetAsync("/api/v1/audit");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Health_live_is_unprotected_and_returns_200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

