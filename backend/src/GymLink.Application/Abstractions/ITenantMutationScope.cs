namespace GymLink.Application.Abstractions;

public interface ITenantMutationScope
{
    bool Allows(Guid tenantId);
    IDisposable Begin(params Guid[] tenantIds);
}
