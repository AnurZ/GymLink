using GymLink.Application.Abstractions;

namespace GymLink.Worker;

internal sealed class WorkerTenantContext : ITenantContext, ICurrentUser, IRequestMetadata
{
    public Guid? TenantId => null;
    public string? TenantRole => null;
    public bool HasTenant => false;
    public Guid? UserId => null;
    public bool IsAuthenticated => false;
    public string CorrelationId => "payment-reconciliation";
    public string? RemoteIpAddress => null;
}
