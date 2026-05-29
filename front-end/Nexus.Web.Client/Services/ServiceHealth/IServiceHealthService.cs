using Nexus.Web.Client.Models.ServiceHealth;

namespace Nexus.Web.Client.Services.ServiceHealth;

public interface IServiceHealthService
{
    Task<List<ServiceHealthStatus>> GetStatusAsync(CancellationToken ct = default);
}
