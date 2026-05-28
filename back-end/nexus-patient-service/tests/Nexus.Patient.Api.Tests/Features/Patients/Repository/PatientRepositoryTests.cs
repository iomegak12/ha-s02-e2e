using Nexus.Patients.Api.Features.Patients.Models;
using Nexus.Patients.Api.Features.Patients.Repository;
using Nexus.Patients.Api.Tests.Infrastructure;

namespace Nexus.Patients.Api.Tests.Features.Patients.Repository;

public sealed class PatientRepositoryTests
{
    private static Patient NewPatient(
        string fullName = "Ravi Kumar",
        string? phone = "+91-9000000001",
        string? email = "ravi@example.com",
        DateOnly? dob = null,
        PatientStatus status = PatientStatus.Draft,
        string publicCode = "PAT-2026-BLR-000001")
    {
        var parts = fullName.Split(' ', 2);
        return new()
        {
            Id = Guid.NewGuid(),
            PublicCode = publicCode,
            FirstName = parts[0],
            LastName = parts.Length > 1 ? parts[1] : string.Empty,
            FullName = fullName,
            Phone = phone,
            Email = email,
            DateOfBirth = dob ?? new DateOnly(1990, 4, 12),
            Gender = "Male",
            PrimaryBranchId = Guid.NewGuid(),
            Status = status,
        };
    }

    [Fact]
    public async Task AddAsync_then_GetByIdAsync_round_trips_patient()
    {
        await using var db = InMemoryDb.Create();
        var repo = new PatientRepository(db);
        var patient = NewPatient();

        await repo.AddAsync(patient, CancellationToken.None);
        var loaded = await repo.GetByIdAsync(patient.Id, CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.FullName.Should().Be("Ravi Kumar");
        loaded.Status.Should().Be(PatientStatus.Draft);
    }

    [Fact]
    public async Task FindByPhoneDobAsync_finds_existing_match()
    {
        await using var db = InMemoryDb.Create();
        var repo = new PatientRepository(db);
        var dob = new DateOnly(1980, 1, 1);
        await repo.AddAsync(NewPatient(phone: "+91-9999999999", dob: dob, publicCode: "PAT-2026-BLR-000010"), CancellationToken.None);

        var hit = await repo.FindByPhoneDobAsync("+91-9999999999", dob, CancellationToken.None);

        hit.Should().NotBeNull();
    }

    [Fact]
    public async Task FindByEmailDobAsync_returns_null_when_no_match()
    {
        await using var db = InMemoryDb.Create();
        var repo = new PatientRepository(db);

        var hit = await repo.FindByEmailDobAsync("nobody@example.com", new DateOnly(1970, 1, 1), CancellationToken.None);

        hit.Should().BeNull();
    }

    [Fact]
    public async Task QueryAsync_filters_by_status_and_paginates()
    {
        await using var db = InMemoryDb.Create();
        var repo = new PatientRepository(db);
        for (var i = 0; i < 5; i++)
            await repo.AddAsync(NewPatient(publicCode: $"PAT-2026-BLR-{i:000000}", phone: $"+91-{i:0000000000}", email: $"p{i}@x.com", status: PatientStatus.Draft), CancellationToken.None);
        await repo.AddAsync(NewPatient(publicCode: "PAT-2026-BLR-999999", phone: "+91-1234567890", email: "active@x.com", status: PatientStatus.Active), CancellationToken.None);

        var draftPage = await repo.QueryAsync(new PatientQuery(Status: PatientStatus.Draft, Page: 1, Size: 3), CancellationToken.None);

        draftPage.Total.Should().Be(5);
        draftPage.Items.Should().HaveCount(3);
        draftPage.Items.Should().OnlyContain(p => p.Status == PatientStatus.Draft);
    }
}

public sealed class PatientCodeSequenceRepositoryTests
{
    [Fact]
    public async Task NextAsync_starts_at_1_and_increments_on_subsequent_calls()
    {
        await using var db = InMemoryDb.Create();
        var repo = new PatientCodeSequenceRepository(db);

        var a = await repo.NextAsync("BLR", 2026, CancellationToken.None);
        var b = await repo.NextAsync("BLR", 2026, CancellationToken.None);
        var c = await repo.NextAsync("BLR", 2026, CancellationToken.None);

        a.Should().Be(1);
        b.Should().Be(2);
        c.Should().Be(3);
    }

    [Fact]
    public async Task NextAsync_scopes_counters_per_branch_and_year()
    {
        await using var db = InMemoryDb.Create();
        var repo = new PatientCodeSequenceRepository(db);

        (await repo.NextAsync("BLR", 2026, CancellationToken.None)).Should().Be(1);
        (await repo.NextAsync("MAA", 2026, CancellationToken.None)).Should().Be(1);
        (await repo.NextAsync("BLR", 2027, CancellationToken.None)).Should().Be(1);
        (await repo.NextAsync("BLR", 2026, CancellationToken.None)).Should().Be(2);
    }

    [Fact]
    public async Task NextAsync_normalises_branch_code_case()
    {
        await using var db = InMemoryDb.Create();
        var repo = new PatientCodeSequenceRepository(db);

        (await repo.NextAsync("blr", 2026, CancellationToken.None)).Should().Be(1);
        (await repo.NextAsync("BLR", 2026, CancellationToken.None)).Should().Be(2);
    }
}
