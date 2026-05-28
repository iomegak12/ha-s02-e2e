using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nexus.Identity.Api.Configuration;
using Nexus.Identity.Api.Features.Auth.Service;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;
using Nexus.Identity.Api.Tests.Fakers;

namespace Nexus.Identity.Api.Tests.Features.Auth.Service;

public class JwtIssuerTests
{
    private static (SigningKey Key, RSA Rsa) NewRsaSigningKey()
    {
        var rsa = RSA.Create(2048);
        var pem = rsa.ExportRSAPrivateKeyPem();
        var key = new SigningKey
        {
            Kid = Guid.NewGuid().ToString("N"),
            Algorithm = "RS256",
            PrivateKeyPemEncrypted = Encoding.UTF8.GetBytes(pem),
            PublicJwkJson = "{}",
            CreatedAtUtc = DateTime.UtcNow,
            ActivatedAtUtc = DateTime.UtcNow,
        };
        return (key, rsa);
    }

    private static IOptionsMonitor<JwtIssuerOptions> NewOptions()
    {
        var monitor = Substitute.For<IOptionsMonitor<JwtIssuerOptions>>();
        monitor.CurrentValue.Returns(new JwtIssuerOptions
        {
            Issuer = "https://api.nexusha.local/identity",
            Audience = "nexus-ha",
            AccessTokenLifetimeMinutes = 15,
        });
        return monitor;
    }

    [Fact]
    public async Task IssueAsync_produces_RS256_JWT_with_expected_claims()
    {
        var (key, rsa) = NewRsaSigningKey();
        using var _ = rsa;

        var store = Substitute.For<ISigningKeyStore>();
        store.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(key);

        var sut = new JwtIssuer(store, NewOptions(), TimeProvider.System);
        var admin = AdminFaker.Create();
        admin.Username = "asha.menon";
        admin.DisplayName = "Asha Menon";

        var (token, expiresIn) = await sut.IssueAsync(admin, CancellationToken.None);

        token.Should().NotBeNullOrWhiteSpace();
        expiresIn.Should().Be(15 * 60);

        var jwt = new JsonWebToken(token);
        jwt.Issuer.Should().Be("https://api.nexusha.local/identity");
        jwt.Audiences.Should().Contain("nexus-ha");
        jwt.Subject.Should().Be(admin.Id.ToString());
        jwt.GetClaim("preferred_username").Value.Should().Be("asha.menon");
        jwt.GetClaim("name").Value.Should().Be("Asha Menon");
        jwt.GetClaim("role").Value.Should().Be("Admin");
        jwt.GetClaim("jti").Value.Should().NotBeNullOrWhiteSpace();
        jwt.Alg.Should().Be(SecurityAlgorithms.RsaSha256);
        jwt.Kid.Should().Be(key.Kid);
    }

    [Fact]
    public async Task IssueAsync_token_is_signature_valid_with_public_key()
    {
        var (key, rsa) = NewRsaSigningKey();
        using var _ = rsa;

        var store = Substitute.For<ISigningKeyStore>();
        store.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(key);

        var sut = new JwtIssuer(store, NewOptions(), TimeProvider.System);
        var (token, _) = await sut.IssueAsync(AdminFaker.Create(), CancellationToken.None);

        var publicKey = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = key.Kid };
        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "https://api.nexusha.local/identity",
            ValidAudience = "nexus-ha",
            IssuerSigningKey = publicKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        });

        result.IsValid.Should().BeTrue(result.Exception?.Message);
    }
}
