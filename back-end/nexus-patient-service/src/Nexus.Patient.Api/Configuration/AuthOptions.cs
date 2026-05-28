using System.ComponentModel.DataAnnotations;

namespace Nexus.Patients.Api.Configuration;

/// <summary>JWT bearer settings for validating tokens issued by <c>nexus-identity-service</c>.</summary>
public class AuthOptions
{
    public const string SectionName = "Auth";

    [Required] public string Authority { get; set; } = string.Empty;
    [Required] public string Audience { get; set; } = string.Empty;
    [Required] public string Issuer { get; set; } = string.Empty;

    /// <summary>Optional explicit JWKS URL. Defaults to <c>{Authority}/.well-known/jwks.json</c>.</summary>
    public string? JwksUrl { get; set; }

    /// <summary>Effective JWKS URL.</summary>
    public string ResolvedJwksUrl =>
        string.IsNullOrWhiteSpace(JwksUrl)
            ? $"{Authority.TrimEnd('/')}/.well-known/jwks.json"
            : JwksUrl!;
}
