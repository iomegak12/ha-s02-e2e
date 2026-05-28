using System.Text.Json.Serialization;

namespace Nexus.Branches.Api.Features.Branches.Models;

/// <summary>Wire shape returned by Branches endpoints (matches <c>specs/branches.openapi.json</c>).</summary>
public sealed class BranchDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("code")] public string Code { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("city")] public string City { get; init; } = string.Empty;
    [JsonPropertyName("isActive")] public bool IsActive { get; init; }
    [JsonPropertyName("createdAtUtc")] public DateTime CreatedAtUtc { get; init; }

    public static BranchDto From(Branch b) => new()
    {
        Id = b.Id,
        Code = b.Code,
        Name = b.Name,
        City = b.City ?? string.Empty,
        IsActive = b.IsActive,
        CreatedAtUtc = b.CreatedAtUtc,
    };
}

/// <summary>POST body for <c>createBranch</c>.</summary>
public sealed class BranchCreateRequest
{
    [JsonPropertyName("code")] public string Code { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("city")] public string City { get; init; } = string.Empty;
    [JsonPropertyName("isActive")] public bool? IsActive { get; init; }
}

/// <summary>PATCH body for <c>patchBranch</c>. <c>code</c> is immutable.</summary>
public sealed class BranchPatchRequest
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("city")] public string? City { get; init; }
    [JsonPropertyName("isActive")] public bool? IsActive { get; init; }
}

/// <summary>Paged response envelope returned by <c>listBranches</c>.</summary>
public sealed class PagedBranchResponse
{
    [JsonPropertyName("page")] public int Page { get; init; }
    [JsonPropertyName("size")] public int Size { get; init; }
    [JsonPropertyName("totalCount")] public long TotalCount { get; init; }
    [JsonPropertyName("items")] public IReadOnlyList<BranchDto> Items { get; init; } = Array.Empty<BranchDto>();
}
