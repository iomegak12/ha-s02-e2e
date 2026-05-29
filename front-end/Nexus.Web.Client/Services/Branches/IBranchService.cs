using Nexus.Web.Client.Models.Branches;
using Nexus.Web.Client.Models.Common;

namespace Nexus.Web.Client.Services.Branches;

public interface IBranchService
{
    Task<ApiResponse<PagedResult<BranchModel>>> ListAsync(
        int page = 1,
        int size = 20,
        string? city = null,
        bool? isActive = null,
        CancellationToken ct = default);

    Task<ApiResponse<BranchModel>> GetByIdAsync(string id, CancellationToken ct = default);

    Task<ApiResponse<BranchModel>> CreateAsync(BranchCreateRequest request, CancellationToken ct = default);

    Task<ApiResponse<BranchModel>> PatchAsync(string id, BranchPatchRequest request, CancellationToken ct = default);

    Task<ApiResponse<bool>> DeactivateAsync(string id, CancellationToken ct = default);
}
