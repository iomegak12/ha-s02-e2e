using Microsoft.AspNetCore.Mvc;

namespace Nexus.Web.Host.Controllers;

[ApiController]
[Route("bff/health")]
public class ServiceHealthController : ControllerBase
{
    private readonly IHttpClientFactory _clientFactory;
    private static readonly string[] ServiceNames = ["Identity", "Patients", "Doctors", "Branches", "Audit"];

    public ServiceHealthController(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    /// <summary>GET bff/health/services — aggregates /health from every downstream service.</summary>
    [HttpGet("services")]
    public async Task<IActionResult> GetServicesHealth(CancellationToken ct)
    {
        var tasks = ServiceNames.Select(async name =>
        {
            var status = "offline";
            try
            {
                var client = _clientFactory.CreateClient(name);
                using var response = await client.GetAsync("/health", ct);
                status = response.IsSuccessStatusCode    ? "healthy"
                       : (int)response.StatusCode < 500  ? "degraded"
                                                          : "offline";
            }
            catch
            {
                status = "offline";
            }

            return new { service = name, status };
        });

        var results = await Task.WhenAll(tasks);
        return Ok(results);
    }
}
