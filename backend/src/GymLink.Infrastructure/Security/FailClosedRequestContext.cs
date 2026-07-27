using GymLink.Application.Abstractions;

namespace GymLink.Infrastructure.Security;

internal sealed class FailClosedRequestContext : ICurrentUser, ITenantContext
{
    public Guid? UserId => null;
    public bool IsAuthenticated => false;
    public Guid? TenantId => null;
    public string? TenantRole => null;
    public bool HasTenant => false;
}
