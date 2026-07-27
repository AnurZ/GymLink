namespace GymLink.Infrastructure.Seeding;

internal sealed class DevelopmentSeedOptions
{
    public const string SectionName = "Seed";

    public bool Enabled { get; init; }
    public string DefaultPassword { get; init; } = "Test123!";
}
