using GymLink.Domain.Common;
using GymLink.Domain.Enums;

namespace GymLink.Domain.Trainers;

public sealed class TrainerProfile : TenantEntity, IConcurrencyTracked
{
    public const long MaximumImageFileSizeBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedImageContentTypes =
        new(StringComparer.Ordinal)
        {
            "image/jpeg",
            "image/png",
            "image/webp",
        };

    public Guid UserId { get; set; }
    public string Biography { get; set; } = string.Empty;
    public string? Credentials { get; set; }
    public string? ImageStorageKey { get; private set; }
    public string? ImageUrl { get; private set; }
    public string? ImageContentType { get; private set; }
    public long? ImageFileSizeBytes { get; private set; }
    public bool IsActive { get; set; } = true;
    public decimal AverageRating { get; private set; }
    public int ReviewCount { get; private set; }
    public byte[] RowVersion { get; set; } = [];

    public void SetImage(
        string storageKey,
        string imageUrl,
        string contentType,
        long fileSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > 500 ||
            Path.IsPathRooted(storageKey) || storageKey.Contains("..", StringComparison.Ordinal) ||
            storageKey.Contains('\\'))
        {
            throw InvalidImage("The image storage key is invalid.");
        }

        if (string.IsNullOrWhiteSpace(imageUrl) || imageUrl.Length > 1000 ||
            imageUrl[0] != '/' ||
            imageUrl.StartsWith("//", StringComparison.Ordinal) ||
            imageUrl.Contains('\\'))
        {
            throw InvalidImage("The image URL must be an API-relative path.");
        }

        if (!AllowedImageContentTypes.Contains(contentType))
        {
            throw InvalidImage("The image content type is not supported.");
        }

        if (fileSizeBytes is <= 0 or > MaximumImageFileSizeBytes)
        {
            throw InvalidImage("The image file size is invalid.");
        }

        ImageStorageKey = storageKey;
        ImageUrl = imageUrl;
        ImageContentType = contentType;
        ImageFileSizeBytes = fileSizeBytes;
    }

    public bool RemoveImage()
    {
        if (ImageStorageKey is null)
        {
            return false;
        }

        ImageStorageKey = null;
        ImageUrl = null;
        ImageContentType = null;
        ImageFileSizeBytes = null;
        return true;
    }

    public void AddReview(int rating)
    {
        if (rating is < 1 or > 5)
        {
            throw new DomainException("invalid_rating", "Rating must be between 1 and 5.");
        }

        AverageRating = decimal.Round(
            ((AverageRating * ReviewCount) + rating) / (ReviewCount + 1),
            2,
            MidpointRounding.AwayFromZero);
        ReviewCount++;
    }

    private static DomainException InvalidImage(string message) =>
        new("invalid_trainer_image", message);
}

public sealed class TrainerTrainingType : TenantEntity
{
    public Guid TrainerProfileId { get; set; }
    public Guid TrainingTypeId { get; set; }
}

public sealed class TrainerServiceOffering : TenantEntity, IConcurrencyTracked
{
    private TrainerServiceOffering() { }

    public TrainerServiceOffering(
        Guid tenantId,
        Guid trainerProfileId,
        Guid trainingTypeId,
        string name,
        int durationMinutes,
        decimal price,
        string currency)
    {
        if (durationMinutes <= 0)
        {
            throw new DomainException("invalid_duration", "Duration must be greater than zero.");
        }

        if (price < 0)
        {
            throw new DomainException("invalid_price", "Price cannot be negative.");
        }

        TenantId = tenantId;
        TrainerProfileId = trainerProfileId;
        TrainingTypeId = trainingTypeId;
        Name = name;
        DurationMinutes = durationMinutes;
        Price = price;
        Currency = currency;
    }

    public Guid TrainerProfileId { get; set; }
    public Guid TrainingTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DurationMinutes { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];

    public void UpdateDetails(
        Guid trainingTypeId,
        string name,
        int durationMinutes,
        decimal price,
        string currency,
        bool isActive)
    {
        if (durationMinutes <= 0)
        {
            throw new DomainException("invalid_duration", "Duration must be greater than zero.");
        }

        if (price < 0)
        {
            throw new DomainException("invalid_price", "Price cannot be negative.");
        }

        TrainingTypeId = trainingTypeId;
        Name = name;
        DurationMinutes = durationMinutes;
        Price = price;
        Currency = currency;
        IsActive = isActive;
    }
}

public sealed class TrainerAvailabilitySlot : TenantEntity, IConcurrencyTracked
{
    private TrainerAvailabilitySlot() { }

    public TrainerAvailabilitySlot(Guid tenantId, Guid trainerProfileId, DateTime startsAtUtc, DateTime endsAtUtc)
    {
        if (startsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException("utc_required", "Availability times must use UTC.");
        }

        if (endsAtUtc <= startsAtUtc)
        {
            throw new DomainException("invalid_time_range", "End time must be after start time.");
        }

        TenantId = tenantId;
        TrainerProfileId = trainerProfileId;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
    }

    public Guid TrainerProfileId { get; set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public int Capacity { get; private set; } = 1;
    public AvailabilitySlotStatus Status { get; private set; } = AvailabilitySlotStatus.Available;
    public byte[] RowVersion { get; set; } = [];

    public void Update(
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        AvailabilitySlotStatus status)
    {
        EnsureMutable();
        EnsureTimeRange(startsAtUtc, endsAtUtc);
        if (status is not AvailabilitySlotStatus.Available and
            not AvailabilitySlotStatus.Unavailable)
        {
            throw InvalidTransition();
        }

        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        Status = status;
    }

    public void Reserve()
    {
        if (Status != AvailabilitySlotStatus.Available)
        {
            throw InvalidTransition();
        }

        Status = AvailabilitySlotStatus.Reserved;
    }

    public void Release()
    {
        if (Status != AvailabilitySlotStatus.Reserved)
        {
            throw InvalidTransition();
        }

        Status = AvailabilitySlotStatus.Available;
    }

    public void Cancel()
    {
        EnsureMutable();
        Status = AvailabilitySlotStatus.Cancelled;
    }

    private void EnsureMutable()
    {
        if (Status is not AvailabilitySlotStatus.Available and
            not AvailabilitySlotStatus.Unavailable)
        {
            throw InvalidTransition();
        }
    }

    private static void EnsureTimeRange(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        if (startsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException("utc_required", "Availability times must use UTC.");
        }

        if (endsAtUtc <= startsAtUtc)
        {
            throw new DomainException("invalid_time_range", "End time must be after start time.");
        }
    }

    private static DomainException InvalidTransition() =>
        new(
            "invalid_state_transition",
            "The availability slot cannot perform that transition from its current state.");
}

public sealed class TrainerAvailabilitySchedule : TenantEntity, IConcurrencyTracked
{
    public const string SarajevoTimeZoneId = "Europe/Sarajevo";
    public const int DefaultBookingHorizonWeeks = 8;

    private TrainerAvailabilitySchedule() { }

    public TrainerAvailabilitySchedule(Guid tenantId, Guid trainerProfileId)
    {
        TenantId = tenantId;
        TrainerProfileId = trainerProfileId;
    }

    public Guid TrainerProfileId { get; private set; }
    public string TimeZoneId { get; private set; } = SarajevoTimeZoneId;
    public int BookingHorizonWeeks { get; private set; } = DefaultBookingHorizonWeeks;
    public int Revision { get; private set; }
    public byte[] RowVersion { get; set; } = [];

    public void RecordReplacement() => Revision++;
}

public sealed class TrainerWeeklyShift : TenantEntity
{
    public static readonly TimeOnly MorningStartsAt = new(8, 0);
    public static readonly TimeOnly MorningEndsAt = new(15, 0);
    public static readonly TimeOnly EveningStartsAt = new(15, 0);
    public static readonly TimeOnly EveningEndsAt = new(22, 0);

    private TrainerWeeklyShift() { }

    public TrainerWeeklyShift(
        Guid tenantId,
        Guid trainerAvailabilityScheduleId,
        Guid trainerProfileId,
        DayOfWeek dayOfWeek,
        TrainerShiftPeriod period)
    {
        if (!Enum.IsDefined(dayOfWeek))
        {
            throw new DomainException("invalid_weekday", "The weekday is invalid.");
        }

        if (!Enum.IsDefined(period))
        {
            throw new DomainException("invalid_shift_period", "The shift period is invalid.");
        }

        TenantId = tenantId;
        TrainerAvailabilityScheduleId = trainerAvailabilityScheduleId;
        TrainerProfileId = trainerProfileId;
        DayOfWeek = dayOfWeek;
        Period = period;
        (StartsAtLocal, EndsAtLocal) = period switch
        {
            TrainerShiftPeriod.Morning => (MorningStartsAt, MorningEndsAt),
            TrainerShiftPeriod.Evening => (EveningStartsAt, EveningEndsAt),
            _ => throw new DomainException("invalid_shift_period", "The shift period is invalid."),
        };
    }

    public Guid TrainerAvailabilityScheduleId { get; private set; }
    public Guid TrainerProfileId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TrainerShiftPeriod Period { get; private set; }
    public TimeOnly StartsAtLocal { get; private set; }
    public TimeOnly EndsAtLocal { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
