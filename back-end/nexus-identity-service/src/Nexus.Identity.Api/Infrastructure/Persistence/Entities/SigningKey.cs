namespace Nexus.Identity.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// RSA signing key used to sign JWTs. Active and recently-retired keys are exposed
/// via the JWKS endpoint so existing tokens validate during rotation.
/// </summary>
public class SigningKey
{
    /// <summary>Key id (the <c>kid</c> claim).</summary>
    public string Kid { get; set; } = string.Empty;

    /// <summary>JOSE algorithm identifier, e.g. <c>RS256</c>.</summary>
    public string Algorithm { get; set; } = "RS256";

    /// <summary>Public JWK as JSON; published verbatim from the JWKS endpoint.</summary>
    public string PublicJwkJson { get; set; } = string.Empty;

    /// <summary>DPAPI/AES-encrypted PEM of the private key.</summary>
    public byte[] PrivateKeyPemEncrypted { get; set; } = Array.Empty<byte>();

    /// <summary>UTC timestamp the key material was generated.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>UTC timestamp the key became the active signer.</summary>
    public DateTime ActivatedAtUtc { get; set; }

    /// <summary>UTC timestamp the key was retired (no longer signs, still publishes).</summary>
    public DateTime? RetiredAtUtc { get; set; }
}
