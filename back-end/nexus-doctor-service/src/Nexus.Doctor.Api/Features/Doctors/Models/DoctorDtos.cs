using System.Text.Json.Serialization;

namespace Nexus.Doctors.Api.Features.Doctors.Models;

/// <summary>Wire shape returned by Doctor endpoints (matches <c>specs/doctors.openapi.json</c>).</summary>
public sealed class DoctorDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("code")] public string Code { get; init; } = string.Empty;
    [JsonPropertyName("firstName")] public string FirstName { get; init; } = string.Empty;
    [JsonPropertyName("lastName")] public string LastName { get; init; } = string.Empty;
    [JsonPropertyName("specialty")] public string Specialty { get; init; } = string.Empty;
    [JsonPropertyName("licenseNumber")] public string LicenseNumber { get; init; } = string.Empty;
    [JsonPropertyName("primaryPhone")] public string? PrimaryPhone { get; init; }
    [JsonPropertyName("email")] public string? Email { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = "Pending";
    [JsonPropertyName("primaryBranchId")] public Guid PrimaryBranchId { get; init; }
    [JsonPropertyName("createdAtUtc")] public DateTime CreatedAtUtc { get; init; }
    [JsonPropertyName("updatedAtUtc")] public DateTime UpdatedAtUtc { get; init; }

    public static DoctorDto From(Doctor d) => new()
    {
        Id = d.Id,
        Code = d.PublicCode,
        FirstName = d.FirstName,
        LastName = d.LastName,
        Specialty = d.Specialisation,
        LicenseNumber = d.LicenseNumber,
        PrimaryPhone = d.Phone,
        Email = d.Email,
        Status = d.Status.ToString(),
        PrimaryBranchId = d.PrimaryBranchId,
        CreatedAtUtc = d.CreatedAtUtc,
        UpdatedAtUtc = d.UpdatedAtUtc,
    };
}

/// <summary>POST body for <c>createDoctor</c>.</summary>
public sealed class DoctorCreateRequest
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Specialty { get; init; } = string.Empty;
    public string LicenseNumber { get; init; } = string.Empty;
    public string PrimaryPhone { get; init; } = string.Empty;
    public string? Email { get; init; }
    public Guid PrimaryBranchId { get; init; }
}

/// <summary>PATCH body for <c>patchDoctor</c>. License is immutable past Verified (enforced in service).</summary>
public sealed class DoctorPatchRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Specialty { get; init; }
    public string? PrimaryPhone { get; init; }
    public string? Email { get; init; }
    public Guid? PrimaryBranchId { get; init; }
}

/// <summary>Body for <c>linkDoctorToBranch</c>.</summary>
public sealed class DoctorBranchLinkRequest
{
    public Guid BranchId { get; init; }
    public bool IsPrimary { get; init; }
}

/// <summary>Wire shape returned by branch-link endpoints.</summary>
public sealed class DoctorBranchLinkDto
{
    [JsonPropertyName("branchId")] public Guid BranchId { get; init; }
    [JsonPropertyName("branchCode")] public string? BranchCode { get; init; }
    [JsonPropertyName("isPrimary")] public bool IsPrimary { get; init; }
    [JsonPropertyName("linkedAtUtc")] public DateTime LinkedAtUtc { get; init; }
}

/// <summary>Paged envelope for <c>listDoctors</c>.</summary>
public sealed class PagedDoctorResponse
{
    [JsonPropertyName("page")] public int Page { get; init; }
    [JsonPropertyName("size")] public int Size { get; init; }
    [JsonPropertyName("totalCount")] public long TotalCount { get; init; }
    [JsonPropertyName("items")] public IReadOnlyList<DoctorDto> Items { get; init; } = Array.Empty<DoctorDto>();
}
