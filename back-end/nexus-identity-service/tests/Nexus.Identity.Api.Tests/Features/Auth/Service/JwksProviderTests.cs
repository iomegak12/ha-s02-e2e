using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Identity.Api.Features.Auth.Service;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;

namespace Nexus.Identity.Api.Tests.Features.Auth.Service;

public class JwksProviderTests
{
    private static SigningKey MakeKey(string kid, string publicJwkJson) => new()
    {
        Kid = kid,
        Algorithm = "RS256",
        PrivateKeyPemEncrypted = Array.Empty<byte>(),
        PublicJwkJson = publicJwkJson,
        CreatedAtUtc = DateTime.UtcNow,
        ActivatedAtUtc = DateTime.UtcNow,
    };

    private static string ValidJwk(string kid) => JsonSerializer.Serialize(new
    {
        kty = "RSA",
        use = "sig",
        alg = "RS256",
        kid,
        n = "abc",
        e = "AQAB",
    });

    [Fact]
    public async Task GetAsync_returns_keys_when_all_valid()
    {
        var store = Substitute.For<ISigningKeyStore>();
        store.GetPublishedAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SigningKey> { MakeKey("kid-1", ValidJwk("kid-1")), MakeKey("kid-2", ValidJwk("kid-2")) });

        var sut = new JwksProvider(store, NullLogger<JwksProvider>.Instance);
        var result = await sut.GetAsync(CancellationToken.None);

        result.Keys.Should().HaveCount(2);
        result.Keys.Select(k => k.Kid).Should().BeEquivalentTo(new[] { "kid-1", "kid-2" });
    }

    [Fact]
    public async Task GetAsync_skips_malformed_jwk()
    {
        var store = Substitute.For<ISigningKeyStore>();
        store.GetPublishedAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SigningKey> { MakeKey("good", ValidJwk("good")), MakeKey("bad", "{not-json") });

        var sut = new JwksProvider(store, NullLogger<JwksProvider>.Instance);
        var result = await sut.GetAsync(CancellationToken.None);

        result.Keys.Should().HaveCount(1);
        result.Keys[0].Kid.Should().Be("good");
    }

    [Fact]
    public async Task GetAsync_empty_when_store_empty()
    {
        var store = Substitute.For<ISigningKeyStore>();
        store.GetPublishedAsync(Arg.Any<CancellationToken>()).Returns(new List<SigningKey>());

        var sut = new JwksProvider(store, NullLogger<JwksProvider>.Instance);
        var result = await sut.GetAsync(CancellationToken.None);

        result.Keys.Should().BeEmpty();
    }
}
