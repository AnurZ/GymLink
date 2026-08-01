namespace GymLink.Infrastructure.Storage;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";
    public const string DefaultRequestPath = "/uploads/trainer-images";

    public string RootPath { get; init; } = string.Empty;
    public string RequestPath { get; init; } = DefaultRequestPath;
}
