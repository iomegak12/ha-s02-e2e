using Microsoft.EntityFrameworkCore;
using Nexus.Patients.Api.Infrastructure.Persistence;

namespace Nexus.Patients.Api.Tests.Infrastructure;

internal static class InMemoryDb
{
    public static PatientDbContext Create()
    {
        var options = new DbContextOptionsBuilder<PatientDbContext>()
            .UseInMemoryDatabase(databaseName: $"patients-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new PatientDbContext(options);
    }
}
