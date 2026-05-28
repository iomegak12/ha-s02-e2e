using Microsoft.EntityFrameworkCore;
using Nexus.Identity.Api.Features.Admins.Repository;
using Nexus.Identity.Api.Infrastructure.Persistence;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;
using Nexus.Identity.Api.Tests.Fakers;

namespace Nexus.Identity.Api.Tests.Features.Admins.Repository;

public class AdminRepositoryTests
{
    private static IdentityDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"adminrepo-{Guid.NewGuid()}")
            .Options;
        return new IdentityDbContext(options);
    }

    [Fact]
    public async Task GetByIdAsync_returns_entity_when_present()
    {
        await using var db = NewContext();
        var admin = AdminFaker.Create();
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        var sut = new AdminRepository(db);
        var result = await sut.GetByIdAsync(admin.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Username.Should().Be(admin.Username);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_missing()
    {
        await using var db = NewContext();
        var sut = new AdminRepository(db);

        var result = await sut.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByUsernameAsync_is_exact_match()
    {
        await using var db = NewContext();
        var admin = AdminFaker.Create();
        admin.Username = "asha.menon";
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        var sut = new AdminRepository(db);

        (await sut.GetByUsernameAsync("asha.menon", CancellationToken.None)).Should().NotBeNull();
        (await sut.GetByUsernameAsync("Asha.Menon", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ExistsByUsernameAsync_returns_true_when_present()
    {
        await using var db = NewContext();
        var admin = AdminFaker.Create();
        db.Admins.Add(admin);
        await db.SaveChangesAsync();

        var sut = new AdminRepository(db);

        (await sut.ExistsByUsernameAsync(admin.Username, CancellationToken.None)).Should().BeTrue();
        (await sut.ExistsByUsernameAsync("does.not.exist", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_paginates_and_orders_by_username_asc()
    {
        await using var db = NewContext();
        for (var i = 0; i < 5; i++)
        {
            var a = AdminFaker.Create();
            a.Username = $"user{i:D2}";
            db.Admins.Add(a);
        }
        await db.SaveChangesAsync();

        var sut = new AdminRepository(db);
        var (items, total) = await sut.ListAsync(
            page: 1, size: 3, isActive: null,
            sort: AdminSortField.Username, descending: false,
            CancellationToken.None);

        total.Should().Be(5);
        items.Should().HaveCount(3);
        items.Select(x => x.Username).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ListAsync_filters_by_isActive()
    {
        await using var db = NewContext();
        for (var i = 0; i < 3; i++)
        {
            db.Admins.Add(AdminFaker.Create(isActive: true));
        }
        for (var i = 0; i < 2; i++)
        {
            db.Admins.Add(AdminFaker.Create(isActive: false));
        }
        await db.SaveChangesAsync();

        var sut = new AdminRepository(db);

        var active = await sut.ListAsync(1, 50, true, AdminSortField.CreatedAtUtc, true, CancellationToken.None);
        active.Total.Should().Be(3);

        var inactive = await sut.ListAsync(1, 50, false, AdminSortField.CreatedAtUtc, true, CancellationToken.None);
        inactive.Total.Should().Be(2);
    }

    [Fact]
    public async Task AddAsync_persists_after_SaveChanges()
    {
        await using var db = NewContext();
        var sut = new AdminRepository(db);

        var admin = AdminFaker.Create();
        await sut.AddAsync(admin, CancellationToken.None);
        await sut.SaveChangesAsync(CancellationToken.None);

        (await db.Admins.CountAsync()).Should().Be(1);
    }
}
