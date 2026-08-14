namespace GymLink.Application.GymImages;

public sealed record GymImageUpload(
    byte[] Content,
    string ContentType,
    string FileName,
    string? ConcurrencyToken = null);

public sealed record GymImageMutationRequest(string ConcurrencyToken);

public sealed record GymImageOrderItemRequest(Guid ImageId, string ConcurrencyToken);

public sealed record GymImageOrderRequest(IReadOnlyList<GymImageOrderItemRequest> Images);

public sealed record GymImageGallerySaveItemRequest(
    Guid? ImageId,
    string? ConcurrencyToken,
    int? UploadIndex);

public sealed record GymImageGalleryRemovedItemRequest(
    Guid ImageId,
    string ConcurrencyToken);

public sealed record GymImageGallerySaveManifest(
    IReadOnlyList<GymImageGallerySaveItemRequest> Items,
    IReadOnlyList<GymImageGalleryRemovedItemRequest> RemovedImages);

public sealed record GymImageManagementDto(
    Guid Id,
    string? ImageUrl,
    string? ContentType,
    long? FileSizeBytes,
    int SortOrder,
    bool IsPrimary,
    string ConcurrencyToken);

public sealed record GymImageGalleryDto(
    int MaximumImages,
    IReadOnlyList<GymImageManagementDto> Images);

public interface IGymImageService
{
    Task<GymImageGalleryDto> AddAsync(
        GymImageUpload upload,
        CancellationToken cancellationToken);

    Task<GymImageGalleryDto> ReplaceAsync(
        Guid imageId,
        GymImageUpload upload,
        CancellationToken cancellationToken);

    Task<GymImageGalleryDto> RemoveAsync(
        Guid imageId,
        GymImageMutationRequest request,
        CancellationToken cancellationToken);

    Task<GymImageGalleryDto> ReorderAsync(
        GymImageOrderRequest request,
        CancellationToken cancellationToken);

    Task<GymImageGalleryDto> SaveGalleryAsync(
        GymImageGallerySaveManifest manifest,
        IReadOnlyList<GymImageUpload> uploads,
        CancellationToken cancellationToken);
}
