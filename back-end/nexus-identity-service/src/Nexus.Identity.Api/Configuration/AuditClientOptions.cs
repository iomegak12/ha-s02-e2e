using System.ComponentModel.DataAnnotations;

namespace Nexus.Identity.Api.Configuration;

/// <summary>
/// Audit service HTTP client configuration. Bound from <c>AuditClient</c>.
/// </summary>
public sealed class AuditClientOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "AuditClient";

    /// <summary>Base URL of the audit service.</summary>
    [Required]
    public string BaseUrl { get; init; } = "http://nexus-audit-service:12000";

    /// <summary>
    /// When <c>false</c>, the audit publisher and readiness probe are no-ops.
    /// Useful for local development before the audit service is built.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
