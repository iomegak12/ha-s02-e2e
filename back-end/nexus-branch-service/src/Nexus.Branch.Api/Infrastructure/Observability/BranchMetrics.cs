using System.Diagnostics.Metrics;

namespace Nexus.Branches.Api.Infrastructure.Observability;

/// <summary>Custom metrics emitted by the Branch service.</summary>
public static class BranchMetrics
{
    public const string MeterName = "Nexus.Branches";

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>Total branches created, tagged by source service.</summary>
    public static readonly Counter<long> BranchesCreated = Meter.CreateCounter<long>(
        "nexus_branches_created_total",
        unit: "{branches}",
        description: "Total branches created.");

    /// <summary>Total branches updated, tagged by source service.</summary>
    public static readonly Counter<long> BranchesUpdated = Meter.CreateCounter<long>(
        "nexus_branches_updated_total",
        unit: "{branches}",
        description: "Total branches updated.");

    /// <summary>Total branches deactivated.</summary>
    public static readonly Counter<long> BranchesDeactivated = Meter.CreateCounter<long>(
        "nexus_branches_deactivated_total",
        unit: "{branches}",
        description: "Total branches deactivated.");
}
