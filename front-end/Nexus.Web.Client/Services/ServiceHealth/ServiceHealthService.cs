using System.Net.Http.Json;
using Nexus.Web.Client.Models.ServiceHealth;

namespace Nexus.Web.Client.Services.ServiceHealth;

public sealed class ServiceHealthService : IServiceHealthService
{
    private readonly HttpClient _http;

    public ServiceHealthService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<ServiceHealthStatus>> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<ServiceHealthStatus>>(
                "/bff/health/services", ct);
            return result ?? [];
        }
        catch
        {
            return [];
        }
    }
}
