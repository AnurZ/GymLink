namespace GymLink.Application.Abstractions;

public interface IFileStorage
{
    Task<FileStorageResult> SaveAsync(
        Stream content,
        string contentType,
        string fileName,
        CancellationToken cancellationToken);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}

public sealed record FileStorageResult(string StorageKey, string? PublicUrl);
