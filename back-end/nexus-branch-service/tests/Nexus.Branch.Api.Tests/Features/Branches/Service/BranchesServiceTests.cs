using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Branches.Api.Features.Branches.Models;
using Nexus.Branches.Api.Features.Branches.Repository;
using Nexus.Branches.Api.Features.Branches.Service;
using Nexus.Branches.Api.Infrastructure.Audit;
using Nexus.Branches.Api.Infrastructure.Errors;
using Nexus.Branches.Api.Tests.Infrastructure;
using NSubstitute;

namespace Nexus.Branches.Api.Tests.Features.Branches.Service;

public sealed class BranchesServiceTests
{
    private static ClaimsPrincipal Admin() => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", Guid.NewGuid().ToString()),
        new Claim("preferred_username", "admin"),
        new Claim(ClaimTypes.Role, "Admin"),
    }, authenticationType: "test"));

    private static (BranchesService svc, IAuditPublisher audit, BranchRepository repo) Build(Microsoft.EntityFrameworkCore.DbContext? _ = null)
    {
        var db = InMemoryDb.Create();
        var repo = new BranchRepository(db);
        var audit = Substitute.For<IAuditPublisher>();
        var clock = TimeProvider.System;
        var svc = new BranchesService(repo, audit, clock, NullLogger<BranchesService>.Instance);
        return (svc, audit, repo);
    }

    [Fact]
    public async Task CreateAsync_creates_branch_and_publishes_audit_event()
    {
        var (svc, audit, _) = Build();
        var req = new BranchCreateRequest { Code = "BLR", Name = "Bengaluru", City = "Bengaluru" };

        var result = await svc.CreateAsync(req, Admin(), CancellationToken.None);

        result.Branch.Code.Should().Be("BLR");
        result.Branch.IsActive.Should().BeTrue();
        result.ETag.Should().NotBeNullOrWhiteSpace();
        await audit.Received(1).PublishAsync(
            Arg.Is<AuditEventDto>(e => e.EntityType == "Branch" && e.Action == "BranchCreated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_uppercases_code()
    {
        var (svc, _, _) = Build();
        var req = new BranchCreateRequest { Code = "blr", Name = "Bengaluru", City = "Bengaluru" };

        var result = await svc.CreateAsync(req, Admin(), CancellationToken.None);

        result.Branch.Code.Should().Be("BLR");
    }

    [Fact]
    public async Task CreateAsync_throws_BranchCodeDuplicate_when_active_code_exists()
    {
        var (svc, _, _) = Build();
        await svc.CreateAsync(new BranchCreateRequest { Code = "BLR", Name = "B1", City = "X" }, Admin(), CancellationToken.None);

        Func<Task> act = () => svc.CreateAsync(new BranchCreateRequest { Code = "BLR", Name = "B2", City = "Y" }, Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.BranchCodeDuplicate);
        ex.Which.Status.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_missing()
    {
        var (svc, _, _) = Build();
        var got = await svc.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);
        got.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_returns_branch_with_etag()
    {
        var (svc, _, _) = Build();
        var created = await svc.CreateAsync(new BranchCreateRequest { Code = "DEL", Name = "Delhi", City = "Delhi" }, Admin(), CancellationToken.None);

        var got = await svc.GetByIdAsync(created.Branch.Id, CancellationToken.None);

        got.Should().NotBeNull();
        got!.Branch.Code.Should().Be("DEL");
        got.ETag.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PatchAsync_updates_fields_and_emits_BranchUpdated()
    {
        var (svc, audit, _) = Build();
        var created = await svc.CreateAsync(new BranchCreateRequest { Code = "MAA", Name = "Madras", City = "Chennai" }, Admin(), CancellationToken.None);

        var patched = await svc.PatchAsync(
            created.Branch.Id,
            new BranchPatchRequest { Name = "Madras Central" },
            ifMatch: null, Admin(), CancellationToken.None);

        patched.Branch.Name.Should().Be("Madras Central");
        patched.Branch.City.Should().Be("Chennai");
        await audit.Received().PublishAsync(
            Arg.Is<AuditEventDto>(e => e.Action == "BranchUpdated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchAsync_throws_NotFound_for_unknown_id()
    {
        var (svc, _, _) = Build();

        Func<Task> act = () => svc.PatchAsync(Guid.NewGuid(), new BranchPatchRequest { Name = "x" }, null, Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task PatchAsync_throws_EtagMismatch_for_wrong_if_match()
    {
        var (svc, _, _) = Build();
        var created = await svc.CreateAsync(new BranchCreateRequest { Code = "HYD", Name = "Hyderabad", City = "Hyderabad" }, Admin(), CancellationToken.None);

        Func<Task> act = () => svc.PatchAsync(
            created.Branch.Id, new BranchPatchRequest { Name = "x" },
            ifMatch: "\"AAAAAAAAAAA=\"", Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.EtagMismatch);
    }

    [Fact]
    public async Task DeactivateAsync_soft_deletes_and_emits_audit()
    {
        var (svc, audit, repo) = Build();
        var created = await svc.CreateAsync(new BranchCreateRequest { Code = "KOL", Name = "Kolkata", City = "Kolkata" }, Admin(), CancellationToken.None);

        await svc.DeactivateAsync(created.Branch.Id, Admin(), CancellationToken.None);

        var reloaded = await repo.GetByIdNoTrackingAsync(created.Branch.Id, CancellationToken.None);
        reloaded!.IsActive.Should().BeFalse();
        await audit.Received().PublishAsync(
            Arg.Is<AuditEventDto>(e => e.Action == "BranchDeactivated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAsync_throws_NotFound_for_unknown_id()
    {
        var (svc, _, _) = Build();
        Func<Task> act = () => svc.DeactivateAsync(Guid.NewGuid(), Admin(), CancellationToken.None);
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task DeactivateAsync_is_idempotent_for_already_inactive_branch()
    {
        var (svc, audit, _) = Build();
        var created = await svc.CreateAsync(new BranchCreateRequest { Code = "PNQ", Name = "Pune", City = "Pune" }, Admin(), CancellationToken.None);
        await svc.DeactivateAsync(created.Branch.Id, Admin(), CancellationToken.None);
        audit.ClearReceivedCalls();

        await svc.DeactivateAsync(created.Branch.Id, Admin(), CancellationToken.None);

        await audit.DidNotReceive().PublishAsync(Arg.Any<AuditEventDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_paginates_and_filters()
    {
        var (svc, _, _) = Build();
        await svc.CreateAsync(new BranchCreateRequest { Code = "AAA", Name = "A", City = "X" }, Admin(), CancellationToken.None);
        await svc.CreateAsync(new BranchCreateRequest { Code = "BBB", Name = "B", City = "Y" }, Admin(), CancellationToken.None);
        var c = await svc.CreateAsync(new BranchCreateRequest { Code = "CCC", Name = "C", City = "Z" }, Admin(), CancellationToken.None);
        await svc.DeactivateAsync(c.Branch.Id, Admin(), CancellationToken.None);

        var page = await svc.ListAsync(isActive: true, codeContains: null, page: 1, size: 10, CancellationToken.None);

        page.TotalCount.Should().Be(2);
        page.Items.Should().OnlyContain(i => i.IsActive);
    }

    [Fact]
    public async Task CreateAsync_allowed_after_deactivation_of_same_code()
    {
        var (svc, _, _) = Build();
        var first = await svc.CreateAsync(new BranchCreateRequest { Code = "REU", Name = "First", City = "X" }, Admin(), CancellationToken.None);
        await svc.DeactivateAsync(first.Branch.Id, Admin(), CancellationToken.None);

        var second = await svc.CreateAsync(new BranchCreateRequest { Code = "REU", Name = "Reused", City = "Y" }, Admin(), CancellationToken.None);

        second.Branch.Code.Should().Be("REU");
        second.Branch.Id.Should().NotBe(first.Branch.Id);
    }
}
