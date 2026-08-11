using Fakturenn.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fakturenn.Infrastructure.Persistence;

/// <summary>
/// Fills <see cref="IAuditable"/> fields on save, so no entity code sets them by
/// hand and none can forget to.
/// <para>
/// Takes <see cref="IClock"/> rather than reading the clock directly, so a test can
/// assert an exact timestamp instead of a tolerance window.
/// </para>
/// </summary>
public sealed class AuditSaveChangesInterceptor(IClock clock, ICurrentUserAccessor currentUser)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyAuditFields(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditFields(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAuditFields(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        DateTimeOffset now = clock.UtcNow;
        string user = AuditStamp.ResolveUser(currentUser.UserName);

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    (DateTimeOffset createdAt, string createdBy) = AuditStamp.ForAdded(
                        entry.Entity.CreatedAt, entry.Entity.CreatedBy, now, user);

                    entry.Entity.CreatedAt = createdAt;
                    entry.Entity.CreatedBy = createdBy;
                    entry.Entity.ModifiedAt = now;
                    entry.Entity.ModifiedBy = user;
                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedAt = now;
                    entry.Entity.ModifiedBy = user;

                    // Creation provenance is a fact about the past. Stop EF writing it
                    // again even if something in the graph changed the property.
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                    break;

                default:
                    break;
            }
        }
    }
}
