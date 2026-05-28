using System.Text.Json.Serialization;

namespace Nexus.Patients.Api.Features.Patients.Models;

/// <summary>Wire shape returned by Patient endpoints (matches <c>specs/patients.openapi.json</c>).</summary>
public sealed class PatientDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("code")] public string Code { get; init; } = string.Empty;
    [JsonPropertyName("firstName")] public string FirstName { get; init; } = string.Empty;
    [JsonPropertyName("lastName")] public string LastName { get; init; } = string.Empty;
    [JsonPropertyName("dateOfBirth")] public string DateOfBirth { get; init; } = string.Empty;
    [JsonPropertyName("gender")] public string Gender { get; init; } = string.Empty;
    [JsonPropertyName("primaryPhone")] public string? PrimaryPhone { get; init; }
    [JsonPropertyName("email")] public string? Email { get; init; }
    [JsonPropertyName("addressLine1")] public string? AddressLine1 { get; init; }
    [JsonPropertyName("addressLine2")] public string? AddressLine2 { get; init; }
    [JsonPropertyName("city")] public string? City { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("postalCode")] public string? PostalCode { get; init; }
    [JsonPropertyName("country")] public string? Country { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = "Draft";
    [JsonPropertyName("primaryBranchId")] public Guid PrimaryBranchId { get; init; }
    [JsonPropertyName("createdAtUtc")] public DateTime CreatedAtUtc { get; init; }
    [JsonPropertyName("updatedAtUtc")] public DateTime UpdatedAtUtc { get; init; }

    public static PatientDto From(Patient p) => new()
    {
        Id = p.Id,
        Code = p.PublicCode,
        FirstName = p.FirstName,
        LastName = p.LastName,
        DateOfBirth = p.DateOfBirth.ToString("yyyy-MM-dd"),
        Gender = p.Gender,
        PrimaryPhone = p.Phone,
        Email = p.Email,
        AddressLine1 = p.AddressLine1,
        AddressLine2 = p.AddressLine2,
        City = p.City,
        State = p.State,
        PostalCode = p.PostalCode,
        Country = p.Country,
        Status = p.Status.ToString(),
        PrimaryBranchId = p.PrimaryBranchId,
        CreatedAtUtc = p.CreatedAtUtc,
        UpdatedAtUtc = p.UpdatedAtUtc,
    };
}

/// <summary>POST body for <c>createPatient</c>.</summary>
public sealed class PatientCreateRequest
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string DateOfBirth { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public string PrimaryPhone { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string PostalCode { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public Guid PrimaryBranchId { get; init; }
}

/// <summary>PATCH body for <c>patchPatient</c>. All fields optional.</summary>
public sealed class PatientPatchRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? PrimaryPhone { get; init; }
    public string? Email { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public Guid? PrimaryBranchId { get; init; }
}

/// <summary>Body for <c>linkPatientToBranch</c>.</summary>
public sealed class PatientBranchLinkRequest
{
    public Guid BranchId { get; init; }
    public bool IsPrimary { get; init; }
}

/// <summary>Wire shape returned by branch-link endpoints.</summary>
public sealed class PatientBranchLinkDto
{
    [JsonPropertyName("branchId")] public Guid BranchId { get; init; }
    [JsonPropertyName("branchCode")] public string? BranchCode { get; init; }
    [JsonPropertyName("isPrimary")] public bool IsPrimary { get; init; }
    [JsonPropertyName("linkedAtUtc")] public DateTime LinkedAtUtc { get; init; }
}

/// <summary>Paged envelope for <c>listPatients</c>.</summary>
public sealed class PagedPatientResponse
{
    [JsonPropertyName("page")] public int Page { get; init; }
    [JsonPropertyName("size")] public int Size { get; init; }
    [JsonPropertyName("totalCount")] public long TotalCount { get; init; }
    [JsonPropertyName("items")] public IReadOnlyList<PatientDto> Items { get; init; } = Array.Empty<PatientDto>();
}
