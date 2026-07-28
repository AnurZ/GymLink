using GymLink.Domain.Common;
using GymLink.Domain.Enums;

namespace GymLink.Domain.Trainers;

public sealed class TrainerProfile : TenantEntity, IConcurrencyTracked
{
    public Guid UserId { get; set; }
    public string Biography { get; set; } = string.Empty;
    public string? Credentials { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal AverageRating { get; private set; }
    public int ReviewCount { get; private set; }
    public byte[] RowVersion { get; set; } = [];

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
