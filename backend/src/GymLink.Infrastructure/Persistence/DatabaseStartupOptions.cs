namespace GymLink.Infrastructure.Persistence;

public sealed class DatabaseStartupOptions
{
    public const string SectionName = "Database";

    public bool MigrateOnStartup { get; init; }
}
