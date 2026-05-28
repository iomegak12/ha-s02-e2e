using Microsoft.EntityFrameworkCore;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;

namespace Nexus.Identity.Api.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the Nexus HA Identity service. Targets the
/// <c>NexusIdentity</c> database on the shared SQL Server host.
/// </summary>
public class IdentityDbContext : DbContext
{
    /// <summary>Creates a new <see cref="IdentityDbContext"/>.</summary>
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options) { }

    /// <summary>Hospital administrators.</summary>
    public DbSet<Admin> Admins => Set<Admin>();

    /// <summary>Outstanding refresh tokens.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>RSA signing keys for JWT issuance.</summary>
    public DbSet<SigningKey> SigningKeys => Set<SigningKey>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Admin>(b =>
        {
            b.ToTable("Admins");
            b.HasKey(x => x.Id);
            b.Property(x => x.Username).HasMaxLength(64).IsRequired();
            b.HasIndex(x => x.Username).IsUnique();
            b.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
            b.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            b.Property(x => x.IsActive).IsRequired();
            b.Property(x => x.CreatedAtUtc).IsRequired();
            b.Property(x => x.UpdatedAtUtc).IsRequired();
            b.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.ToTable("RefreshTokens");
            b.HasKey(x => x.Id);
            b.Property(x => x.TokenHash).HasColumnType("varbinary(32)").IsRequired();
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.Property(x => x.IssuedAtUtc).IsRequired();
            b.Property(x => x.ExpiresAtUtc).IsRequired();
            b.HasIndex(x => x.AdminId);
            b.HasOne(x => x.Admin)
                .WithMany(a => a.RefreshTokens)
                .HasForeignKey(x => x.AdminId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SigningKey>(b =>
        {
            b.ToTable("SigningKeys");
            b.HasKey(x => x.Kid);
            b.Property(x => x.Kid).HasMaxLength(64);
            b.Property(x => x.Algorithm).HasMaxLength(16).IsRequired();
            b.Property(x => x.PublicJwkJson).IsRequired();
            b.Property(x => x.PrivateKeyPemEncrypted).IsRequired();
            b.Property(x => x.CreatedAtUtc).IsRequired();
            b.Property(x => x.ActivatedAtUtc).IsRequired();
        });
    }
}
