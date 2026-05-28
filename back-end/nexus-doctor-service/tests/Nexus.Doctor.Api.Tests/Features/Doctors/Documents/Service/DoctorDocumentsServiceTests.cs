using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.Doctors.Api.Configuration;
using Nexus.Doctors.Api.Features.Doctors.Documents.Models;
using Nexus.Doctors.Api.Features.Doctors.Documents.Repository;
using Nexus.Doctors.Api.Features.Doctors.Documents.Service;
using Nexus.Doctors.Api.Features.Doctors.Models;
using Nexus.Doctors.Api.Features.Doctors.Repository;
using Nexus.Doctors.Api.Features.Doctors.Service;
using Nexus.Doctors.Api.Infrastructure.Audit;
using Nexus.Doctors.Api.Infrastructure.Branches;
using Nexus.Doctors.Api.Infrastructure.Documents;
using Nexus.Doctors.Api.Infrastructure.Errors;
using Nexus.Doctors.Api.Tests.Infrastructure;
using NSubstitute;

namespace Nexus.Doctors.Api.Tests.Features.Doctors.Documents.Service;

public sealed class DoctorDocumentsServiceTests
{
    private static ClaimsPrincipal Admin() => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", Guid.NewGuid().ToString()),
        new Claim("preferred_username", "admin"),
        new Claim(ClaimTypes.Role, "Admin"),
    }, authenticationType: "test"));

    private sealed class FakeStorage : IDocumentStorage
    {
        public Dictionary<string, (string Sha256, long Size)> Stored { get; } = new();
        public List<string> Deleted { get; } = new();

        public async Task<StoredDocument> StoreAsync(string storageKey, Stream source, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            await source.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            Stored[storageKey] = (sha, bytes.LongLength);
            return new StoredDocument(sha, bytes.LongLength);
        }
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());
        public Task DeleteAsync(string storageKey, CancellationToken ct)
        {
            Deleted.Add(storageKey);
            Stored.Remove(storageKey);
            return Task.CompletedTask;
        }
    }

    private static (DoctorsService doctors, DoctorDocumentsService docs, FakeStorage storage,
        IAuditPublisher audit, Nexus.Doctors.Api.Infrastructure.Persistence.DoctorDbContext db) Build(long maxBytes = 1024 * 1024)
    {
        var db = InMemoryDb.Create();
        var doctorRepo = new DoctorRepository(db);
        var docRepo = new DoctorDocumentRepository(db);
        var seq = new DoctorCodeSequenceRepository(db);
        var gen = new PublicCodeGenerator(seq);
        var audit = Substitute.For<IAuditPublisher>();
        var branches = Substitute.For<IBranchesClient>();
        branches.GetBranchAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<BranchSummary?>(new BranchSummary { Id = ci.Arg<Guid>(), Code = "BLR", IsActive = true }));
        var storage = new FakeStorage();
        var opts = Options.Create(new DocumentsOptions
        {
            RootPath = "/tmp",
            MaxFileBytes = maxBytes,
            AllowedContentTypes = new[] { "application/pdf", "image/png", "image/jpeg" },
        });
        var doctorsSvc = new DoctorsService(doctorRepo, gen, branches, audit, TimeProvider.System, NullLogger<DoctorsService>.Instance);
        var docsSvc = new DoctorDocumentsService(doctorRepo, docRepo, storage, audit, opts, TimeProvider.System, NullLogger<DoctorDocumentsService>.Instance);
        return (doctorsSvc, docsSvc, storage, audit, db);
    }

    private static DoctorCreateRequest NewCreate(Guid branchId, string license = "L-1") => new()
    {
        FirstName = "Asha", LastName = "Menon", Specialty = "Cardiology",
        LicenseNumber = license, PrimaryPhone = "+91-90000", Email = "a@x.com",
        PrimaryBranchId = branchId,
    };

    private static Stream PdfStream(string content = "%PDF-1.4 hello") =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task UploadAsync_creates_document_with_sha256_and_audits()
    {
        var (doctors, docs, storage, audit, _) = Build();
        var created = await doctors.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);

        var dto = await docs.UploadAsync(created.Doctor.Id,
            DocumentKinds.MedicalLicense, "lic.pdf", "application/pdf", PdfStream(), Admin(), CancellationToken.None);

        dto.Kind.Should().Be(DocumentKinds.MedicalLicense);
        dto.ContentType.Should().Be("application/pdf");
        dto.SizeBytes.Should().BeGreaterThan(0);
        dto.Status.Should().Be("Uploaded");
        storage.Stored.Should().ContainKey(dto.StoragePath);
        await audit.Received().PublishAsync(
            Arg.Is<AuditEventDto>(e => e.Action == "DoctorDocument.Uploaded"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAsync_dedups_same_sha_within_doctor()
    {
        var (doctors, docs, _, _, _) = Build();
        var created = await doctors.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);

        var a = await docs.UploadAsync(created.Doctor.Id, DocumentKinds.MedicalLicense, "a.pdf", "application/pdf", PdfStream(), Admin(), CancellationToken.None);
        var b = await docs.UploadAsync(created.Doctor.Id, DocumentKinds.MedicalLicense, "b.pdf", "application/pdf", PdfStream(), Admin(), CancellationToken.None);

        b.Id.Should().Be(a.Id);
    }

    [Fact]
    public async Task UploadAsync_rejects_unsupported_content_type()
    {
        var (doctors, docs, _, _, _) = Build();
        var created = await doctors.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);

        Func<Task> act = () => docs.UploadAsync(created.Doctor.Id, DocumentKinds.MedicalLicense, "x.exe", "application/x-msdownload",
            PdfStream(), Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.DocumentUnsupportedType);
        ex.Which.Status.Should().Be(StatusCodes.Status415UnsupportedMediaType);
    }

    [Fact]
    public async Task UploadAsync_enforces_size_limit()
    {
        var (doctors, docs, _, _, _) = Build(maxBytes: 5);
        var created = await doctors.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);

        Func<Task> act = () => docs.UploadAsync(created.Doctor.Id, DocumentKinds.MedicalLicense, "big.pdf", "application/pdf",
            PdfStream("0123456789ABCDEF"), Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.DocumentTooLarge);
    }

    [Fact]
    public async Task UploadAsync_rejects_unknown_kind()
    {
        var (doctors, docs, _, _, _) = Build();
        var created = await doctors.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);

        Func<Task> act = () => docs.UploadAsync(created.Doctor.Id, "Bogus", "x.pdf", "application/pdf", PdfStream(), Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.DoctorValidation);
    }

    [Fact]
    public async Task UploadAsync_blocked_when_doctor_is_Verified()
    {
        var (doctors, docs, _, _, db) = Build();
        var created = await doctors.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);
        // Push the doctor straight to Verified via the seed + verify path.
        db.DoctorDocuments.Add(new DoctorDocument
        {
            Id = Guid.NewGuid(), DoctorId = created.Doctor.Id, Kind = DocumentKinds.MedicalLicense,
            FileName = "k.pdf", ContentType = "application/pdf", Sha256 = "preexisting",
            SizeBytes = 1, StorageKey = "k", Status = DocumentStatus.Verified, IsRequired = true,
            UploadedAtUtc = DateTime.UtcNow, ReviewedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        foreach (var e in db.ChangeTracker.Entries().ToList()) e.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        await doctors.VerifyAsync(created.Doctor.Id, Admin(), CancellationToken.None);

        Func<Task> act = () => docs.UploadAsync(created.Doctor.Id, DocumentKinds.GovernmentId, "id.pdf", "application/pdf",
            PdfStream("different content"), Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.DocumentLocked);
    }

    [Fact]
    public async Task ReviewAsync_verified_records_decision()
    {
        var (doctors, docs, _, audit, _) = Build();
        var created = await doctors.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);
        var dto = await docs.UploadAsync(created.Doctor.Id, DocumentKinds.MedicalLicense, "lic.pdf", "application/pdf", PdfStream(), Admin(), CancellationToken.None);

        var reviewed = await docs.ReviewAsync(created.Doctor.Id, dto.Id,
            new DocumentReviewRequest { Status = "Verified" }, Admin(), CancellationToken.None);

        reviewed.Status.Should().Be("Verified");
        await audit.Received().PublishAsync(
            Arg.Is<AuditEventDto>(e => e.Action == "DoctorDocument.Reviewed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReviewAsync_rejected_requires_reason()
    {
        var (doctors, docs, _, _, _) = Build();
        var created = await doctors.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);
        var dto = await docs.UploadAsync(created.Doctor.Id, DocumentKinds.MedicalLicense, "lic.pdf", "application/pdf", PdfStream(), Admin(), CancellationToken.None);

        Func<Task> act = () => docs.ReviewAsync(created.Doctor.Id, dto.Id,
            new DocumentReviewRequest { Status = "Rejected" }, Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.DoctorValidation);
    }

    [Fact]
    public async Task ReviewAsync_double_review_returns_LifecycleInvalidTransition()
    {
        var (doctors, docs, _, _, _) = Build();
        var created = await doctors.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);
        var dto = await docs.UploadAsync(created.Doctor.Id, DocumentKinds.MedicalLicense, "lic.pdf", "application/pdf", PdfStream(), Admin(), CancellationToken.None);
        await docs.ReviewAsync(created.Doctor.Id, dto.Id, new DocumentReviewRequest { Status = "Verified" }, Admin(), CancellationToken.None);

        Func<Task> act = () => docs.ReviewAsync(created.Doctor.Id, dto.Id,
            new DocumentReviewRequest { Status = "Rejected", RejectionReason = "x" }, Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.LifecycleInvalidTransition);
    }

    [Fact]
    public async Task DeleteAsync_soft_deletes_uploaded_document_and_calls_storage_delete()
    {
        var (doctors, docs, storage, _, _) = Build();
        var created = await doctors.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);
        var dto = await docs.UploadAsync(created.Doctor.Id, DocumentKinds.MedicalLicense, "lic.pdf", "application/pdf", PdfStream(), Admin(), CancellationToken.None);

        await docs.DeleteAsync(created.Doctor.Id, dto.Id, Admin(), CancellationToken.None);

        storage.Deleted.Should().Contain(dto.StoragePath);
        (await docs.GetAsync(created.Doctor.Id, dto.Id, CancellationToken.None)).Should().BeNull();
        (await docs.ListAsync(created.Doctor.Id, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_blocks_verified_document_with_DocumentLocked()
    {
        var (doctors, docs, _, _, _) = Build();
        var created = await doctors.CreateAsync(NewCreate(Guid.NewGuid()), Admin(), CancellationToken.None);
        var dto = await docs.UploadAsync(created.Doctor.Id, DocumentKinds.MedicalLicense, "lic.pdf", "application/pdf", PdfStream(), Admin(), CancellationToken.None);
        await docs.ReviewAsync(created.Doctor.Id, dto.Id, new DocumentReviewRequest { Status = "Verified" }, Admin(), CancellationToken.None);

        Func<Task> act = () => docs.DeleteAsync(created.Doctor.Id, dto.Id, Admin(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(ErrorCodes.DocumentLocked);
    }
}
