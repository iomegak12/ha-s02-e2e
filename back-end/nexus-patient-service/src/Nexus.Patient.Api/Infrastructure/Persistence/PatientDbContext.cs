using Microsoft.EntityFrameworkCore;
using Nexus.Patients.Api.Features.Patients.Models;
using Nexus.Patients.Api.Infrastructure.Audit.Outbox;

namespace Nexus.Patients.Api.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Nexus HA Patient service. Targets the
/// <c>NexusPatients</c> database on the shared SQL Server host.
/// </summary>
public class PatientDbContext : DbContext
{
    /// <summary>Creates a new <see cref="PatientDbContext"/>.</summary>
    public PatientDbContext(DbContextOptions<PatientDbContext> options) : base(options) { }

    /// <summary>Patient records.</summary>
    public DbSet<Patient> Patients => Set<Patient>();

    /// <summary>Patient ↔ branch associations (live + soft-deleted).</summary>
    public DbSet<PatientBranchLink> PatientBranchLinks => Set<PatientBranchLink>();

    /// <summary>Per-branch / per-year monotonic counters for public-code generation.</summary>
    public DbSet<PatientCodeSequence> PatientCodeSequences => Set<PatientCodeSequence>();

    /// <summary>Pending audit outbox rows (Phase 3 reconciler drains them).</summary>
    public DbSet<PendingAuditEntry> PendingAuditEntries => Set<PendingAuditEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Patient>(b =>
        {
            b.ToTable("Patients");
            b.HasKey(x => x.Id);

            b.Property(x => x.PublicCode).HasMaxLength(32).IsRequired();
            b.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            b.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Phone).HasMaxLength(32);
            b.Property(x => x.Email).HasMaxLength(254);
            b.Property(x => x.DateOfBirth).HasColumnType("date").IsRequired();
            b.Property(x => x.Gender).HasMaxLength(16).IsRequired();
            b.Property(x => x.AddressLine1).HasMaxLength(200);
            b.Property(x => x.AddressLine2).HasMaxLength(200);
            b.Property(x => x.City).HasMaxLength(100);
            b.Property(x => x.State).HasMaxLength(100);
            b.Property(x => x.PostalCode).HasMaxLength(20);
            b.Property(x => x.Country).HasMaxLength(2);
            b.Property(x => x.PrimaryBranchId).IsRequired();
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.ArchivedAtUtc).HasColumnType("datetime2(7)");
            b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
            b.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
            b.Property(x => x.RowVersion).IsRowVersion();

            b.HasIndex(x => x.PublicCode).HasDatabaseName("UX_Patients_PublicCode").IsUnique();

            // Duplicate detection: phone + DOB and email + DOB. Filtered so blank
            // phones / emails don't collide.
            b.HasIndex(x => new { x.Phone, x.DateOfBirth })
                .HasDatabaseName("UX_Patients_Phone_DOB")
                .IsUnique()
                .HasFilter("[Phone] IS NOT NULL");
            b.HasIndex(x => new { x.Email, x.DateOfBirth })
                .HasDatabaseName("UX_Patients_Email_DOB")
                .IsUnique()
                .HasFilter("[Email] IS NOT NULL");

            // Purge filter index.
            b.HasIndex(x => new { x.Status, x.ArchivedAtUtc })
                .HasDatabaseName("IX_Patients_Status_ArchivedAtUtc");
        });

        modelBuilder.Entity<PatientBranchLink>(b =>
        {
            b.ToTable("PatientBranchLinks");
            b.HasKey(x => new { x.PatientId, x.BranchId });

            b.Property(x => x.IsPrimary).IsRequired();
            b.Property(x => x.LinkedAtUtc).HasColumnType("datetime2(7)").IsRequired();
            b.Property(x => x.UnlinkedAtUtc).HasColumnType("datetime2(7)");

            // Index used by the primary-branch invariant enforcement query.
            b.HasIndex(x => new { x.PatientId, x.IsPrimary, x.UnlinkedAtUtc })
                .HasDatabaseName("IX_PatientBranchLinks_PatientId_IsPrimary_Unlinked");

            b.HasOne(x => x.Patient)
                .WithMany(p => p.BranchLinks)
                .HasForeignKey(x => x.PatientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PatientCodeSequence>(b =>
        {
            b.ToTable("PatientCodeSequences");
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
