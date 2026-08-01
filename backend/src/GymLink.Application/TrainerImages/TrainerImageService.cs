using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Images;
using GymLink.Domain.Common;
using GymLink.Domain.Identity;
using GymLink.Domain.Trainers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymLink.Application.TrainerImages;

public sealed class TrainerImageService(
    IApplicationDbContext dbContext,
    IFileStorage fileStorage,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IRequestMetadata requestMetadata,
    TimeProvider timeProvider,
    ILogger<TrainerImageService> logger) : ITrainerImageService
{
    private static readonly Action<ILogger, string, Exception?> LogCompensationFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(9301, nameof(LogCompensationFailure)),
            "Failed to remove uncommitted Trainer image {StorageKey}.");

    private static readonly Action<ILogger, string, Exception?> LogSupersededDeleteFailure =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(9302, nameof(LogSupersededDeleteFailure)),
            "Trainer image metadata was committed but old file {StorageKey} could not be removed.");

    public async Task<TrainerImageDto> UploadOwnAsync(
        TrainerImageUpload upload,
        CancellationToken cancellationToken)
    {
        RequireTenantRole(RoleNames.Trainer);
        var actorId = RequireUser();
        var trainer = await dbContext.TrainerProfiles
            .SingleOrDefaultAsync(x => x.UserId == actorId && x.IsActive, cancellationToken)
            ?? throw TrainerNotFound();
        return await UploadAsync(trainer, upload, actorId, cancellationToken);
    }

    public async Task<TrainerImageDto> RemoveOwnAsync(
        TrainerImageMutationRequest request,
        CancellationToken cancellationToken)
    {
        RequireTenantRole(RoleNames.Trainer);
        var actorId = RequireUser();
        var trainer = await dbContext.TrainerProfiles
            .SingleOrDefaultAsync(x => x.UserId == actorId && x.IsActive, cancellationToken)
            ?? throw TrainerNotFound();
        return await RemoveAsync(trainer, request, actorId, cancellationToken);
    }

    public async Task<TrainerImageDto> UploadForTenantAsync(
        Guid trainerProfileId,
        TrainerImageUpload upload,
        CancellationToken cancellationToken)
    {
        RequireTenantRole(RoleNames.GymAdmin);
        var trainer = await ResolveTenantTrainerAsync(trainerProfileId, cancellationToken);
        return await UploadAsync(trainer, upload, RequireUser(), cancellationToken);
    }

    public async Task<TrainerImageDto> RemoveForTenantAsync(
        Guid trainerProfileId,
        TrainerImageMutationRequest request,
        CancellationToken cancellationToken)
    {
        RequireTenantRole(RoleNames.GymAdmin);
        var trainer = await ResolveTenantTrainerAsync(trainerProfileId, cancellationToken);
        return await RemoveAsync(trainer, request, RequireUser(), cancellationToken);
    }

    private async Task<TrainerImageDto> UploadAsync(
        TrainerProfile trainer,
        TrainerImageUpload upload,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        ValidateConcurrencyToken(trainer.RowVersion, upload.ConcurrencyToken);
        var contentType = ValidateUpload(upload);
        var oldStorageKey = trainer.ImageStorageKey;
        FileStorageResult? saved = null;

        try
        {
            await using var content = new MemoryStream(upload.Content, writable: false);
            saved = await fileStorage.SaveAsync(
                FileStorageArea.TrainerImages,
                content,
                contentType,
                upload.FileName,
                cancellationToken);
            trainer.SetImage(
                saved.StorageKey,
                saved.PublicUrl ?? throw new InvalidOperationException(
                    "Trainer image storage must return a public URL."),
                contentType,
                upload.Content.LongLength);
            AddAudit(
                actorId,
                trainer,
                oldStorageKey is null ? "trainer_image.uploaded" : "trainer_image.replaced");
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            dbContext.ClearTrackedChanges();
            if (saved is not null)
            {
                await DeleteCompensatingAsync(saved.StorageKey, cancellationToken);
            }

            throw;
        }

        if (oldStorageKey is not null &&
            !string.Equals(oldStorageKey, saved.StorageKey, StringComparison.Ordinal))
        {
            await DeleteSupersededAsync(oldStorageKey, cancellationToken);
        }

        return ToDto(trainer);
    }

    private async Task<TrainerImageDto> RemoveAsync(
        TrainerProfile trainer,
        TrainerImageMutationRequest request,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        ValidateConcurrencyToken(trainer.RowVersion, request.ConcurrencyToken);
        var oldStorageKey = trainer.ImageStorageKey;
        if (!trainer.RemoveImage())
        {
            return ToDto(trainer);
        }

        AddAudit(actorId, trainer, "trainer_image.removed");
        await dbContext.SaveChangesAsync(cancellationToken);
        await DeleteSupersededAsync(oldStorageKey!, cancellationToken);
        return ToDto(trainer);
    }

    private async Task<TrainerProfile> ResolveTenantTrainerAsync(
        Guid trainerProfileId,
        CancellationToken cancellationToken) =>
        await dbContext.TrainerProfiles.SingleOrDefaultAsync(
            x => x.Id == trainerProfileId && x.IsActive,
            cancellationToken) ?? throw TrainerNotFound();

    private void AddAudit(Guid actorId, TrainerProfile trainer, string action) =>
        dbContext.SecurityAuditRecords.Add(new SecurityAuditRecord
        {
            ActorUserId = actorId,
            TargetUserId = trainer.UserId,
            TargetTenantId = trainer.TenantId,
            Action = action,
            TargetType = nameof(TrainerProfile),
            TargetId = trainer.Id,
            CorrelationId = requestMetadata.CorrelationId,
            OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        });

    private async Task DeleteCompensatingAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await fileStorage.DeleteAsync(
                FileStorageArea.TrainerImages,
                storageKey,
                cancellationToken);
        }
        catch (Exception exception)
        {
            LogCompensationFailure(logger, storageKey, exception);
        }
    }

    private async Task DeleteSupersededAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await fileStorage.DeleteAsync(
                FileStorageArea.TrainerImages,
                storageKey,
                cancellationToken);
        }
        catch (Exception exception)
        {
            LogSupersededDeleteFailure(logger, storageKey, exception);
        }
    }

    private static string ValidateUpload(TrainerImageUpload upload)
        => ImageUploadValidator.Validate(
            upload.Content,
            upload.ContentType,
            upload.FileName,
            TrainerProfile.MaximumImageFileSizeBytes,
            "invalid_trainer_image");

    private static void ValidateConcurrencyToken(byte[] current, string token)
    {
        byte[] supplied;
        try
        {
            supplied = Convert.FromBase64String(token);
        }
        catch (FormatException)
        {
            throw new ApplicationRuleException(
                "concurrency_token_invalid",
                "The concurrency token is invalid.");
        }

        if (!current.SequenceEqual(supplied))
        {
            throw new ConflictException(
                "concurrency_conflict",
                "The Trainer profile changed. Reload it and try again.");
        }
    }

    private static TrainerImageDto ToDto(TrainerProfile trainer) => new(
        trainer.Id,
        trainer.ImageUrl,
        trainer.ImageContentType,
        trainer.ImageFileSizeBytes,
        Convert.ToBase64String(trainer.RowVersion));

    private Guid RequireUser() => currentUser.UserId ??
        throw new AuthorizationDeniedException(
            "current_user_required",
            "A current user is required.");

    private void RequireTenantRole(string role)
    {
        if (!tenantContext.HasTenant ||
            !string.Equals(tenantContext.TenantRole, role, StringComparison.Ordinal))
        {
            throw new AuthorizationDeniedException();
        }
    }

    private static NotFoundException TrainerNotFound() =>
        new("trainer_not_found", "The Trainer was not found.");

}
