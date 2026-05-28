using Nexus.IntegrationTests.Infrastructure;

namespace Nexus.IntegrationTests.Identity;

[Collection("stack")]
public sealed class HealthTests
{
    private readonly StackFixture _fx;
    public HealthTests(StackFixture fx) => _fx = fx;

    [Fact]
    public async Task Health_live_returns_200()
    {
        using var http = _fx.Client(_fx.Settings.IdentityBaseUrl);
        var response = await http.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_ready_returns_200()
    {
        using var http = _fx.Client(_fx.Settings.IdentityBaseUrl);
        var response = await http.GetAsync("/health/ready");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
