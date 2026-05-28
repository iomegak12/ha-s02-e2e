using Nexus.Branches.Api.Features.Branches.Models;
using Nexus.Branches.Api.Features.Branches.Repository;
using Nexus.Branches.Api.Tests.Infrastructure;

namespace Nexus.Branches.Api.Tests.Features.Branches.Repository;

public sealed class BranchRepositoryTests
{
    private static Branch NewBranch(string code = "BLR", string name = "Bangalore", bool isActive = true) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        City = "Bengaluru",
        IsActive = isActive,
    };

    [Fact]
    public async Task AddAsync_then_GetByIdAsync_round_trips_entity()
    {
        await using var db = InMemoryDb.Create();
        var repo = new BranchRepository(db);
        var branch = NewBranch();

        await repo.AddAsync(branch, CancellationToken.None);
        var loaded = await repo.GetByIdAsync(branch.Id, CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.Code.Should().Be("BLR");
        loaded.Name.Should().Be("Bangalore");
        loaded.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdNoTrackingAsync_returns_detached_copy()
    {
        await using var db = InMemoryDb.Create();
        var repo = new BranchRepository(db);
        var branch = NewBranch();
        await repo.AddAsync(branch, CancellationToken.None);

        var fetched = await repo.GetByIdNoTrackingAsync(branch.Id, CancellationToken.None);
        fetched.Should().NotBeNull();
        db.Entry(fetched!).State.Should().Be(Microsoft.EntityFrameworkCore.EntityState.Detached);
    }

    [Fact]
    public async Task GetActiveByCodeAsync_ignores_inactive_rows()
    {
        await using var db = InMemoryDb.Create();
        var repo = new BranchRepository(db);
        await repo.AddAsync(NewBranch("OLD", "Old branch", isActive: false), CancellationToken.None);

        var hit = await repo.GetActiveByCodeAsync("OLD", CancellationToken.None);

        hit.Should().BeNull();
    }

    [Fact]
    public async Task QueryAsync_filters_by_isActive_and_paginates()
    {
        await using var db = InMemoryDb.Create();
        var repo = new BranchRepository(db);
        await repo.AddAsync(NewBranch("BLR", "Bangalore"), CancellationToken.None);
        await repo.AddAsync(NewBranch("MAA", "Madras"), CancellationToken.None);
        await repo.AddAsync(NewBranch("DEL", "Delhi", isActive: false), CancellationToken.None);

        var active = await repo.QueryAsync(new BranchQuery(IsActive: true, Page: 1, Size: 10), CancellationToken.None);

        active.Total.Should().Be(2);
        active.Items.Should().OnlyContain(b => b.IsActive);
    }

    [Fact]
    public async Task QueryAsync_filters_by_codeContains()
    {
        await using var db = InMemoryDb.Create();
        var repo = new BranchRepository(db);
        await repo.AddAsync(NewBranch("BLR-N", "North"), CancellationToken.None);
        await repo.AddAsync(NewBranch("BLR-S", "South"), CancellationToken.None);
        await repo.AddAsync(NewBranch("MAA", "Madras"), CancellationToken.None);

        var matched = await repo.QueryAsync(new BranchQuery(CodeContains: "BLR"), CancellationToken.None);

        matched.Total.Should().Be(2);
        matched.Items.Should().OnlyContain(b => b.Code.StartsWith("BLR"));
    }
}
