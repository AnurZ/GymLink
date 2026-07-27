using GymLink.Domain.Common;
using GymLink.Domain.Enums;

namespace GymLink.Domain.Recommendations;

public sealed class UserPreference : AuditedEntity
{
    public Guid UserId { get; set; }
    public Guid? PreferredCityId { get; set; }
    public Guid? PreferredTrainingTypeId { get; set; }
    public decimal Weight { get; set; }
}

public sealed class ActivityHistory : Entity
{
    public Guid UserId { get; set; }
    public Guid? TargetTenantId { get; set; }
    public RecommendationTargetType? TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public ActivityEventType EventType { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public int MetadataVersion { get; set; } = 1;
}

public sealed class Recommendation : Entity
{
    public Guid UserId { get; set; }
    public Guid? TargetTenantId { get; set; }
    public RecommendationTargetType TargetType { get; set; }
    public Guid TargetId { get; set; }
    public decimal Score { get; set; }
    public string AlgorithmVersion { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}
