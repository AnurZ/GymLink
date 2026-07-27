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
    public byte[] RowVersion { get; set; } = [];
}

public sealed class GymImage : TenantEntity
{
    public Guid GymId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string? PublicUrl { get; set; }
    public string AltText { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
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
