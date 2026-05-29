using System.Text.Json.Serialization;

namespace Nexus.Web.Client.Models.Auth;

public sealed class SessionInfo
{
    [JsonPropertyName("id")]            public string         Id            { get; init; } = "";
    [JsonPropertyName("username")]      public string         Username      { get; init; } = "";
    [JsonPropertyName("role")]          public string         Role          { get; init; } = "";
    [JsonPropertyName("sessionExpiry")] public DateTimeOffset SessionExpiry { get; init; }
}
