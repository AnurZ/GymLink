namespace GymLink.Infrastructure.Storage;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";
    public const string DefaultRequestPath = "/uploads/trainer-images";
    public const string DefaultGymRequestPath = "/uploads/gym-images";

    public string RootPath { get; init; } = string.Empty;
    public string RequestPath { get; init; } = DefaultRequestPath;
    public string GymRootPath { get; init; } = string.Empty;
    public string GymRequestPath { get; init; } = DefaultGymRequestPath;
    public string ChatRootPath { get; init; } = string.Empty;
}
