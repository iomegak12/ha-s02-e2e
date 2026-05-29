using System.Text.Json.Serialization;

namespace Nexus.Web.Client.Models.Common;

/// <summary>
/// Standard paged result envelope returned by all list endpoints.
/// </summary>
public sealed class PagedResult<T>
{
    [JsonPropertyName("items")]       public List<T> Items      { get; init; } = [];
    [JsonPropertyName("totalCount")]  public int     TotalCount { get; init; }
    [JsonPropertyName("page")]        public int     Page       { get; init; }
    [JsonPropertyName("size")]        public int     Size       { get; init; }

    public int TotalPages => Size > 0 ? (int)Math.Ceiling((double)TotalCount / Size) : 0;
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}
