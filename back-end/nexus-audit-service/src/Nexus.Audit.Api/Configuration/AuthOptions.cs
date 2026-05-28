using System.ComponentModel.DataAnnotations;

namespace Nexus.Audit.Api.Configuration;

/// <summary>
/// JWT bearer settings for validating tokens issued by <c>nexus-identity-service</c>.
/// Keys live under the <c>Auth</c> section of configuration.
/// </summary>
public class AuthOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Auth";

    /// <summary>Base URL of the Identity service (used to derive the JWKS URL by default).</summary>
    [Required]
    public string Authority { get; set; } = string.Empty;

    /// <summary>Expected <c>aud</c> claim.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Expected <c>iss</c> claim.</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit JWKS URL. When unset, defaults to
    /// <c>{Authority}/.well-known/jwks.json</c>.
    /// </summary>
    public string? JwksUrl { get; set; }

    /// <summary>Returns the effective JWKS URL (explicit or derived).</summary>
    public string ResolvedJwksUrl =>
        string.IsNullOrWhiteSpace(JwksUrl)
            ? $"{Authority.TrimEnd('/')}/.well-known/jwks.json"
            : JwksUrl!;
}
