using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Patients.Api.Features.Patients.Models;
using Nexus.Patients.Api.Features.Patients.Repository;
using Nexus.Patients.Api.Features.Patients.Service;
using Nexus.Patients.Api.Infrastructure.Audit;
using Nexus.Patients.Api.Infrastructure.Branches;
using Nexus.Patients.Api.Infrastructure.Errors;
using Nexus.Patients.Api.Tests.Infrastructure;
using NSubstitute;

namespace Nexus.Patients.Api.Tests.Features.Patients.Service;

public sealed class PatientsServiceTests
{
    private static ClaimsPrincipal Admin() => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", Guid.NewGuid().ToString()),
        new Claim("preferred_username", "admin"),
        new Claim(ClaimTypes.Role, "Admin"),
    }, authenticationType: "test"));

    private static (PatientsService svc, IAuditPublisher audit, IBranchesClient branches, PatientRepository repo) Build(
        Guid? primaryBranchId = null, bool branchActive = true)
    {
        var db = InMemoryDb.Create();
        var repo = new PatientRepository(db);
        var seq = new PatientCodeSequenceRepository(db);
        var gen = new PublicCodeGenerator(seq);
        var audit = Substitute.For<IAuditPublisher>();
        var branches = Substitute.For<IBranchesClient>();
        branches.GetBranchAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<BranchSummary?>(branchActive
                ? new BranchSummary { Id = ci.Arg<Guid>(), Code = "BLR", IsActive = true }
                : null));
        var svc = new PatientsService(repo, gen, branches, audit, TimeProvider.System, NullLogger<PatientsService>.Instance);
        return (svc, audit, branches, repo);
    }

    private static PatientCreateRequest NewCreate(
        Guid branchId, string phone = "+91-9000000001", string? email = "x@example.com", string dob = "1990-01-01") => new()
    {
        FirstName = "Ravi",
        LastName = "Kumar",
        DateOfBirth = dob,
        Gender = "Male",
        PrimaryPhone = phone,
        Email = email,
        AddressLine1 = "12 MG Rd",
        City = "Bengaluru",
        State = "KA",
        PostalCode = "560001",
        Country = "IN",
        PrimaryBranchId = branchId,
    };

    [Fact]
    public async Task CreateAsync_creates_draft_and_emits_audit_with_public_code()
    {
        var branchId = Guid.NewGuid();
        var (svc, audit, _, _) = Build();

        var result = await svc.CreateAsync(NewCreate(branchId), Admin(), CancellationToken.None);

        result.Patient.Status.Should().Be("Draft");
        result.Patient.Code.Should().MatchRegex("^PAT-\\d{4}-[A-Z]{3}-\\d{6}$");
        result.Patient.PrimaryBranchId.Should().Be(branchId);
        await audit.Received().PublishAsync(
            Arg.Is<AuditEventDto>(e => e.EntityType == "Patient" && e.Action == "Patient.Created"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_throws_BranchUnknown_for_inactive_branch()
    {
        var (svc, _, _, _) = Build(branchActive: false);

        Func<Task> act = () => svc.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.BranchUnknown);
        ex.Which.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task CreateAsync_throws_DuplicateIdentity_on_phone_DOB_collision()
    {
        var branchId = Guid.NewGuid();
        var (svc, _, _, _) = Build();
        await svc.CreateAsync(NewCreate(branchId, phone: "+91-9000000099", email: "a@x.com"), Admin(), CancellationToken.None);

        Func<Task> act = () => svc.CreateAsync(
            NewCreate(branchId, phone: "+91-9000000099", email: "b@x.com"),
            Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.DuplicateIdentity);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_missing()
    {
        var (svc, _, _, _) = Build();
        var got = await svc.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);
        got.Should().BeNull();
    }

    [Fact]
    public async Task PatchAsync_updates_fields_and_emits_audit()
    {
        var (svc, audit, _, _) = Build();
        var created = await svc.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);

        var patched = await svc.PatchAsync(created.Patient.Id,
            new PatientPatchRequest { FirstName = "Raj" }, ifMatch: null, Admin(), CancellationToken.None);

        patched.Patient.FirstName.Should().Be("Raj");
        await audit.Received().PublishAsync(
            Arg.Is<AuditEventDto>(e => e.Action == "Patient.Updated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatchAsync_throws_NotFound_for_unknown_id()
    {
        var (svc, _, _, _) = Build();
        Func<Task> act = () => svc.PatchAsync(Guid.NewGuid(), new PatientPatchRequest { FirstName = "X" }, null, Admin(), CancellationToken.None);
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task PatchAsync_throws_EtagMismatch_for_wrong_if_match()
    {
        var (svc, _, _, _) = Build();
        var created = await svc.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);

        Func<Task> act = () => svc.PatchAsync(created.Patient.Id,
            new PatientPatchRequest { FirstName = "X" }, ifMatch: "\"BAD=\"", Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.EtagMismatch);
    }

    [Fact]
    public async Task ActivateAsync_draft_to_active_emits_state_change_and_auto_links_primary()
    {
        var branchId = Guid.NewGuid();
        var (svc, audit, _, _) = Build();
        var created = await svc.CreateAsync(NewCreate(branchId), Admin(), CancellationToken.None);

        var activated = await svc.ActivateAsync(created.Patient.Id, Admin(), CancellationToken.None);

        activated.Patient.Status.Should().Be("Active");
        await audit.Received().PublishAsync(
            Arg.Is<AuditEventDto>(e => e.Action == "Patient.StateChanged"),
            Arg.Any<CancellationToken>());
        var links = await svc.ListBranchesAsync(created.Patient.Id, CancellationToken.None);
        links.Should().ContainSingle(l => l.BranchId == branchId && l.IsPrimary);
    }

    [Fact]
    public async Task ActivateAsync_archived_throws_lifecycle_invalid_transition()
    {
        var (svc, _, _, _) = Build();
        var created = await svc.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);
        await svc.ArchiveAsync(created.Patient.Id, Admin(), CancellationToken.None);

        Func<Task> act = () => svc.ActivateAsync(created.Patient.Id, Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.LifecycleInvalidTransition);
    }

    [Fact]
    public async Task ArchiveAsync_sets_archived_and_archivedAtUtc()
    {
        var (svc, _, _, repo) = Build();
        var created = await svc.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);

        await svc.ArchiveAsync(created.Patient.Id, Admin(), CancellationToken.None);

        var reloaded = await repo.GetByIdNoTrackingAsync(created.Patient.Id, CancellationToken.None);
        reloaded!.Status.Should().Be(PatientStatus.Archived);
        reloaded.ArchivedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task LinkBranchAsync_creates_link_and_promotes_primary_when_requested()
    {
        var primary = Guid.NewGuid();
        var other = Guid.NewGuid();
        var (svc, _, _, _) = Build();
        var created = await svc.CreateAsync(NewCreate(primary), Admin(), CancellationToken.None);
        await svc.ActivateAsync(created.Patient.Id, Admin(), CancellationToken.None);

        var link = await svc.LinkBranchAsync(created.Patient.Id,
            new PatientBranchLinkRequest { BranchId = other, IsPrimary = true }, Admin(), CancellationToken.None);

        link.BranchId.Should().Be(other);
        link.IsPrimary.Should().BeTrue();

        var links = await svc.ListBranchesAsync(created.Patient.Id, CancellationToken.None);
        links.Where(l => l.IsPrimary).Should().ContainSingle(l => l.BranchId == other);
    }

    [Fact]
    public async Task LinkBranchAsync_throws_AlreadyLinked_when_active_link_exists()
    {
        var primary = Guid.NewGuid();
        var (svc, _, _, _) = Build();
        var created = await svc.CreateAsync(NewCreate(primary), Admin(), CancellationToken.None);
        await svc.ActivateAsync(created.Patient.Id, Admin(), CancellationToken.None);

        Func<Task> act = () => svc.LinkBranchAsync(created.Patient.Id,
            new PatientBranchLinkRequest { BranchId = primary, IsPrimary = false }, Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.AlreadyLinked);
    }

    [Fact]
    public async Task LinkBranchAsync_throws_BranchUnknown_for_inactive_branch()
    {
        var (svc, _, branches, _) = Build();
        var created = await svc.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);
        branches.GetBranchAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BranchSummary?>(null));

        Func<Task> act = () => svc.LinkBranchAsync(created.Patient.Id,
            new PatientBranchLinkRequest { BranchId = Guid.NewGuid(), IsPrimary = false }, Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.BranchUnknown);
    }

    [Fact]
    public async Task UnlinkBranchAsync_blocks_unlinking_primary_of_active_patient()
    {
        var primary = Guid.NewGuid();
        var (svc, _, _, _) = Build();
        var created = await svc.CreateAsync(NewCreate(primary), Admin(), CancellationToken.None);
        await svc.ActivateAsync(created.Patient.Id, Admin(), CancellationToken.None);

        Func<Task> act = () => svc.UnlinkBranchAsync(created.Patient.Id, primary, Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.PrimaryBranchRequired);
    }

    [Fact]
    public async Task UnlinkBranchAsync_is_idempotent_for_unknown_branch_link()
    {
        var (svc, audit, _, _) = Build();
        var created = await svc.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);
        audit.ClearReceivedCalls();

        await svc.UnlinkBranchAsync(created.Patient.Id, Guid.NewGuid(), Admin(), CancellationToken.None);

        await audit.DidNotReceive().PublishAsync(Arg.Any<AuditEventDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_filters_by_status()
    {
        var (svc, _, _, _) = Build();
        var a = await svc.CreateAsync(NewCreate(Guid.NewGuid(), phone: "+91-1000000001", email: "a@x.com"), Admin(), CancellationToken.None);
        var b = await svc.CreateAsync(NewCreate(Guid.NewGuid(), phone: "+91-1000000002", email: "b@x.com"), Admin(), CancellationToken.None);
        await svc.ArchiveAsync(b.Patient.Id, Admin(), CancellationToken.None);

        var drafts = await svc.ListAsync(PatientStatus.Draft, null, null, 1, 10, CancellationToken.None);
        var archived = await svc.ListAsync(PatientStatus.Archived, null, null, 1, 10, CancellationToken.None);

        drafts.TotalCount.Should().Be(1);
        drafts.Items.Single().Id.Should().Be(a.Patient.Id);
        archived.TotalCount.Should().Be(1);
        archived.Items.Single().Id.Should().Be(b.Patient.Id);
    }
}
