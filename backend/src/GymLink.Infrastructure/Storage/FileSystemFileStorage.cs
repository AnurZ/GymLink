using GymLink.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GymLink.Infrastructure.Storage;

internal sealed class FileSystemFileStorage : IFileStorage
{
    private readonly IReadOnlyDictionary<FileStorageArea, StorageAreaSettings> areas;

    public FileSystemFileStorage(
        IOptions<FileStorageOptions> options,
        IHostEnvironment environment)
    {
        var settings = options.Value;
        areas = new Dictionary<FileStorageArea, StorageAreaSettings>
        {
            [FileStorageArea.TrainerImages] = CreateSettings(
                settings.RootPath,
                settings.RequestPath,
                environment.ContentRootPath),
            [FileStorageArea.GymImages] = CreateSettings(
                settings.GymRootPath,
                settings.GymRequestPath,
                environment.ContentRootPath),
        };
        foreach (var area in areas.Values)
        {
            Directory.CreateDirectory(area.RootPath);
        }
    }

    public async Task<FileStorageResult> SaveAsync(
        FileStorageArea area,
        Stream content,
        string contentType,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        _ = fileName;
        var settings = GetArea(area);
        var storageKey = $"{Guid.NewGuid():N}{ExtensionFor(contentType)}";
        var destination = ResolveStorageKey(settings.RootPath, storageKey);

        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await content.CopyToAsync(output, cancellationToken);
        return new FileStorageResult(
            storageKey,
            $"{settings.RequestPath}/{storageKey}");
    }

    public Task DeleteAsync(
        FileStorageArea area,
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveStorageKey(GetArea(area).RootPath, storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private static string ResolveStorageKey(string rootPath, string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) ||
            !string.Equals(storageKey, Path.GetFileName(storageKey), StringComparison.Ordinal) ||
            storageKey.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("The storage key is invalid.", nameof(storageKey));
        }

        var path = Path.GetFullPath(Path.Combine(rootPath, storageKey));
        var requiredPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        if (!path.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The storage key is outside the configured root.", nameof(storageKey));
        }

        return path;
    }

    private StorageAreaSettings GetArea(FileStorageArea area) =>
        areas.TryGetValue(area, out var settings)
            ? settings
            : throw new ArgumentOutOfRangeException(nameof(area));

    private static StorageAreaSettings CreateSettings(
        string configuredRootPath,
        string configuredRequestPath,
        string contentRootPath) =>
        new(
            Path.GetFullPath(
                Path.IsPathRooted(configuredRootPath)
                    ? configuredRootPath
                    : Path.Combine(contentRootPath, configuredRootPath)),
            configuredRequestPath.TrimEnd('/'));

    private static string ExtensionFor(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => throw new ArgumentException("The content type is not supported.", nameof(contentType)),
    };

    private sealed record StorageAreaSettings(string RootPath, string RequestPath);
}
