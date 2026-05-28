namespace Nexus.Identity.Api.Infrastructure.Security;

/// <summary>
/// BCrypt-based <see cref="IPasswordHasher"/>. Work factor (cost) is fixed at 12 per LLD §10.
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    /// <inheritdoc />
    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, workFactor: WorkFactor);

    /// <inheritdoc />
    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
