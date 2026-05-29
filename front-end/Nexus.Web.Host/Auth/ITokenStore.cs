namespace Nexus.Web.Host.Auth;

/// <summary>
/// Stores JWT access + refresh tokens server-side, keyed by the opaque session ID.
/// The browser only ever holds the HttpOnly session cookie — never a raw JWT.
/// </summary>
public interface ITokenStore
{
    void Store(string sessionId, TokenEntry entry);
    TokenEntry? Get(string sessionId);
    void Remove(string sessionId);
}
