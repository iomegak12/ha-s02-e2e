namespace Nexus.Identity.Api.Infrastructure.Security;

/// <summary>
/// Abstraction over password hashing/verification. Implementations MUST use a
/// memory- or work-factor-hard algorithm; plaintext storage is forbidden.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hash a plaintext password for at-rest storage.</summary>
    string Hash(string password);

    /// <summary>Verify a plaintext password against a previously-stored hash.</summary>
    bool Verify(string password, string hash);
}
