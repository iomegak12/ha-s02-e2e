using Nexus.Doctors.Api.Features.Doctors.Models;
using Nexus.Doctors.Api.Infrastructure.Pagination;

namespace Nexus.Doctors.Api.Features.Doctors.Repository;

/// <summary>Data access for <see cref="Doctor"/>. Feature surface in Phase 6/7 wraps these.</summary>
public interface IDoctorRepository
{
    Task<Doctor> AddAsync(Doctor entity, CancellationToken ct);
    Task<Doctor?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Doctor?> GetByIdNoTrackingAsync(Guid id, CancellationToken ct);
    Task<Doctor?> FindActiveByLicenseAsync(string licenseNumber, CancellationToken ct);
    Task<List<DoctorBranchLink>> GetActiveLinksAsync(Guid doctorId, CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
    Task<PagedResult<Doctor>> QueryAsync(DoctorQuery query, CancellationToken ct);
}

/// <summary>Filter / paging shape consumed by <see cref="IDoctorRepository.QueryAsync"/>.</summary>
public sealed record DoctorQuery(
    DoctorStatus? Status = null,
    Guid? BranchId = null,
    string? Search = null,
    int Page = 1,
    int Size = 20);
