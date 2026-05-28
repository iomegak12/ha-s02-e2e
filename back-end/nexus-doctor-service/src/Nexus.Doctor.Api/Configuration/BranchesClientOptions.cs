using System.ComponentModel.DataAnnotations;

namespace Nexus.Doctors.Api.Configuration;

/// <summary>Bound from <c>Branches:*</c> in <c>appsettings.json</c>.</summary>
public sealed class BranchesClientOptions
{
    public const string SectionName = "Branches";

    [Required] public string BaseUrl { get; set; } = "http://nexus-branch-service:11000";
    [Range(1, 60)] public int AttemptTimeoutSeconds { get; set; } = 5;
    [Range(1, 120)] public int TotalRequestTimeoutSeconds { get; set; } = 15;
    [Range(0, 10)] public int MaxRetryAttempts { get; set; } = 3;
}
