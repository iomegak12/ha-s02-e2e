using Microsoft.EntityFrameworkCore;
using Nexus.Doctors.Api.Infrastructure.Persistence;

namespace Nexus.Doctors.Api.Tests.Infrastructure;

internal static class InMemoryDb
{
    public static DoctorDbContext Create()
    {
        var options = new DbContextOptionsBuilder<DoctorDbContext>()
            .UseInMemoryDatabase(databaseName: $"doctors-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new DoctorDbContext(options);
    }
}
