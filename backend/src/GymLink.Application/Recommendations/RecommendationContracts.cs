using System.ComponentModel.DataAnnotations;
using GymLink.Domain.Enums;

namespace GymLink.Application.Recommendations;

public sealed record PreferenceDto(
    int Rank,
    Guid CityId,
    string City,
    Guid TrainingTypeId,
    string TrainingType,
    decimal Weight);

public sealed record PreferenceItemRequest
{
    public Guid CityId { get; init; }
    public Guid TrainingTypeId { get; init; }
}

public sealed record ReplacePreferencesRequest
{
    [MaxLength(3)]
    public IReadOnlyList<PreferenceItemRequest> Items { get; init; } = [];
}

public sealed record RecommendationItemDto(
    RecommendationTargetType TargetType,
    Guid TargetId,
    Guid GymId,
    string Name,
    string Subtitle,
    string? ImageUrl,
    decimal RatingAverage,
    int RatingCount,
    decimal Score,
    string AlgorithmVersion,
    DateTime GeneratedAtUtc,
    string Reason);

public sealed record RecommendationActivitySummaryDto(
    string? MostFrequentTrainingType,
    decimal AverageReservationsPerWeek,
    string? PreferredCity);

public sealed record RecommendationFeedDto(
    IReadOnlyList<RecommendationItemDto> Items,
    RecommendationActivitySummaryDto ActivitySummary,
    string AlgorithmVersion,
    DateTime GeneratedAtUtc);

public interface IRecommendationService
{
    Task<IReadOnlyList<PreferenceDto>> GetPreferencesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PreferenceDto>> ReplacePreferencesAsync(
        ReplacePreferencesRequest request,
        CancellationToken cancellationToken);
    Task<RecommendationFeedDto> GetAsync(int limit, CancellationToken cancellationToken);
    Task<RecommendationFeedDto> RefreshAsync(int limit, CancellationToken cancellationToken);
    Task GenerateForUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);
}

public interface IRecommendationActivityRecorder
{
    Task RecordReadAsync(
        ActivityEventType eventType,
        Guid? targetTenantId,
        RecommendationTargetType? targetType,
        Guid? targetId,
        CancellationToken cancellationToken);

    Task RecordWorkflowAsync(
        Guid userId,
        Guid? targetTenantId,
        RecommendationTargetType? targetType,
        Guid? targetId,
        ActivityEventType eventType,
        Guid sourceId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken);
}
