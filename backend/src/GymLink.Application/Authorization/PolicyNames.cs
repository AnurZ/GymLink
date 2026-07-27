namespace GymLink.Application.Authorization;

public static class PolicyNames
{
    public const string CentralAdminOnly = nameof(CentralAdminOnly);
    public const string TenantGymAdmin = nameof(TenantGymAdmin);
    public const string TenantTrainer = nameof(TenantTrainer);
    public const string TenantStaff = nameof(TenantStaff);
    public const string MemberSelf = nameof(MemberSelf);
}
