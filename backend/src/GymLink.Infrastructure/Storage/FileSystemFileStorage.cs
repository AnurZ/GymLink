using GymLink.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GymLink.Infrastructure.Storage;

internal sealed class FileSystemFileStorage : IFileStorage
{
    private readonly string rootPath;
    private readonly string requestPath;

    public FileSystemFileStorage(
        IOptions<FileStorageOptions> options,
        IHostEnvironment environment)
    {
        var settings = options.Value;
        rootPath = Path.GetFullPath(
            Path.IsPathRooted(settings.RootPath)
                ? settings.RootPath
                : Path.Combine(environment.ContentRootPath, settings.RootPath));
        requestPath = settings.RequestPath.TrimEnd('/');
        Directory.CreateDirectory(rootPath);
    }

    public async Task<FileStorageResult> SaveAsync(
        Stream content,
        string contentType,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        _ = fileName;
        var storageKey = $"{Guid.NewGuid():N}{ExtensionFor(contentType)}";
        var destination = ResolveStorageKey(storageKey);

        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await content.CopyToAsync(output, cancellationToken);
        return new FileStorageResult(storageKey, $"{requestPath}/{storageKey}");
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveStorageKey(storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string ResolveStorageKey(string storageKey)
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

    private static string ExtensionFor(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => throw new ArgumentException("The content type is not supported.", nameof(contentType)),
    };
}
