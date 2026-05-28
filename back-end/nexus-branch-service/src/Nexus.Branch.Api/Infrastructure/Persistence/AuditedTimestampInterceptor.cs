using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Nexus.Branches.Api.Infrastructure.Persistence;

/// <summary>
/// Stamps <c>CreatedAtUtc</c> / <c>UpdatedAtUtc</c> on tracked entities that implement
/// <see cref="IAuditedEntity"/>. SQL Server's native <c>rowversion</c> column is updated
/// by the database itself; this interceptor only handles the audit timestamps so callers
/// do not have to set them by hand.
/// </summary>
public sealed class AuditedTimestampInterceptor : SaveChangesInterceptor
{
    private readonly TimeProvider _clock;

    /// <summary>Create the interceptor.</summary>
    public AuditedTimestampInterceptor(TimeProvider clock)
    {
        _clock = clock;
    }

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

    private void Stamp(DbContext? context)
    {
        if (context is null) return;
        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is IAuditedEntity audited)
            {
                if (entry.State == EntityState.Added)
                {
                    if (audited.CreatedAtUtc == default) audited.CreatedAtUtc = now;
                    audited.UpdatedAtUtc = now;
                }
                else if (entry.State == EntityState.Modified)
                {
                    audited.UpdatedAtUtc = now;
                }
            }
        }
    }
}
