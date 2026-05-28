using System.Security.Cryptography;
using Bogus;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;

namespace Nexus.Identity.Api.Tests.Fakers;

/// <summary>Bogus faker for <see cref="RefreshToken"/> entities used by tests.</summary>
public static class RefreshTokenFaker
{
    /// <summary>Create a populated active refresh token bound to <paramref name="adminId"/>.</summary>
    public static RefreshToken Create(Guid adminId, DateTime? issuedAt = null, int lifetimeDays = 14)
    {
        var issued = issuedAt ?? DateTime.UtcNow;
        var hashBuffer = new byte[32];
        RandomNumberGenerator.Fill(hashBuffer);

        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            AdminId = adminId,
            TokenHash = hashBuffer,
            IssuedAtUtc = issued,
            ExpiresAtUtc = issued.AddDays(lifetimeDays),
        };
    }
}
