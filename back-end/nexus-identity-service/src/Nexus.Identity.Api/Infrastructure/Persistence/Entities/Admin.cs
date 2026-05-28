namespace Nexus.Identity.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// Hospital administrator account. Authenticates via username + password
/// (Argon2id hash stored in <see cref="PasswordHash"/>).
/// </summary>
public class Admin
{
    /// <summary>Stable identifier (SQL <c>uniqueidentifier</c>).</summary>
    public Guid Id { get; set; }

    /// <summary>Login handle; unique across the tenant.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Human-readable name shown in the admin UI.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Argon2id-encoded password hash.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Soft-delete flag. Deactivated admins cannot authenticate.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>UTC timestamp set when the row is created.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>UTC timestamp updated on every persisted mutation.</summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>SQL Server <c>rowversion</c> used for optimistic concurrency / ETag.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    /// <summary>Outstanding refresh tokens issued to this admin.</summary>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
