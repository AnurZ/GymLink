using GymLink.Application.Abstractions;
using GymLink.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GymLink.Infrastructure.Persistence;

public sealed class TenantAuditSaveChangesInterceptor(
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        GuardAndStamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        GuardAndStamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    internal void GuardAndStamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged)
            {
                continue;
            }

            if (entry.Entity is ITenantOwned tenantOwned)
            {
                if (!tenantContext.TenantId.HasValue)
                {
                    throw new InvalidOperationException("A tenant context is required for tenant-owned writes.");
                }

                if (entry.State == EntityState.Added && tenantOwned.TenantId == Guid.Empty)
                {
                    tenantOwned.TenantId = tenantContext.TenantId.Value;
                }

                if (tenantOwned.TenantId != tenantContext.TenantId.Value)
                {
                    throw new InvalidOperationException("Cross-tenant writes are not permitted.");
                }

                if (entry.State is EntityState.Modified or EntityState.Deleted)
                {
                    var originalTenantId = entry.Property(nameof(ITenantOwned.TenantId)).OriginalValue;
                    if (originalTenantId is Guid original && original != tenantContext.TenantId.Value)
                    {
                        throw new InvalidOperationException("Cross-tenant writes are not permitted.");
                    }
                }
            }

            if (entry.Entity is AuditedEntity audited)
            {
                if (entry.State == EntityState.Added)
                {
                    audited.CreatedAtUtc = now;
                    audited.CreatedByUserId = currentUser.UserId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    audited.UpdatedAtUtc = now;
                    audited.UpdatedByUserId = currentUser.UserId;
                }
            }

            var invalidDateTime = entry.Properties
                .Where(property => property.Metadata.ClrType == typeof(DateTime) ||
                    property.Metadata.ClrType == typeof(DateTime?))
                .Select(property => property.CurrentValue)
                .OfType<DateTime>()
                .FirstOrDefault(value =>
                    value != default && value.Kind != DateTimeKind.Utc);
            if (invalidDateTime != default)
            {
                throw new InvalidOperationException("All DateTime values must use UTC.");
            }
        }
    }
}
