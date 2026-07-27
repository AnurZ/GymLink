using GymLink.Domain.Common;
using GymLink.Domain.Enums;

namespace GymLink.Domain.Trainers;

public sealed class TrainerProfile : TenantEntity, IConcurrencyTracked
{
    public Guid UserId { get; set; }
    public string Biography { get; set; } = string.Empty;
    public string? Credentials { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public byte[] RowVersion { get; set; } = [];
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
    public int Capacity { get; set; } = 1;
    public AvailabilitySlotStatus Status { get; set; } = AvailabilitySlotStatus.Available;
    public byte[] RowVersion { get; set; } = [];
}
