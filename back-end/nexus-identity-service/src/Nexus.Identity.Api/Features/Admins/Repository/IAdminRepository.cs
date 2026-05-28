using Nexus.Identity.Api.Infrastructure.Persistence.Entities;

namespace Nexus.Identity.Api.Features.Admins.Repository;

/// <summary>
/// Sort directives accepted by <see cref="IAdminRepository.ListAsync"/>.
/// </summary>
public enum AdminSortField
{
    /// <summary>Sort by <see cref="Admin.CreatedAtUtc"/>.</summary>
    CreatedAtUtc,

    /// <summary>Sort by <see cref="Admin.Username"/>.</summary>
    Username,

    /// <summary>Sort by <see cref="Admin.DisplayName"/>.</summary>
    DisplayName,
}

/// <summary>Persistence gateway for <see cref="Admin"/>.</summary>
public interface IAdminRepository
{
    /// <summary>Look up an admin by id.</summary>
    Task<Admin?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Look up an admin by username.</summary>
    Task<Admin?> GetByUsernameAsync(string username, CancellationToken cancellationToken);

    /// <summary>Return whether an admin with the given username already exists.</summary>
    Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken);

    /// <summary>Paged list of admins.</summary>
    Task<(IReadOnlyList<Admin> Items, long Total)> ListAsync(
        int page,
        int size,
        bool? isActive,
        AdminSortField sort,
        bool descending,
        CancellationToken cancellationToken);

    /// <summary>Stage a new admin for insert.</summary>
    Task AddAsync(Admin admin, CancellationToken cancellationToken);

    /// <summary>Persist staged changes.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
