using Nexus.Doctors.Api.Features.Doctors.Models;
using Nexus.Doctors.Api.Features.Doctors.Repository;
using Nexus.Doctors.Api.Tests.Infrastructure;

namespace Nexus.Doctors.Api.Tests.Features.Doctors.Repository;

public sealed class DoctorRepositoryTests
{
    private static Doctor NewDoctor(
        string fullName = "Asha Menon",
        string licenseNumber = "KAR-MED-12345",
        DoctorStatus status = DoctorStatus.Pending,
        string publicCode = "DOC-2026-BLR-000001")
    {
        var parts = fullName.Split(' ', 2);
        return new()
        {
            Id = Guid.NewGuid(),
            PublicCode = publicCode,
            FirstName = parts[0],
            LastName = parts.Length > 1 ? parts[1] : string.Empty,
            FullName = fullName,
            Phone = "+91-9000000000",
            Email = "asha@example.com",
            Specialisation = "Cardiology",
            LicenseNumber = licenseNumber,
            LicenseAuthority = "Karnataka Medical Council",
            PrimaryBranchId = Guid.NewGuid(),
            Status = status,
        };
    }

    [Fact]
    public async Task AddAsync_then_GetByIdAsync_round_trips_doctor()
    {
        await using var db = InMemoryDb.Create();
        var repo = new DoctorRepository(db);
        var doctor = NewDoctor();

        await repo.AddAsync(doctor, CancellationToken.None);
        var loaded = await repo.GetByIdAsync(doctor.Id, CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.LicenseNumber.Should().Be("KAR-MED-12345");
        loaded.Status.Should().Be(DoctorStatus.Pending);
    }

    [Fact]
    public async Task FindActiveByLicenseAsync_skips_deactivated_rows()
    {
        await using var db = InMemoryDb.Create();
        var repo = new DoctorRepository(db);
        await repo.AddAsync(NewDoctor(licenseNumber: "OLD-1", status: DoctorStatus.Deactivated, publicCode: "DOC-2026-BLR-000001"), CancellationToken.None);

        var hit = await repo.FindActiveByLicenseAsync("OLD-1", CancellationToken.None);

        hit.Should().BeNull();
    }

    [Fact]
    public async Task FindActiveByLicenseAsync_finds_active_doctors()
    {
        await using var db = InMemoryDb.Create();
        var repo = new DoctorRepository(db);
        await repo.AddAsync(NewDoctor(licenseNumber: "ACTIVE-1", status: DoctorStatus.Active, publicCode: "DOC-2026-BLR-000002"), CancellationToken.None);

        var hit = await repo.FindActiveByLicenseAsync("ACTIVE-1", CancellationToken.None);

        hit.Should().NotBeNull();
    }

    [Fact]
    public async Task QueryAsync_filters_by_status_and_paginates()
    {
        await using var db = InMemoryDb.Create();
        var repo = new DoctorRepository(db);
        for (var i = 0; i < 4; i++)
            await repo.AddAsync(NewDoctor(licenseNumber: $"PENDING-{i}", publicCode: $"DOC-2026-BLR-{i:000000}", status: DoctorStatus.Pending), CancellationToken.None);
        await repo.AddAsync(NewDoctor(licenseNumber: "ACTIVE-1", publicCode: "DOC-2026-BLR-999999", status: DoctorStatus.Active), CancellationToken.None);

        var pending = await repo.QueryAsync(new DoctorQuery(Status: DoctorStatus.Pending, Page: 1, Size: 3), CancellationToken.None);

        pending.Total.Should().Be(4);
        pending.Items.Should().HaveCount(3);
        pending.Items.Should().OnlyContain(d => d.Status == DoctorStatus.Pending);
    }
}

public sealed class DoctorCodeSequenceRepositoryTests
{
    [Fact]
    public async Task NextAsync_starts_at_1_and_increments()
    {
        await using var db = InMemoryDb.Create();
        var repo = new DoctorCodeSequenceRepository(db);

        (await repo.NextAsync("BLR", 2026, CancellationToken.None)).Should().Be(1);
        (await repo.NextAsync("BLR", 2026, CancellationToken.None)).Should().Be(2);
    }

    [Fact]
    public async Task NextAsync_scopes_per_branch_year()
    {
        await using var db = InMemoryDb.Create();
        var repo = new DoctorCodeSequenceRepository(db);

        (await repo.NextAsync("BLR", 2026, CancellationToken.None)).Should().Be(1);
        (await repo.NextAsync("MAA", 2026, CancellationToken.None)).Should().Be(1);
        (await repo.NextAsync("BLR", 2026, CancellationToken.None)).Should().Be(2);
    }
}
