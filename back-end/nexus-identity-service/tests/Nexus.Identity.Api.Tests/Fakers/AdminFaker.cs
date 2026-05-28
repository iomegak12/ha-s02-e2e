using Bogus;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;

namespace Nexus.Identity.Api.Tests.Fakers;

/// <summary>Bogus faker for <see cref="Admin"/> entities used by tests.</summary>
public static class AdminFaker
{
    /// <summary>Create a populated <see cref="Admin"/> instance. Optionally override the seed for determinism.</summary>
    public static Admin Create(int? seed = null, bool isActive = true)
    {
        var faker = new Faker<Admin>()
            .RuleFor(a => a.Id, _ => Guid.NewGuid())
            .RuleFor(a => a.Username, f => f.Internet.UserName().ToLowerInvariant().Replace(" ", string.Empty))
            .RuleFor(a => a.DisplayName, f => f.Name.FullName())
            .RuleFor(a => a.PasswordHash, _ => "$2a$12$" + new string('x', 53))
            .RuleFor(a => a.IsActive, _ => isActive)
            .RuleFor(a => a.CreatedAtUtc, _ => DateTime.UtcNow.AddDays(-1))
            .RuleFor(a => a.UpdatedAtUtc, _ => DateTime.UtcNow)
            .RuleFor(a => a.RowVersion, _ => BitConverter.GetBytes(DateTime.UtcNow.Ticks));

        if (seed is { } s)
        {
            faker.UseSeed(s);
        }

        return faker.Generate();
    }
}
