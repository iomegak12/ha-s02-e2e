using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Doctors.Api.Features.Doctors.Models;
using Nexus.Doctors.Api.Features.Doctors.Repository;
using Nexus.Doctors.Api.Features.Doctors.Service;
using Nexus.Doctors.Api.Infrastructure.Audit;
using Nexus.Doctors.Api.Infrastructure.Branches;
using Nexus.Doctors.Api.Infrastructure.Errors;
using Nexus.Doctors.Api.Tests.Infrastructure;
using NSubstitute;

namespace Nexus.Doctors.Api.Tests.Features.Doctors.Service;

public sealed class DoctorsServiceTests
{
    private static ClaimsPrincipal Admin() => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", Guid.NewGuid().ToString()),
        new Claim("preferred_username", "admin"),
        new Claim(ClaimTypes.Role, "Admin"),
    }, authenticationType: "test"));

    private static (DoctorsService svc, IAuditPublisher audit, IBranchesClient branches, Nexus.Doctors.Api.Infrastructure.Persistence.DoctorDbContext db) Build(bool branchActive = true)
    {
        var db = InMemoryDb.Create();
        var repo = new DoctorRepository(db);
        var seq = new DoctorCodeSequenceRepository(db);
        var gen = new PublicCodeGenerator(seq);
        var audit = Substitute.For<IAuditPublisher>();
        var branches = Substitute.For<IBranchesClient>();
        branches.GetBranchAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<BranchSummary?>(branchActive
                ? new BranchSummary { Id = ci.Arg<Guid>(), Code = "BLR", IsActive = true }
                : null));
        var svc = new DoctorsService(repo, gen, branches, audit, TimeProvider.System, NullLogger<DoctorsService>.Instance);
        return (svc, audit, branches, db);
    }

    private static async Task SeedVerifiedRequiredDocAsync(Nexus.Doctors.Api.Infrastructure.Persistence.DoctorDbContext db, Guid doctorId)
    {
        db.DoctorDocuments.Add(new DoctorDocument
        {
            Id = Guid.NewGuid(),
            DoctorId = doctorId,
            Kind = DocumentKinds.MedicalLicense,
            FileName = "license.pdf",
            ContentType = "application/pdf",
            Sha256 = Guid.NewGuid().ToString("N"),
            SizeBytes = 1,
            StorageKey = "k",
            Status = DocumentStatus.Verified,
            IsRequired = true,
            UploadedAtUtc = DateTime.UtcNow,
            ReviewedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(CancellationToken.None);
        // Detach so subsequent GetByIdAsync(Include Documents) sees a fresh load.
        foreach (var e in db.ChangeTracker.Entries().ToList()) e.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
    }

    private static DoctorCreateRequest NewCreate(Guid branchId, string license = "KAR-MED-12345") => new()
    {
        FirstName = "Asha",
        LastName = "Menon",
        Specialty = "Cardiology",
        LicenseNumber = license,
        PrimaryPhone = "+91-9000000099",
        Email = "asha@example.com",
        PrimaryBranchId = branchId,
    };

    [Fact]
    public async Task CreateAsync_creates_pending_with_public_code_and_audit()
    {
        var branchId = Guid.NewGuid();
        var (svc, audit, _, _) = Build();

        var result = await svc.CreateAsync(NewCreate(branchId), Admin(), CancellationToken.None);

        result.Doctor.Status.Should().Be("Pending");
        result.Doctor.Code.Should().MatchRegex("^DOC-\\d{4}-[A-Z]{3}-\\d{6}$");
        result.Doctor.PrimaryBranchId.Should().Be(branchId);
        await audit.Received().PublishAsync(
            Arg.Is<AuditEventDto>(e => e.EntityType == "Doctor" && e.Action == "Doctor.Created"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_throws_BranchUnknown_for_inactive_branch()
    {
        var (svc, _, _, _) = Build(branchActive: false);
        Func<Task> act = () => svc.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.BranchUnknown);
    }

    [Fact]
    public async Task CreateAsync_throws_DuplicateLicense_on_collision_with_active()
    {
        var (svc, _, _, _) = Build();
        await svc.CreateAsync(NewCreate(Guid.NewGuid(), license: "DUP-1"), Admin(), CancellationToken.None);

        Func<Task> act = () => svc.CreateAsync(NewCreate(Guid.NewGuid(), license: "DUP-1"), Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.DuplicateLicense);
    }

    [Fact]
    public async Task PatchAsync_updates_fields_and_emits_audit()
    {
        var (svc, audit, _, _) = Build();
        var created = await svc.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);

        var patched = await svc.PatchAsync(created.Doctor.Id,
            new DoctorPatchRequest { Specialty = "Oncology" }, null, Admin(), CancellationToken.None);

        patched.Doctor.Specialty.Should().Be("Oncology");
        await audit.Received().PublishAsync(
            Arg.Is<AuditEventDto>(e => e.Action == "Doctor.Updated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchAsync_throws_EtagMismatch_for_wrong_if_match()
    {
        var (svc, _, _, _) = Build();
        var created = await svc.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);
        Func<Task> act = () => svc.PatchAsync(created.Doctor.Id,
            new DoctorPatchRequest { Specialty = "X" }, "\"BAD=\"", Admin(), CancellationToken.None);
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.EtagMismatch);
    }

    [Fact]
    public async Task VerifyAsync_blocked_when_no_required_documents_verified()
    {
        var (svc, _, _, _) = Build();
        var created = await svc.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);

        Func<Task> act = () => svc.VerifyAsync(created.Doctor.Id, Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.LifecycleBlocked);
    }

    [Fact]
    public async Task VerifyAsync_moves_pending_to_verified_when_required_doc_verified()
    {
        var (svc, _, _, db) = Build();
        var created = await svc.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);
        await SeedVerifiedRequiredDocAsync(db, created.Doctor.Id);

        var verified = await svc.VerifyAsync(created.Doctor.Id, Admin(), CancellationToken.None);

        verified.Doctor.Status.Should().Be("Verified");
    }

    [Fact]
    public async Task ApproveAsync_blocks_when_not_verified()
    {
        var (svc, _, _, _) = Build();
        var created = await svc.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);

        Func<Task> act = () => svc.ApproveAsync(created.Doctor.Id, Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.LifecycleInvalidTransition);
    }

    [Fact]
    public async Task Full_lifecycle_pending_to_active_then_deactivate_then_reactivate()
    {
        var branchId = Guid.NewGuid();
        var (svc, _, _, db) = Build();
        var created = await svc.CreateAsync(NewCreate(branchId), Admin(), CancellationToken.None);
        await SeedVerifiedRequiredDocAsync(db, created.Doctor.Id);
        await svc.VerifyAsync(created.Doctor.Id, Admin(), CancellationToken.None);
        await svc.ApproveAsync(created.Doctor.Id, Admin(), CancellationToken.None);
        var active = await svc.ActivateAsync(created.Doctor.Id, Admin(), CancellationToken.None);
        active.Doctor.Status.Should().Be("Active");

        var deactivated = await svc.DeactivateAsync(created.Doctor.Id, Admin(), CancellationToken.None);
        deactivated.Doctor.Status.Should().Be("Deactivated");

        // Re-activation: Deactivated → Active allowed per spec.
        var reactivated = await svc.ActivateAsync(created.Doctor.Id, Admin(), CancellationToken.None);
        reactivated.Doctor.Status.Should().Be("Active");
    }

    [Fact]
    public async Task ActivateAsync_auto_creates_primary_branch_link()
    {
        var branchId = Guid.NewGuid();
        var (svc, _, _, db) = Build();
        var created = await svc.CreateAsync(NewCreate(branchId), Admin(), CancellationToken.None);
        await SeedVerifiedRequiredDocAsync(db, created.Doctor.Id);
        await svc.VerifyAsync(created.Doctor.Id, Admin(), CancellationToken.None);
        await svc.ApproveAsync(created.Doctor.Id, Admin(), CancellationToken.None);
        await svc.ActivateAsync(created.Doctor.Id, Admin(), CancellationToken.None);

        var links = await svc.ListBranchesAsync(created.Doctor.Id, CancellationToken.None);
        links.Should().ContainSingle(l => l.BranchId == branchId && l.IsPrimary);
    }

    [Fact]
    public async Task LinkBranchAsync_promotes_primary_and_demotes_existing()
    {
        var primary = Guid.NewGuid();
        var other = Guid.NewGuid();
        var (svc, _, _, db) = Build();
        var created = await svc.CreateAsync(NewCreate(primary), Admin(), CancellationToken.None);
        await SeedVerifiedRequiredDocAsync(db, created.Doctor.Id);
        await svc.VerifyAsync(created.Doctor.Id, Admin(), CancellationToken.None);
        await svc.ApproveAsync(created.Doctor.Id, Admin(), CancellationToken.None);
        await svc.ActivateAsync(created.Doctor.Id, Admin(), CancellationToken.None);

        var link = await svc.LinkBranchAsync(created.Doctor.Id,
            new DoctorBranchLinkRequest { BranchId = other, IsPrimary = true }, Admin(), CancellationToken.None);

        link.IsPrimary.Should().BeTrue();
        var links = await svc.ListBranchesAsync(created.Doctor.Id, CancellationToken.None);
        links.Where(l => l.IsPrimary).Should().ContainSingle(l => l.BranchId == other);
    }

    [Fact]
    public async Task LinkBranchAsync_throws_AlreadyLinked_for_duplicate_live_link()
    {
        var primary = Guid.NewGuid();
        var (svc, _, _, db) = Build();
        var created = await svc.CreateAsync(NewCreate(primary), Admin(), CancellationToken.None);
        await SeedVerifiedRequiredDocAsync(db, created.Doctor.Id);
        await svc.VerifyAsync(created.Doctor.Id, Admin(), CancellationToken.None);
        await svc.ApproveAsync(created.Doctor.Id, Admin(), CancellationToken.None);
        await svc.ActivateAsync(created.Doctor.Id, Admin(), CancellationToken.None);

        Func<Task> act = () => svc.LinkBranchAsync(created.Doctor.Id,
            new DoctorBranchLinkRequest { BranchId = primary, IsPrimary = false }, Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.AlreadyLinked);
    }

    [Fact]
    public async Task UnlinkBranchAsync_blocks_unlinking_primary_of_active_doctor()
    {
        var primary = Guid.NewGuid();
        var (svc, _, _, db) = Build();
        var created = await svc.CreateAsync(NewCreate(primary), Admin(), CancellationToken.None);
        await SeedVerifiedRequiredDocAsync(db, created.Doctor.Id);
        await svc.VerifyAsync(created.Doctor.Id, Admin(), CancellationToken.None);
        await svc.ApproveAsync(created.Doctor.Id, Admin(), CancellationToken.None);
        await svc.ActivateAsync(created.Doctor.Id, Admin(), CancellationToken.None);

        Func<Task> act = () => svc.UnlinkBranchAsync(created.Doctor.Id, primary, Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.PrimaryBranchRequired);
    }

    [Fact]
    public async Task UnlinkBranchAsync_idempotent_for_unknown_link()
    {
        var (svc, audit, _, _) = Build();
        var created = await svc.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);
        audit.ClearReceivedCalls();

        await svc.UnlinkBranchAsync(created.Doctor.Id, Guid.NewGuid(), Admin(), CancellationToken.None);

        await audit.DidNotReceive().PublishAsync(Arg.Any<AuditEventDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_filters_by_status()
    {
        var (svc, _, _, db) = Build();
        var pending = await svc.CreateAsync(NewCreate(Guid.NewGuid(), license: "L1"), Admin(), CancellationToken.None);
        var verified = await svc.CreateAsync(NewCreate(Guid.NewGuid(), license: "L2"), Admin(), CancellationToken.None);
        await SeedVerifiedRequiredDocAsync(db, verified.Doctor.Id);
        await svc.VerifyAsync(verified.Doctor.Id, Admin(), CancellationToken.None);

        var pendings = await svc.ListAsync(DoctorStatus.Pending, null, null, 1, 10, CancellationToken.None);
        var verifieds = await svc.ListAsync(DoctorStatus.Verified, null, null, 1, 10, CancellationToken.None);

        pendings.TotalCount.Should().Be(1);
        pendings.Items.Single().Id.Should().Be(pending.Doctor.Id);
        verifieds.TotalCount.Should().Be(1);
        verifieds.Items.Single().Id.Should().Be(verified.Doctor.Id);
    }
}
