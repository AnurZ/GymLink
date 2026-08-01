using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Application.Images;
using GymLink.Domain.Catalog;
using GymLink.Domain.Common;
using GymLink.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymLink.Application.GymImages;

public sealed class GymImageService(
    IApplicationDbContext dbContext,
    IApplicationTransaction transaction,
    IFileStorage fileStorage,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IRequestMetadata requestMetadata,
    TimeProvider timeProvider,
    ILogger<GymImageService> logger) : IGymImageService
{
    private const int TemporarySortOrderBase = 1000;

    private static readonly Action<ILogger, string, Exception?> LogCompensationFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(9401, nameof(LogCompensationFailure)),
            "Failed to remove uncommitted Gym image {StorageKey}.");

    private static readonly Action<ILogger, string, Exception?> LogSupersededDeleteFailure =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(9402, nameof(LogSupersededDeleteFailure)),
            "Gym image metadata was committed but old file {StorageKey} could not be removed.");

    public async Task<GymImageGalleryDto> AddAsync(
        GymImageUpload upload,
        CancellationToken cancellationToken)
    {
        RequireGymAdmin();
        var actorId = RequireUser();
        var gym = await ResolveGymAsync(cancellationToken);
        var images = await LoadImagesAsync(gym.Id, cancellationToken);
        if (images.Count >= GymImage.MaximumGalleryImages)
        {
            throw new ConflictException(
                "gym_image_limit_reached",
                $"A gym may have at most {GymImage.MaximumGalleryImages} images.");
        }

        var contentType = ValidateUpload(upload);
        FileStorageResult? saved = null;
        try
        {
            await using var content = new MemoryStream(upload.Content, writable: false);
            saved = await fileStorage.SaveAsync(
                FileStorageArea.GymImages,
                content,
                contentType,
                upload.FileName,
                cancellationToken);
            var image = new GymImage
            {
                GymId = gym.Id,
                AltText = gym.Name,
            };
            image.SetManagedContent(
                saved.StorageKey,
                saved.PublicUrl ?? throw new InvalidOperationException(
                    "Gym image storage must return a public URL."),
                contentType,
                upload.Content.LongLength);
            image.SetGalleryPosition(images.Count, isPrimary: images.Count == 0);
            dbContext.GymImages.Add(image);
            AddAudit(actorId, gym, "gym_image.uploaded");
            await dbContext.SaveChangesAsync(cancellationToken);
            images.Add(image);
            return ToGallery(images);
        }
        catch (Exception exception)
        {
            dbContext.ClearTrackedChanges();
            if (saved is not null)
            {
                await DeleteCompensatingAsync(saved.StorageKey, cancellationToken);
            }

            if (exception is DbUpdateException)
            {
                throw new ConflictException(
                    "concurrency_conflict",
                    "The gym gallery changed. Reload it and try again.",
                    exception);
            }

            throw;
        }
    }

    public async Task<GymImageGalleryDto> ReplaceAsync(
        Guid imageId,
        GymImageUpload upload,
        CancellationToken cancellationToken)
    {
        RequireGymAdmin();
        var actorId = RequireUser();
        var gym = await ResolveGymAsync(cancellationToken);
        var images = await LoadImagesAsync(gym.Id, cancellationToken);
        var image = images.SingleOrDefault(x => x.Id == imageId) ?? throw ImageNotFound();
        ValidateConcurrencyToken(image.RowVersion, upload.ConcurrencyToken);
        var contentType = ValidateUpload(upload);
        var oldStorageKey = image.StorageKey;
        var oldFileWasManaged = IsManaged(image);
        FileStorageResult? saved = null;

        try
        {
            await using var content = new MemoryStream(upload.Content, writable: false);
            saved = await fileStorage.SaveAsync(
                FileStorageArea.GymImages,
                content,
                contentType,
                upload.FileName,
                cancellationToken);
            image.SetManagedContent(
                saved.StorageKey,
                saved.PublicUrl ?? throw new InvalidOperationException(
                    "Gym image storage must return a public URL."),
                contentType,
                upload.Content.LongLength);
            AddAudit(actorId, gym, "gym_image.replaced");
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

        if (oldFileWasManaged &&
            !string.Equals(oldStorageKey, saved.StorageKey, StringComparison.Ordinal))
        {
            await DeleteSupersededAsync(oldStorageKey, cancellationToken);
        }

        return ToGallery(images);
    }

    public async Task<GymImageGalleryDto> RemoveAsync(
        Guid imageId,
        GymImageMutationRequest request,
        CancellationToken cancellationToken)
    {
        RequireGymAdmin();
        var actorId = RequireUser();
        var gym = await ResolveGymAsync(cancellationToken);
        var images = await LoadImagesAsync(gym.Id, cancellationToken);
        var removed = images.SingleOrDefault(x => x.Id == imageId) ?? throw ImageNotFound();
        ValidateConcurrencyToken(removed.RowVersion, request.ConcurrencyToken);
        var oldFileWasManaged = IsManaged(removed);
        var oldStorageKey = removed.StorageKey;
        var primaryChanged = removed.IsPrimary && images.Count > 1;

        var remaining = images.Where(x => x.Id != imageId).ToList();
        await transaction.ExecuteAsync(async ct =>
        {
            dbContext.GymImages.Remove(removed);
            if (remaining.Count > 0)
            {
                SetTemporaryPositions(remaining);
                await dbContext.SaveChangesAsync(ct);
                SetFinalPositions(remaining);
            }

            AddAudit(actorId, gym, "gym_image.removed");
            if (primaryChanged)
            {
                AddAudit(actorId, gym, "gym_image.primary_changed");
            }

            await dbContext.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);

        if (oldFileWasManaged)
        {
            await DeleteSupersededAsync(oldStorageKey, cancellationToken);
        }

        return ToGallery(remaining);
    }

    public async Task<GymImageGalleryDto> ReorderAsync(
        GymImageOrderRequest request,
        CancellationToken cancellationToken)
    {
        RequireGymAdmin();
        var actorId = RequireUser();
        var gym = await ResolveGymAsync(cancellationToken);
        var images = await LoadImagesAsync(gym.Id, cancellationToken);
        ValidateOrderRequest(request, images);

        var byId = images.ToDictionary(x => x.Id);
        var ordered = request.Images.Select(x => byId[x.ImageId]).ToList();
        var primaryChanged = ordered.Count > 0 && !ordered[0].IsPrimary;
        await transaction.ExecuteAsync(async ct =>
        {
            SetTemporaryPositions(ordered);
            await dbContext.SaveChangesAsync(ct);
            SetFinalPositions(ordered);
            AddAudit(actorId, gym, "gym_image.reordered");
            if (primaryChanged)
            {
                AddAudit(actorId, gym, "gym_image.primary_changed");
            }

            await dbContext.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);

        return ToGallery(ordered);
    }

    private async Task<Gym> ResolveGymAsync(CancellationToken cancellationToken) =>
        await dbContext.Gyms.SingleOrDefaultAsync(cancellationToken) ??
        throw new NotFoundException("gym_not_found", "No gym exists for the current tenant.");

    private async Task<List<GymImage>> LoadImagesAsync(
        Guid gymId,
        CancellationToken cancellationToken) =>
        await dbContext.GymImages
            .Where(x => x.GymId == gymId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

    private static void ValidateOrderRequest(
        GymImageOrderRequest request,
        List<GymImage> current)
    {
        if (request.Images is null ||
            request.Images.Count != current.Count ||
            request.Images.Select(x => x.ImageId).Distinct().Count() != current.Count ||
            !request.Images.Select(x => x.ImageId).ToHashSet()
                .SetEquals(current.Select(x => x.Id)))
        {
            throw new ApplicationRuleException(
                "gym_image_order_invalid",
                "The complete current gallery order is required.");
        }

        var byId = current.ToDictionary(x => x.Id);
        foreach (var item in request.Images)
        {
            ValidateConcurrencyToken(byId[item.ImageId].RowVersion, item.ConcurrencyToken);
        }
    }

    private static void SetTemporaryPositions(List<GymImage> images)
    {
        for (var index = 0; index < images.Count; index++)
        {
            images[index].SetGalleryPosition(TemporarySortOrderBase + index, isPrimary: false);
        }
    }

    private static void SetFinalPositions(List<GymImage> images)
    {
        for (var index = 0; index < images.Count; index++)
        {
            images[index].SetGalleryPosition(index, isPrimary: index == 0);
        }
    }

    private void AddAudit(Guid actorId, Gym gym, string action) =>
        dbContext.SecurityAuditRecords.Add(new SecurityAuditRecord
        {
            ActorUserId = actorId,
            TargetTenantId = gym.TenantId,
            Action = action,
            TargetType = nameof(Gym),
            TargetId = gym.Id,
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
                FileStorageArea.GymImages,
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
                FileStorageArea.GymImages,
                storageKey,
                cancellationToken);
        }
        catch (Exception exception)
        {
            LogSupersededDeleteFailure(logger, storageKey, exception);
        }
    }

    private static bool IsManaged(GymImage image) =>
        image.ContentType is not null && image.FileSizeBytes.HasValue;

    private static string ValidateUpload(GymImageUpload upload) =>
        ImageUploadValidator.Validate(
            upload.Content,
            upload.ContentType,
            upload.FileName,
            GymImage.MaximumFileSizeBytes,
            "invalid_gym_image");

    private static void ValidateConcurrencyToken(byte[] current, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ApplicationRuleException(
                "concurrency_token_invalid",
                "The concurrency token is required.");
        }

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
                "The gym gallery changed. Reload it and try again.");
        }
    }

    public static GymImageGalleryDto ToGallery(IReadOnlyList<GymImage> images) =>
        new(
            GymImage.MaximumGalleryImages,
            images.OrderBy(x => x.SortOrder).Select(ToDto).ToArray());

    private static GymImageManagementDto ToDto(GymImage image) =>
        new(
            image.Id,
            image.PublicUrl,
            image.ContentType,
            image.FileSizeBytes,
            image.SortOrder,
            image.IsPrimary,
            Convert.ToBase64String(image.RowVersion));

    private Guid RequireUser() => currentUser.UserId ??
        throw new AuthorizationDeniedException(
            "current_user_required",
            "A current user is required.");

    private void RequireGymAdmin()
    {
        if (!tenantContext.HasTenant ||
            !string.Equals(
                tenantContext.TenantRole,
                RoleNames.GymAdmin,
                StringComparison.Ordinal))
        {
            throw new AuthorizationDeniedException();
        }
    }

    private static NotFoundException ImageNotFound() =>
        new("gym_image_not_found", "The gym image was not found.");
}
