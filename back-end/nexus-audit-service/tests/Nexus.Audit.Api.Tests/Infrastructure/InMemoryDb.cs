using Microsoft.EntityFrameworkCore;
using Nexus.Audit.Api.Infrastructure.Persistence;

namespace Nexus.Audit.Api.Tests.Infrastructure;

/// <summary>Helper to spin up a fresh in-memory <see cref="AuditDbContext"/> per test.</summary>
internal static class InMemoryDb
{
    public static AuditDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(databaseName: $"audit-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AuditDbContext(options);
    }
}
