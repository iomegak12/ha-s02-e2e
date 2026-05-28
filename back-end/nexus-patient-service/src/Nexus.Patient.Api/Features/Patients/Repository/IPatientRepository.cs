using Nexus.Patients.Api.Features.Patients.Models;
using Nexus.Patients.Api.Infrastructure.Pagination;

namespace Nexus.Patients.Api.Features.Patients.Repository;

/// <summary>Data access for <see cref="Patient"/>. Feature surface in Phase 5 wraps these.</summary>
public interface IPatientRepository
{
    Task<Patient> AddAsync(Patient entity, CancellationToken ct);
    Task<Patient?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Patient?> GetByIdNoTrackingAsync(Guid id, CancellationToken ct);
    Task<Patient?> FindByPhoneDobAsync(string phone, DateOnly dateOfBirth, CancellationToken ct);
    Task<Patient?> FindByEmailDobAsync(string email, DateOnly dateOfBirth, CancellationToken ct);
    Task<List<PatientBranchLink>> GetActiveLinksAsync(Guid patientId, CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
    Task<PagedResult<Patient>> QueryAsync(PatientQuery query, CancellationToken ct);
}

/// <summary>Filter / paging shape for the list endpoint.</summary>
public sealed record PatientQuery(
    PatientStatus? Status = null,
    Guid? BranchId = null,
    string? Search = null,
    int Page = 1,
    int Size = 20);
