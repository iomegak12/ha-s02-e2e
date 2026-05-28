using System.ComponentModel.DataAnnotations;

namespace Nexus.Patients.Api.Configuration;

/// <summary>Bound from <c>Branches:*</c> in <c>appsettings.json</c>.</summary>
public sealed class BranchesClientOptions
{
    public const string SectionName = "Branches";

    /// <summary>Base URL of <c>nexus-branch-service</c>.</summary>
    [Required] public string BaseUrl { get; set; } = "http://nexus-branch-service:11000";

    /// <summary>Per-attempt HTTP timeout.</summary>
    [Range(1, 60)] public int AttemptTimeoutSeconds { get; set; } = 5;

    /// <summary>Total request timeout (covers retries).</summary>
    [Range(1, 120)] public int TotalRequestTimeoutSeconds { get; set; } = 15;

    /// <summary>Number of retry attempts for idempotent GETs.</summary>
    [Range(0, 10)] public int MaxRetryAttempts { get; set; } = 3;
}
