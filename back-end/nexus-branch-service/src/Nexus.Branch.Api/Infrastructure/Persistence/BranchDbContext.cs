using Microsoft.EntityFrameworkCore;
using Nexus.Branches.Api.Features.Branches.Models;
using Nexus.Branches.Api.Infrastructure.Audit.Outbox;

namespace Nexus.Branches.Api.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Nexus HA Branch service. Targets the
/// <c>NexusBranches</c> database on the shared SQL Server host.
/// </summary>
public class BranchDbContext : DbContext
{
    /// <summary>Creates a new <see cref="BranchDbContext"/>.</summary>
    public BranchDbContext(DbContextOptions<BranchDbContext> options) : base(options) { }

    /// <summary>Hospital branches.</summary>
    public DbSet<Branch> Branches => Set<Branch>();

    /// <summary>Pending audit outbox rows (drained by <c>PendingAuditWorker</c>).</summary>
    public DbSet<PendingAuditEntry> PendingAuditEntries => Set<PendingAuditEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Branch>(b =>
        {
            b.ToTable("Branches");
            b.HasKey(x => x.Id);

            b.Property(x => x.Code).HasMaxLength(16).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.City).HasMaxLength(100);
            b.Property(x => x.IsActive).IsRequired();
            b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
            b.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
            b.Property(x => x.RowVersion).IsRowVersion();

            // Filtered-unique on Code so a code can be re-used after deactivation.
            b.HasIndex(x => x.Code)
                .HasDatabaseName("IX_Branches_Code_Active")
                .IsUnique()
                .HasFilter("[IsActive] = 1");

            b.HasIndex(x => x.IsActive).HasDatabaseName("IX_Branches_IsActive");
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
