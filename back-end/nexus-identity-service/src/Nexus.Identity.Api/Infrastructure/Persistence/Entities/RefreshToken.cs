namespace Nexus.Identity.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// Single refresh token issued to an <see cref="Admin"/>. Stored as a SHA-256 hash;
/// the plaintext is only ever returned to the client at issue time.
/// </summary>
public class RefreshToken
{
    /// <summary>Stable identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning admin's id.</summary>
    public Guid AdminId { get; set; }

    /// <summary>SHA-256 hash (32 bytes) of the opaque refresh token.</summary>
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();

    /// <summary>UTC timestamp the token was issued.</summary>
    public DateTime IssuedAtUtc { get; set; }

    /// <summary>Absolute UTC expiry.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>UTC timestamp the token was revoked, if any.</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>If rotated, points at the replacement token.</summary>
    public Guid? ReplacedByTokenId { get; set; }

    /// <summary>Navigation back to the owning admin.</summary>
    public Admin? Admin { get; set; }
}
