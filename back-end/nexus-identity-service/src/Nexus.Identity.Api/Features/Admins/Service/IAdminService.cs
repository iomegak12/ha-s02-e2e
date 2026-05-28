using Nexus.Identity.Api.Features.Admins.Models;

namespace Nexus.Identity.Api.Features.Admins.Service;

/// <summary>Application-layer operations for managing admin accounts.</summary>
public interface IAdminService
{
    /// <summary>List admins with optional active filter and sort.</summary>
    Task<PagedAdminResponse> ListAsync(int page, int size, bool? isActive, string? sort, CancellationToken cancellationToken);

    /// <summary>Fetch a single admin and its ETag (base64 row version).</summary>
    Task<(AdminDto Admin, string ETag)?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Create a new admin. Throws on duplicate username.</summary>
    Task<(AdminDto Admin, string ETag)> CreateAsync(AdminCreateRequest request, CancellationToken cancellationToken);

    /// <summary>Apply a partial update. Throws on missing id or ETag mismatch.</summary>
    Task<(AdminDto Admin, string ETag)> PatchAsync(Guid id, AdminPatchRequest request, string? ifMatch, CancellationToken cancellationToken);

    /// <summary>Idempotent soft delete (sets <c>IsActive=false</c>). Throws when the id is unknown.</summary>
    Task DeactivateAsync(Guid id, CancellationToken cancellationToken);
}
