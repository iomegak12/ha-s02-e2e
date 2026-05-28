using System.ComponentModel.DataAnnotations;

namespace Nexus.Identity.Api.Configuration;

/// <summary>
/// JWT issuance configuration: issuer/audience, lifetimes, and signing-key store options.
/// Bound from the <c>JwtIssuer</c> configuration section.
/// </summary>
public sealed class JwtIssuerOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "JwtIssuer";

    /// <summary>Token <c>iss</c> claim and JWKS issuer URL.</summary>
    [Required]
    public string Issuer { get; init; } = "https://api.nexusha.local/identity";

    /// <summary>Token <c>aud</c> claim.</summary>
    [Required]
    public string Audience { get; init; } = "nexus-ha";

    /// <summary>Access token lifetime in minutes.</summary>
    [Range(1, 1440)]
    public int AccessTokenLifetimeMinutes { get; init; } = 15;

    /// <summary>Refresh token lifetime in days.</summary>
    [Range(1, 365)]
    public int RefreshTokenLifetimeDays { get; init; } = 14;

    /// <summary>Signing-key rotation cadence in days.</summary>
    [Range(1, 365)]
    public int RotationDays { get; init; } = 30;

    /// <summary>Signing-key store backend: <c>Sql</c> or <c>File</c>.</summary>
    [Required]
    public string SigningKeyStore { get; init; } = "Sql";
}
