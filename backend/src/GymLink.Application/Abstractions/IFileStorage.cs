namespace GymLink.Application.Abstractions;

public interface IFileStorage
{
    Task<FileStorageResult> SaveAsync(
        FileStorageArea area,
        Stream content,
        string contentType,
        string fileName,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        FileStorageArea area,
        string storageKey,
        CancellationToken cancellationToken);

    Task<Stream?> OpenReadAsync(
        FileStorageArea area,
        string storageKey,
        CancellationToken cancellationToken);
}

public enum FileStorageArea
{
    TrainerImages,
    GymImages,
    ChatImages,
}

public sealed record FileStorageResult(string StorageKey, string? PublicUrl);
