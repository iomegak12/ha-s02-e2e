using System.ComponentModel.DataAnnotations;

namespace Nexus.Branches.Api.Configuration;

/// <summary>Configuration for the outbound audit-publishing client.</summary>
public sealed class AuditClientOptions
{
    public const string SectionName = "AuditClient";

    /// <summary>Base URL of <c>nexus-audit-service</c>.</summary>
    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Master toggle. When false, audit publishing is replaced by <c>NullAuditPublisher</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Lower-cased source-service identifier sent as <c>X-Source-Service</c>.</summary>
    public string SourceService { get; set; } = "nexus-branch";

    /// <summary>How often the reconciler worker scans the outbox (seconds).</summary>
    [Range(5, 3600)]
    public int ReconcilerIntervalSeconds { get; set; } = 30;

    /// <summary>How many rows the reconciler processes per pass.</summary>
    [Range(1, 1000)]
    public int ReconcilerBatchSize { get; set; } = 50;
}
