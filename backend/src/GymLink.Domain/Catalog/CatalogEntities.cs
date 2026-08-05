using GymLink.Domain.Common;

namespace GymLink.Domain.Catalog;

public sealed class Gym : TenantEntity, IConcurrencyTracked
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public Guid CityId { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsPubliclyVisible { get; set; }
    public decimal AverageRating { get; private set; }
    public int ReviewCount { get; private set; }
    public byte[] RowVersion { get; set; } = [];

    public void AddReview(int rating)
    {
        EnsureRating(rating);
        AverageRating = decimal.Round(
            ((AverageRating * ReviewCount) + rating) / (ReviewCount + 1),
            2,
            MidpointRounding.AwayFromZero);
        ReviewCount++;
    }

    private static void EnsureRating(int rating)
    {
        if (rating is < 1 or > 5)
        {
            throw new DomainException("invalid_rating", "Rating must be between 1 and 5.");
        }
    }
}

public sealed class GymImage : TenantEntity, IConcurrencyTracked
{
    public const int MaximumGalleryImages = 5;
    public const long MaximumFileSizeBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes =
        new(StringComparer.Ordinal)
        {
            "image/jpeg",
            "image/png",
            "image/webp",
        };

    public Guid GymId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string? PublicUrl { get; set; }
    public string AltText { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
    public string? ContentType { get; private set; }
    public long? FileSizeBytes { get; private set; }
    public byte[] RowVersion { get; set; } = [];

    public void SetManagedContent(
        string storageKey,
        string publicUrl,
        string contentType,
        long fileSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > 512 ||
            Path.IsPathRooted(storageKey) || storageKey.Contains("..", StringComparison.Ordinal) ||
            storageKey.Contains('\\'))
        {
            throw InvalidImage("The image storage key is invalid.");
        }

        if (string.IsNullOrWhiteSpace(publicUrl) || publicUrl.Length > 2048 ||
            publicUrl[0] != '/' ||
            publicUrl.StartsWith("//", StringComparison.Ordinal) ||
            publicUrl.Contains('\\'))
        {
            throw InvalidImage("The image URL must be an API-relative path.");
        }

        if (!AllowedContentTypes.Contains(contentType))
        {
            throw InvalidImage("The image content type is not supported.");
        }

        if (fileSizeBytes is <= 0 or > MaximumFileSizeBytes)
        {
            throw InvalidImage("The image file size is invalid.");
        }

        StorageKey = storageKey;
        PublicUrl = publicUrl;
        ContentType = contentType;
        FileSizeBytes = fileSizeBytes;
    }

    public void SetGalleryPosition(int sortOrder, bool isPrimary)
    {
        if (sortOrder < 0 || isPrimary != (sortOrder == 0))
        {
            throw InvalidImage("The gallery position is invalid.");
        }

        SortOrder = sortOrder;
        IsPrimary = isPrimary;
    }

    private static DomainException InvalidImage(string message) =>
        new("invalid_gym_image", message);
}

public sealed class GymWorkingHours : TenantEntity
{
    public Guid GymId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly? OpensAt { get; set; }
    public TimeOnly? ClosesAt { get; set; }
    public bool IsClosed { get; set; }
}

public sealed class GymEquipment : TenantEntity
{
    public Guid GymId { get; set; }
    public Guid EquipmentId { get; set; }
    public int? Quantity { get; set; }
    public string? Notes { get; set; }
}

public sealed class GymTrainingType : TenantEntity
{
    public Guid GymId { get; set; }
    public Guid TrainingTypeId { get; set; }
}
