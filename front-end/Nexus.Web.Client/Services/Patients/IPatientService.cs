using Nexus.Web.Client.Models.Common;
using Nexus.Web.Client.Models.Patients;

namespace Nexus.Web.Client.Services.Patients;

public interface IPatientService
{
    Task<ApiResponse<PagedResult<PatientModel>>> ListAsync(
        int page = 1,
        int size = 20,
        string? status = null,
        string? branchId = null,
        string? q = null,
        CancellationToken ct = default);

    Task<ApiResponse<PatientModel>> GetByIdAsync(string id, CancellationToken ct = default);

    Task<ApiResponse<PatientModel>> CreateAsync(PatientCreateRequest request, CancellationToken ct = default);

    Task<ApiResponse<PatientModel>> PatchAsync(string id, PatientPatchRequest request, CancellationToken ct = default);

    /// <summary>Transitions patient from Draft → Active.</summary>
    Task<ApiResponse<PatientModel>> ActivateAsync(string id, CancellationToken ct = default);

    /// <summary>Transitions patient from Active → Archived.</summary>
    Task<ApiResponse<PatientModel>> ArchiveAsync(string id, CancellationToken ct = default);
}
