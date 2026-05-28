using System.Text.Json.Serialization;

namespace Nexus.Identity.Api.Features.Admins.Models;

/// <summary>
/// Paged response envelope for <c>GET /api/v1/admins</c>, matching the OpenAPI <c>PagedAdmin</c> schema.
/// </summary>
public sealed class PagedAdminResponse
{
    /// <summary>1-based page number.</summary>
    [JsonPropertyName("page")]
    public int Page { get; init; }

    /// <summary>Requested page size.</summary>
    [JsonPropertyName("size")]
    public int Size { get; init; }

    /// <summary>Total matching rows.</summary>
    [JsonPropertyName("totalCount")]
    public long TotalCount { get; init; }

    /// <summary>Page contents.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<AdminDto> Items { get; init; } = Array.Empty<AdminDto>();
}
