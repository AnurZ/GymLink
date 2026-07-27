namespace GymLink.Domain.Common;

public static class RoleNames
{
    public const string CentralAdmin = nameof(CentralAdmin);
    public const string GymAdmin = nameof(GymAdmin);
    public const string Trainer = nameof(Trainer);
    public const string Member = nameof(Member);

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            CentralAdmin,
            GymAdmin,
            Trainer,
            Member,
        };
}
