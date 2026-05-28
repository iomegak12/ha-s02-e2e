namespace Nexus.Identity.Api.Features.Admins.Models;

/// <summary>
/// Request body for <c>PATCH /api/v1/admins/{id}</c>. Partial update; username is immutable.
/// At least one field MUST be supplied.
/// </summary>
public sealed class AdminPatchRequest
{
    /// <summary>New display name, if changing.</summary>
    public string? DisplayName { get; init; }

    /// <summary>New active flag, if changing.</summary>
    public bool? IsActive { get; init; }
}
