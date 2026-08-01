namespace GymLink.Application.TrainerImages;

public sealed record TrainerImageUpload(
    byte[] Content,
    string ContentType,
    string FileName,
    string ConcurrencyToken);

public sealed record TrainerImageMutationRequest(string ConcurrencyToken);

public sealed record TrainerImageDto(
    Guid TrainerProfileId,
    string? ImageUrl,
    string? ContentType,
    long? FileSizeBytes,
    string ConcurrencyToken);

public interface ITrainerImageService
{
    Task<TrainerImageDto> UploadOwnAsync(
        TrainerImageUpload upload,
        CancellationToken cancellationToken);

    Task<TrainerImageDto> RemoveOwnAsync(
        TrainerImageMutationRequest request,
        CancellationToken cancellationToken);

    Task<TrainerImageDto> UploadForTenantAsync(
        Guid trainerProfileId,
        TrainerImageUpload upload,
        CancellationToken cancellationToken);

    Task<TrainerImageDto> RemoveForTenantAsync(
        Guid trainerProfileId,
        TrainerImageMutationRequest request,
        CancellationToken cancellationToken);
}
