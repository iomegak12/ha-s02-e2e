using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.Identity.Api.Configuration;
using Nexus.Identity.Api.Features.Auth.Models;
using Nexus.Identity.Api.Features.Auth.Repository;
using Nexus.Identity.Api.Features.Auth.Service;
using Nexus.Identity.Api.Infrastructure.Errors;
using Nexus.Identity.Api.Infrastructure.Persistence;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;
using Nexus.Identity.Api.Infrastructure.Security;
using Nexus.Identity.Api.Tests.Fakers;

namespace Nexus.Identity.Api.Tests.Features.Auth.Service;

public class AuthServiceTests
{
    private readonly IRefreshTokenRepository _refreshRepo = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtIssuer _jwt = Substitute.For<IJwtIssuer>();
    private readonly IOptionsMonitor<JwtIssuerOptions> _opts = MakeOptions();
    private readonly TimeProvider _clock = TimeProvider.System;

    private static IOptionsMonitor<JwtIssuerOptions> MakeOptions()
    {
        var monitor = Substitute.For<IOptionsMonitor<JwtIssuerOptions>>();
        monitor.CurrentValue.Returns(new JwtIssuerOptions
        {
            Issuer = "https://api.nexusha.local/identity",
            Audience = "nexus-ha",
            AccessTokenLifetimeMinutes = 15,
            RefreshTokenLifetimeDays = 14,
            SigningKeyStore = "Sql",
        });
        return monitor;
    }

    private static IdentityDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"auth-{Guid.NewGuid()}")
            .Options;
        return new IdentityDbContext(options);
    }

    private AuthService NewSut(IdentityDbContext db)
    {
        _jwt.IssueAsync(Arg.Any<Admin>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(("access.jwt.token", 900)));
        return new AuthService(db, _refreshRepo, _hasher, _jwt, _opts, _clock, NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task IssueAsync_throws_INVALID_CREDENTIALS_when_user_missing()
    {
        await using var db = NewDb();
        var sut = NewSut(db);

        var act = () => sut.IssueAsync(new TokenRequest { Username = "ghost", Password = "x" }, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.InvalidCredentials);
        ex.Which.Status.Should().Be(401);
    }

    [Fact]
    public async Task IssueAsync_throws_INVALID_CREDENTIALS_when_password_wrong()
    {
        await using var db = NewDb();
        var admin = AdminFaker.Create();
        admin.Username = "asha";
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        _hasher.Verify("wrong", admin.PasswordHash).Returns(false);

        var sut = NewSut(db);
        var act = () => sut.IssueAsync(new TokenRequest { Username = "asha", Password = "wrong" }, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task IssueAsync_throws_when_admin_inactive()
    {
        await using var db = NewDb();
        var admin = AdminFaker.Create(isActive: false);
        admin.Username = "asha";
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var sut = NewSut(db);
        var act = () => sut.IssueAsync(new TokenRequest { Username = "asha", Password = "pw" }, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task IssueAsync_happy_path_returns_token_pair()
    {
        await using var db = NewDb();
        var admin = AdminFaker.Create();
        admin.Username = "asha";
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        _hasher.Verify("pw", admin.PasswordHash).Returns(true);

        var sut = NewSut(db);
        var response = await sut.IssueAsync(new TokenRequest { Username = "asha", Password = "pw" }, CancellationToken.None);

        response.AccessToken.Should().Be("access.jwt.token");
        response.TokenType.Should().Be("Bearer");
        response.ExpiresIn.Should().Be(900);
        response.RefreshToken.Should().NotBeNullOrWhiteSpace();

        await _refreshRepo.Received(1).AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
        await _refreshRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_throws_REFRESH_INVALID_when_unknown()
    {
        await using var db = NewDb();
        _refreshRepo.GetByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns((RefreshToken?)null);
        var sut = NewSut(db);

        var act = () => sut.RefreshAsync(new RefreshRequest { RefreshToken = "anything" }, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.RefreshInvalid);
    }

    [Fact]
    public async Task RefreshAsync_throws_when_token_revoked()
    {
        await using var db = NewDb();
        var admin = AdminFaker.Create();
        var rt = RefreshTokenFaker.Create(admin.Id);
        rt.RevokedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        rt.Admin = admin;
        _refreshRepo.GetByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(rt);
        var sut = NewSut(db);

        var act = () => sut.RefreshAsync(new RefreshRequest { RefreshToken = "anything" }, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.RefreshInvalid);
    }

    [Fact]
    public async Task RefreshAsync_throws_when_token_expired()
    {
        await using var db = NewDb();
        var admin = AdminFaker.Create();
        var rt = RefreshTokenFaker.Create(admin.Id, issuedAt: DateTime.UtcNow.AddDays(-30), lifetimeDays: 1);
        rt.Admin = admin;
        _refreshRepo.GetByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(rt);
        var sut = NewSut(db);

        var act = () => sut.RefreshAsync(new RefreshRequest { RefreshToken = "anything" }, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.RefreshInvalid);
    }

    [Fact]
    public async Task RefreshAsync_happy_path_rotates_token()
    {
        await using var db = NewDb();
        var admin = AdminFaker.Create();
        var rt = RefreshTokenFaker.Create(admin.Id);
        rt.Admin = admin;
        _refreshRepo.GetByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(rt);
        var sut = NewSut(db);

        var response = await sut.RefreshAsync(new RefreshRequest { RefreshToken = "anything" }, CancellationToken.None);

        response.AccessToken.Should().Be("access.jwt.token");
        rt.RevokedAtUtc.Should().NotBeNull("the old refresh token is rotated/revoked");
        rt.ReplacedByTokenId.Should().NotBeNull();
        await _refreshRepo.Received(1).AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_is_idempotent_on_unknown_token()
    {
        await using var db = NewDb();
        _refreshRepo.GetByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns((RefreshToken?)null);
        var sut = NewSut(db);

        await sut.RevokeAsync(new RefreshRequest { RefreshToken = "x" }, CancellationToken.None);

        await _refreshRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_is_idempotent_when_already_revoked()
    {
        await using var db = NewDb();
        var admin = AdminFaker.Create();
        var rt = RefreshTokenFaker.Create(admin.Id);
        rt.RevokedAtUtc = DateTime.UtcNow.AddMinutes(-1);
        _refreshRepo.GetByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(rt);
        var sut = NewSut(db);

        await sut.RevokeAsync(new RefreshRequest { RefreshToken = "x" }, CancellationToken.None);

        await _refreshRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_revokes_active_token()
    {
        await using var db = NewDb();
        var admin = AdminFaker.Create();
        var rt = RefreshTokenFaker.Create(admin.Id);
        _refreshRepo.GetByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(rt);
        var sut = NewSut(db);

        await sut.RevokeAsync(new RefreshRequest { RefreshToken = "x" }, CancellationToken.None);

        rt.RevokedAtUtc.Should().NotBeNull();
        await _refreshRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
