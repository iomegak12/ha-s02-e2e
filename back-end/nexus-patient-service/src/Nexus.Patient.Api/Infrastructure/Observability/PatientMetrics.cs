using System.Diagnostics.Metrics;

namespace Nexus.Patients.Api.Infrastructure.Observability;

/// <summary>Custom metrics emitted by the Patient service.</summary>
public static class PatientMetrics
{
    public const string MeterName = "Nexus.Patients";

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>Total patients created, tagged by source service.</summary>
    public static readonly Counter<long> PatientsCreated = Meter.CreateCounter<long>(
        "nexus_patients_created_total",
        unit: "{patients}",
        description: "Total patients created.");

    /// <summary>Lifecycle state transitions, tagged by from / to.</summary>
    public static readonly Counter<long> StateTransitions = Meter.CreateCounter<long>(
        "nexus_patient_state_transitions_total",
        unit: "{transitions}",
        description: "Patient lifecycle transitions, partitioned by from/to.");

    /// <summary>Branch links established.</summary>
    public static readonly Counter<long> BranchesLinked = Meter.CreateCounter<long>(
        "nexus_patient_branches_linked_total",
        unit: "{links}",
        description: "Patient ↔ branch link operations.");

    /// <summary>Patient purged by the 30-day worker (Phase 8).</summary>
    public static readonly Counter<long> PatientsPurged = Meter.CreateCounter<long>(
        "nexus_patients_purged_total",
        unit: "{patients}",
        description: "Archived patients hard-deleted by the purge worker.");
}
