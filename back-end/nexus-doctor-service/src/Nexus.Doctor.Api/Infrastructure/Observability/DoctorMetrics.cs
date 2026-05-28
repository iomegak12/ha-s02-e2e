using System.Diagnostics.Metrics;

namespace Nexus.Doctors.Api.Infrastructure.Observability;

/// <summary>Custom metrics emitted by the Doctor service.</summary>
public static class DoctorMetrics
{
    public const string MeterName = "Nexus.Doctors";

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>Total doctors created.</summary>
    public static readonly Counter<long> DoctorsCreated = Meter.CreateCounter<long>(
        "nexus_doctors_created_total",
        unit: "{doctors}",
        description: "Total doctors created.");

    /// <summary>Lifecycle state transitions, tagged by from / to.</summary>
    public static readonly Counter<long> StateTransitions = Meter.CreateCounter<long>(
        "nexus_doctor_state_transitions_total",
        unit: "{transitions}",
        description: "Doctor lifecycle transitions, partitioned by from/to.");

    /// <summary>Branch links established.</summary>
    public static readonly Counter<long> BranchesLinked = Meter.CreateCounter<long>(
        "nexus_doctor_branches_linked_total",
        unit: "{links}",
        description: "Doctor ↔ branch link operations.");

    /// <summary>Documents uploaded, tagged by type and result (Phase 7).</summary>
    public static readonly Counter<long> DocumentsUploaded = Meter.CreateCounter<long>(
        "nexus_doctor_documents_uploaded_total",
        unit: "{documents}",
        description: "Doctor verification documents uploaded.");

    /// <summary>Documents reviewed (Verified or Rejected), tagged by decision (Phase 7).</summary>
    public static readonly Counter<long> DocumentsReviewed = Meter.CreateCounter<long>(
        "nexus_doctor_documents_reviewed_total",
        unit: "{documents}",
        description: "Doctor documents reviewed.");
}
