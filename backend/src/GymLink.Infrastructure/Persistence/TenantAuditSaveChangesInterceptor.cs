using GymLink.Application.Abstractions;
using GymLink.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GymLink.Infrastructure.Persistence;

public sealed class TenantAuditSaveChangesInterceptor(
    ITenantContext tenantContext,
    ITenantMutationScope tenantMutationScope,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
    : SaveChangesInterceptor
{
    public TenantAuditSaveChangesInterceptor(
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
        : this(tenantContext, new NoTenantMutationScope(), currentUser, timeProvider)
    {
    }

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
                if (entry.State == EntityState.Added &&
                    tenantOwned.TenantId == Guid.Empty &&
                    tenantContext.TenantId.HasValue)
                {
                    tenantOwned.TenantId = tenantContext.TenantId.Value;
                }

                if ((!tenantContext.TenantId.HasValue ||
                     tenantOwned.TenantId != tenantContext.TenantId.Value) &&
                    !tenantMutationScope.Allows(tenantOwned.TenantId))
                {
                    throw new InvalidOperationException("Cross-tenant writes are not permitted.");
                }

                if (entry.State is EntityState.Modified or EntityState.Deleted)
                {
                    var originalTenantId = entry.Property(nameof(ITenantOwned.TenantId)).OriginalValue;
                    if (originalTenantId is Guid original &&
                        ((!tenantContext.TenantId.HasValue ||
                          original != tenantContext.TenantId.Value) &&
                         !tenantMutationScope.Allows(original)))
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

    private sealed class NoTenantMutationScope : ITenantMutationScope
    {
        public bool Allows(Guid tenantId) => false;

        public IDisposable Begin(params Guid[] tenantIds) =>
            throw new NotSupportedException();
    }
}
