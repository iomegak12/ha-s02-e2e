namespace Nexus.Web.Client.Models.ServiceHealth;

public sealed class ServiceHealthStatus
{
    public string Service { get; set; } = string.Empty;

    /// <summary>healthy | degraded | offline | unknown</summary>
    public string Status { get; set; } = "unknown";
}
