using Microsoft.AspNetCore.Http;
using Nexus.Identity.Api.Features.Admins.Models;
using Nexus.Identity.Api.Features.Admins.Repository;
using Nexus.Identity.Api.Features.Admins.Service;
using Nexus.Identity.Api.Infrastructure.Audit;
using Nexus.Identity.Api.Infrastructure.Errors;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;
using Nexus.Identity.Api.Infrastructure.Security;
using Nexus.Identity.Api.Tests.Fakers;

namespace Nexus.Identity.Api.Tests.Features.Admins.Service;

public class AdminServiceTests
{
    private readonly IAdminRepository _repo = Substitute.For<IAdminRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IAuditPublisher _audit = Substitute.For<IAuditPublisher>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly TimeProvider _clock = TimeProvider.System;

    private AdminService CreateSut() => new(_repo, _hasher, _clock, _audit, _httpContextAccessor);

    [Fact]
    public async Task CreateAsync_creates_admin_and_emits_Created_audit()
    {
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");
        _repo.ExistsByUsernameAsync("new.user", Arg.Any<CancellationToken>()).Returns(false);

        var sut = CreateSut();
        var (admin, etag) = await sut.CreateAsync(
            new AdminCreateRequest { Username = "new.user", DisplayName = "New User", Password = "S3cret!Pa55word" },
            CancellationToken.None);

        admin.Username.Should().Be("new.user");
        etag.Should().StartWith("\"").And.EndWith("\"");

        await _repo.Received(1).AddAsync(Arg.Is<Admin>(a => a.PasswordHash == "hashed" && a.IsActive), Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.Received(1).PublishAsync(
            Arg.Is<AuditEntryDto>(e => e.Action == "Created" && e.EntityType == "Admin" && e.EntityCode == "new.user"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_throws_DomainException_on_duplicate_username()
    {
        _repo.ExistsByUsernameAsync("dup", Arg.Any<CancellationToken>()).Returns(true);
        var sut = CreateSut();

        var act = () => sut.CreateAsync(
            new AdminCreateRequest { Username = "dup", DisplayName = "User", Password = "S3cret!Pa55word" },
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.AdminUsernameDuplicate);
        ex.Which.Status.Should().Be(StatusCodes.Status409Conflict);

        await _audit.DidNotReceive().PublishAsync(Arg.Any<AuditEntryDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_missing()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Admin?)null);
        var sut = CreateSut();

        var result = await sut.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_returns_dto_and_etag()
    {
        var entity = AdminFaker.Create();
        _repo.GetByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        var sut = CreateSut();

        var result = await sut.GetByIdAsync(entity.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Value.Admin.Id.Should().Be(entity.Id);
        result.Value.ETag.Should().Contain(Convert.ToBase64String(entity.RowVersion));
    }

    [Fact]
    public async Task PatchAsync_throws_when_not_found()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Admin?)null);
        var sut = CreateSut();

        var act = () => sut.PatchAsync(Guid.NewGuid(), new AdminPatchRequest { DisplayName = "x" }, null, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task PatchAsync_throws_ETAG_MISMATCH_when_ifMatch_does_not_match()
    {
        var entity = AdminFaker.Create();
        _repo.GetByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        var sut = CreateSut();

        var act = () => sut.PatchAsync(
            entity.Id,
            new AdminPatchRequest { DisplayName = "New" },
            ifMatch: "\"WRONG\"",
            CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.EtagMismatch);

        await _audit.DidNotReceive().PublishAsync(Arg.Any<AuditEntryDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchAsync_emits_Updated_when_only_displayName_changes()
    {
        var entity = AdminFaker.Create();
        entity.DisplayName = "Old";
        _repo.GetByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        var sut = CreateSut();

        await sut.PatchAsync(entity.Id, new AdminPatchRequest { DisplayName = "New" }, ifMatch: null, CancellationToken.None);

        entity.DisplayName.Should().Be("New");
        await _audit.Received(1).PublishAsync(
            Arg.Is<AuditEntryDto>(e => e.Action == "Updated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchAsync_emits_StatusChanged_when_isActive_toggles()
    {
        var entity = AdminFaker.Create(isActive: true);
        _repo.GetByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        var sut = CreateSut();

        await sut.PatchAsync(entity.Id, new AdminPatchRequest { IsActive = false }, ifMatch: null, CancellationToken.None);

        entity.IsActive.Should().BeFalse();
        await _audit.Received(1).PublishAsync(
            Arg.Is<AuditEntryDto>(e => e.Action == "StatusChanged"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchAsync_does_not_emit_when_no_effective_change()
    {
        var entity = AdminFaker.Create(isActive: true);
        entity.DisplayName = "Same";
        _repo.GetByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        var sut = CreateSut();

        await sut.PatchAsync(
            entity.Id,
            new AdminPatchRequest { DisplayName = "Same", IsActive = true },
            ifMatch: null,
            CancellationToken.None);

        await _audit.DidNotReceive().PublishAsync(Arg.Any<AuditEntryDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAsync_emits_StatusChanged_when_active()
    {
        var entity = AdminFaker.Create(isActive: true);
        _repo.GetByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        var sut = CreateSut();

        await sut.DeactivateAsync(entity.Id, CancellationToken.None);

        entity.IsActive.Should().BeFalse();
        await _audit.Received(1).PublishAsync(
            Arg.Is<AuditEntryDto>(e => e.Action == "StatusChanged"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAsync_is_idempotent_on_already_inactive()
    {
        var entity = AdminFaker.Create(isActive: false);
        _repo.GetByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        var sut = CreateSut();

        await sut.DeactivateAsync(entity.Id, CancellationToken.None);

        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().PublishAsync(Arg.Any<AuditEntryDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAsync_throws_when_missing()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Admin?)null);
        var sut = CreateSut();

        var act = () => sut.DeactivateAsync(Guid.NewGuid(), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Theory]
    [InlineData("bogus")]              // missing direction
    [InlineData("unknownField:asc")]   // unknown field
    [InlineData("username:sideways")]  // unknown direction
    public async Task ListAsync_throws_422_on_bad_sort(string sort)
    {
        var sut = CreateSut();
        var act = () => sut.ListAsync(1, 20, null, sort, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
        ex.Which.Code.Should().Be(ErrorCodes.AdminValidation);
    }
}
