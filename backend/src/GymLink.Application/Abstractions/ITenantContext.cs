namespace GymLink.Application.Abstractions;

public interface ITenantContext
{
    Guid? TenantId { get; }
    string? TenantRole { get; }
    bool HasTenant { get; }
}
