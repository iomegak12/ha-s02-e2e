using Microsoft.EntityFrameworkCore;
using Nexus.Doctors.Api.Features.Doctors.Models;
using Nexus.Doctors.Api.Infrastructure.Audit.Outbox;

namespace Nexus.Doctors.Api.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Nexus HA Doctor service. Targets the
/// <c>NexusDoctors</c> database on the shared SQL Server host.
/// </summary>
public class DoctorDbContext : DbContext
{
    /// <summary>Creates a new <see cref="DoctorDbContext"/>.</summary>
    public DoctorDbContext(DbContextOptions<DoctorDbContext> options) : base(options) { }

    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<DoctorDocument> DoctorDocuments => Set<DoctorDocument>();
    public DbSet<DoctorBranchLink> DoctorBranchLinks => Set<DoctorBranchLink>();
    public DbSet<DoctorCodeSequence> DoctorCodeSequences => Set<DoctorCodeSequence>();
    public DbSet<PendingAuditEntry> PendingAuditEntries => Set<PendingAuditEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Doctor>(b =>
        {
            b.ToTable("Doctors");
            b.HasKey(x => x.Id);

            b.Property(x => x.PublicCode).HasMaxLength(32).IsRequired();
            b.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            b.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Phone).HasMaxLength(32);
            b.Property(x => x.Email).HasMaxLength(254);
            b.Property(x => x.Specialisation).HasMaxLength(100).IsRequired();
            b.Property(x => x.LicenseNumber).HasMaxLength(64).IsRequired();
            b.Property(x => x.LicenseAuthority).HasMaxLength(100);
            b.Property(x => x.PrimaryBranchId).IsRequired();
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
            b.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
            b.Property(x => x.RowVersion).IsRowVersion();

            b.HasIndex(x => x.PublicCode).HasDatabaseName("UX_Doctors_PublicCode").IsUnique();

            // License unique while not Deactivated (filtered).
            b.HasIndex(x => x.LicenseNumber)
                .HasDatabaseName("UX_Doctors_License_Active")
                .IsUnique()
                .HasFilter("[Status] <> 4");
        });

        modelBuilder.Entity<DoctorDocument>(b =>
        {
            b.ToTable("DoctorDocuments");
            b.HasKey(x => x.Id);

            b.Property(x => x.DoctorId).IsRequired();
            b.Property(x => x.Kind).HasMaxLength(32).IsRequired();
            b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            b.Property(x => x.ContentType).HasMaxLength(64).IsRequired();
            b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            b.Property(x => x.SizeBytes).IsRequired();
            b.Property(x => x.StorageKey).HasMaxLength(255).IsRequired();
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.IsRequired).IsRequired();
            b.Property(x => x.ReviewNote).HasMaxLength(500);
            b.Property(x => x.ReviewedAtUtc).HasColumnType("datetime2(7)");
            b.Property(x => x.ReviewedByUsername).HasMaxLength(64);
            b.Property(x => x.UploadedAtUtc).HasColumnType("datetime2(7)").IsRequired();
            b.Property(x => x.DeletedAtUtc).HasColumnType("datetime2(7)");

            // SHA-256 dedup, filtered on not-deleted.
            b.HasIndex(x => new { x.DoctorId, x.Sha256 })
                .HasDatabaseName("UX_DoctorDocuments_Doctor_Sha")
                .IsUnique()
                .HasFilter("[DeletedAtUtc] IS NULL");

            b.HasIndex(x => x.DoctorId).HasDatabaseName("IX_DoctorDocuments_DoctorId");

            b.HasOne(x => x.Doctor)
                .WithMany(d => d.Documents)
                .HasForeignKey(x => x.DoctorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DoctorBranchLink>(b =>
        {
            b.ToTable("DoctorBranchLinks");
            b.HasKey(x => new { x.DoctorId, x.BranchId });

            b.Property(x => x.IsPrimary).IsRequired();
            b.Property(x => x.LinkedAtUtc).HasColumnType("datetime2(7)").IsRequired();
            b.Property(x => x.UnlinkedAtUtc).HasColumnType("datetime2(7)");

            b.HasIndex(x => new { x.DoctorId, x.IsPrimary, x.UnlinkedAtUtc })
                .HasDatabaseName("IX_DoctorBranchLinks_DoctorId_IsPrimary_Unlinked");

            b.HasOne(x => x.Doctor)
                .WithMany(d => d.BranchLinks)
                .HasForeignKey(x => x.DoctorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DoctorCodeSequence>(b =>
        {
            b.ToTable("DoctorCodeSequences");
            b.HasKey(x => new { x.BranchCode, x.Year });
            b.Property(x => x.BranchCode).HasMaxLength(16).IsRequired();
            b.Property(x => x.Year).IsRequired();
            b.Property(x => x.LastValue).IsRequired();
        });

        modelBuilder.Entity<PendingAuditEntry>(b =>
        {
            b.ToTable("PendingAuditEntries");
            b.HasKey(x => x.Id);
            b.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
            b.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
            b.Property(x => x.Attempts).IsRequired();
            b.Property(x => x.NextRetryUtc).HasColumnType("datetime2(7)").IsRequired();
            b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
            b.Property(x => x.LastError).HasMaxLength(1024);

            b.HasIndex(x => x.IdempotencyKey).HasDatabaseName("UX_PendingAudit_IdempotencyKey").IsUnique();
            b.HasIndex(x => x.NextRetryUtc).HasDatabaseName("IX_PendingAudit_NextRetryUtc");
        });
    }
}
