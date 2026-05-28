using System.Text.Json.Serialization;

namespace Nexus.Identity.Api.Features.Auth.Models;

/// <summary>
/// One RSA public key in JWK form, as returned by <c>/.well-known/jwks.json</c>.
/// </summary>
public sealed class JwkKey
{
    /// <summary>Key type. Always <c>RSA</c>.</summary>
    [JsonPropertyName("kty")]
    public string Kty { get; init; } = "RSA";

    /// <summary>Public-key use. Always <c>sig</c>.</summary>
    [JsonPropertyName("use")]
    public string Use { get; init; } = "sig";

    /// <summary>JOSE algorithm. Always <c>RS256</c>.</summary>
    [JsonPropertyName("alg")]
    public string Alg { get; init; } = "RS256";

    /// <summary>Key id (matches the <c>kid</c> JWT header).</summary>
    [JsonPropertyName("kid")]
    public string Kid { get; init; } = string.Empty;

    /// <summary>RSA modulus, base64url-encoded.</summary>
    [JsonPropertyName("n")]
    public string N { get; init; } = string.Empty;

    /// <summary>RSA public exponent, base64url-encoded.</summary>
    [JsonPropertyName("e")]
    public string E { get; init; } = string.Empty;
}

/// <summary>JWKS response envelope.</summary>
public sealed class JwksResponse
{
    /// <summary>Currently-published keys.</summary>
    [JsonPropertyName("keys")]
    public IReadOnlyList<JwkKey> Keys { get; init; } = Array.Empty<JwkKey>();
}
