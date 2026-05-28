namespace Nexus.Doctors.Api.Infrastructure.Persistence;

/// <summary>Marker for entities whose CreatedAt/UpdatedAt timestamps are stamped automatically.</summary>
public interface IAuditedEntity
{
    DateTime CreatedAtUtc { get; set; }
    DateTime UpdatedAtUtc { get; set; }
}
