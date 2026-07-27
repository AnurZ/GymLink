using GymLink.Application.Abstractions;

namespace GymLink.Infrastructure.Security;

internal sealed class TenantMutationScope : ITenantMutationScope
{
    private HashSet<Guid>? tenantIds;

    public bool Allows(Guid tenantId) => tenantIds?.Contains(tenantId) == true;

    public IDisposable Begin(params Guid[] tenantIds)
    {
        if (tenantIds.Length == 0 || tenantIds.Any(x => x == Guid.Empty))
        {
            throw new ArgumentException("At least one valid tenant ID is required.", nameof(tenantIds));
        }

        if (this.tenantIds is not null)
        {
            throw new InvalidOperationException("A tenant mutation scope is already active.");
        }

        this.tenantIds = tenantIds.ToHashSet();
        return new ScopeLease(this);
    }

    private sealed class ScopeLease(TenantMutationScope owner) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (!disposed)
            {
                owner.tenantIds = null;
                disposed = true;
            }
        }
    }
}
