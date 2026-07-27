using GymLink.Application.Abstractions;

namespace GymLink.IntegrationTests;

internal sealed record TestTenantContext(Guid? TenantId, string? TenantRole = null) : ITenantContext
{
    public bool HasTenant => TenantId.HasValue;
}

internal sealed record TestCurrentUser(Guid? UserId) : ICurrentUser
{
    public bool IsAuthenticated => UserId.HasValue;
}
