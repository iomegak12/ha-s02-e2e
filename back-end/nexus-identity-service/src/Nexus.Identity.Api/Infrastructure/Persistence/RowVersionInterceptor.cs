using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nexus.Identity.Api.Infrastructure.Persistence.Entities;

namespace Nexus.Identity.Api.Infrastructure.Persistence;

/// <summary>
/// Stamps <c>CreatedAtUtc</c> / <c>UpdatedAtUtc</c> on tracked entities. SQL Server's
/// native <c>rowversion</c> column is updated by the database; this interceptor only
/// handles the audit timestamps so callers do not have to set them by hand.
/// </summary>
public sealed class RowVersionInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Stamp(DbContext? context)
    {
        if (context is null) return;
        var now = DateTime.UtcNow;
        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is Admin admin)
            {
                if (entry.State == EntityState.Added)
                {
                    if (admin.CreatedAtUtc == default) admin.CreatedAtUtc = now;
                    admin.UpdatedAtUtc = now;
                }
                else if (entry.State == EntityState.Modified)
                {
                    admin.UpdatedAtUtc = now;
                }
            }
        }
    }
}
