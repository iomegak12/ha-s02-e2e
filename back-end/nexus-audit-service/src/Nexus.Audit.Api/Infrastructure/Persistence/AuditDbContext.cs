using Microsoft.EntityFrameworkCore;
using Nexus.Audit.Api.Features.Audit.Models;

namespace Nexus.Audit.Api.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Nexus HA Audit service. Targets the
/// <c>NexusAudit</c> database on the shared SQL Server host. Entities are
/// immutable after insert — no <c>Update</c> paths are exposed.
/// </summary>
public class AuditDbContext : DbContext
{
    /// <summary>Creates a new <see cref="AuditDbContext"/>.</summary>
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

    /// <summary>Append-only audit log.</summary>
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <summary>Backs the per-source-service idempotency contract.</summary>
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.ToTable("AuditEntries");
            b.HasKey(x => x.Id);

            b.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
            b.Property(x => x.EntityId).IsRequired();
            b.Property(x => x.EntityCode).HasMaxLength(64);
            b.Property(x => x.Action).HasMaxLength(32).IsRequired();
            b.Property(x => x.ActorId).IsRequired();
            b.Property(x => x.ActorUsername).HasMaxLength(64).IsRequired();
            b.Property(x => x.OccurredAtUtc).HasColumnType("datetime2(7)").IsRequired();
            b.Property(x => x.ReceivedAtUtc).HasColumnType("datetime2(7)").IsRequired();
            b.Property(x => x.Summary).HasMaxLength(512).IsRequired();
            b.Property(x => x.DiffJson).HasColumnType("nvarchar(max)");
            b.Property(x => x.SourceService).HasMaxLength(64).IsRequired();
            b.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();

            b.HasIndex(x => new { x.EntityType, x.EntityId, x.OccurredAtUtc })
                .HasDatabaseName("IX_AuditEntries_EntityTypeEntityId")
                .IsDescending(false, false, true);

            b.HasIndex(x => new { x.ActorId, x.OccurredAtUtc })
                .HasDatabaseName("IX_AuditEntries_ActorId")
                .IsDescending(false, true);

            b.HasIndex(x => x.OccurredAtUtc)
                .HasDatabaseName("IX_AuditEntries_OccurredAtUtc")
                .IsDescending(true);

            b.HasIndex(x => new { x.SourceService, x.IdempotencyKey })
                .HasDatabaseName("IX_AuditEntries_Idem")
                .IsUnique();
        });

        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("IdempotencyRecords");
            b.HasKey(x => x.Id);

            b.Property(x => x.SourceService).HasMaxLength(64).IsRequired();
            b.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
            b.Property(x => x.EntryId).IsRequired();
            b.Property(x => x.PayloadHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();

            b.HasIndex(x => new { x.SourceService, x.IdempotencyKey })
                .HasDatabaseName("IX_IdempotencyRecords_SourceServiceKey")
                .IsUnique();

            b.HasIndex(x => x.CreatedAtUtc)
                .HasDatabaseName("IX_IdempotencyRecords_CreatedAtUtc");

            b.HasOne<AuditEntry>()
                .WithMany()
                .HasForeignKey(x => x.EntryId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
