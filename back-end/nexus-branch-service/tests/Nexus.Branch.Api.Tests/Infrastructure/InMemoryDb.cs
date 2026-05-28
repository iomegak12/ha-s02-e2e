using Microsoft.EntityFrameworkCore;
using Nexus.Branches.Api.Infrastructure.Persistence;

namespace Nexus.Branches.Api.Tests.Infrastructure;

/// <summary>Helper to spin up a fresh in-memory <see cref="BranchDbContext"/> per test.</summary>
internal static class InMemoryDb
{
    public static BranchDbContext Create()
    {
        var options = new DbContextOptionsBuilder<BranchDbContext>()
            .UseInMemoryDatabase(databaseName: $"branches-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new BranchDbContext(options);
    }
}
