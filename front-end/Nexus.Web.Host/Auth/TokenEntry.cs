namespace Nexus.Web.Host.Auth;

public sealed record TokenEntry(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiry,
    DateTimeOffset RefreshTokenExpiry);
