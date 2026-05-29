using System.Text.Json.Serialization;

namespace Nexus.Web.Client.Models.Common;

/// <summary>
/// RFC 9457 Problem Details — matches the ProblemDetails schema used by all Nexus HA services.
/// </summary>
public sealed class ProblemDetails
{
    [JsonPropertyName("type")]     public string? Type     { get; init; }
    [JsonPropertyName("title")]    public string? Title    { get; init; }
    [JsonPropertyName("status")]   public int?    Status   { get; init; }
    [JsonPropertyName("detail")]   public string? Detail   { get; init; }
    [JsonPropertyName("code")]     public string? Code     { get; init; }
    [JsonPropertyName("instance")] public string? Instance { get; init; }
    [JsonPropertyName("traceId")]  public string? TraceId  { get; init; }

    [JsonExtensionData]
    public Dictionary<string, object?>? Extensions { get; init; }
}
