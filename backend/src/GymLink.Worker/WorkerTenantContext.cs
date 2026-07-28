using GymLink.Application.Abstractions;

namespace GymLink.Worker;

internal sealed class WorkerTenantContext : ITenantContext
{
    public Guid? TenantId => null;
    public string? TenantRole => null;
    public bool HasTenant => false;
}
