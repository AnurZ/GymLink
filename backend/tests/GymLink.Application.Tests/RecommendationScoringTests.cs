using GymLink.Application.Recommendations;
using GymLink.Domain.Enums;
using GymLink.Domain.Recommendations;

namespace GymLink.Application.Tests;

public sealed class RecommendationScoringTests
{
    private static readonly Guid CityId = Guid.NewGuid();
    private static readonly Guid TypeId = Guid.NewGuid();

    [Fact]
    public void PreferenceScore_UsesCityAndTrainingTypeWeights()
    {
        var preferences = new[] { new PreferenceSignal(CityId, TypeId, 1) };

        Assert.Equal(1, RecommendationScoring.PreferenceScore(
            preferences, CityId, new HashSet<Guid> { TypeId }), 8);
        Assert.Equal(0.4, RecommendationScoring.PreferenceScore(
            preferences, CityId, new HashSet<Guid>()), 8);
        Assert.Equal(0.6, RecommendationScoring.PreferenceScore(
            preferences, Guid.NewGuid(), new HashSet<Guid> { TypeId }), 8);
    }

    [Fact]
    public void PreferenceScore_NormalizesRankedProfileWeights()
    {
        var preferences = new[]
        {
            new PreferenceSignal(CityId, TypeId, 1),
            new PreferenceSignal(Guid.NewGuid(), Guid.NewGuid(), 0.7),
        };

        Assert.Equal(1 / 1.7, RecommendationScoring.PreferenceScore(
            preferences, CityId, new HashSet<Guid> { TypeId }), 8);
    }

    [Fact]
    public void ActivityDecay_UsesSixtyDayHalfLifeAndConfiguredWeights()
    {
        Assert.Equal(6, RecommendationScoring.DecayedActivityWeight(
            ActivityEventType.ReservationCompletion, 0), 8);
        Assert.Equal(3, RecommendationScoring.DecayedActivityWeight(
            ActivityEventType.ReservationCompletion, 60), 8);
        Assert.Equal(0, RecommendationScoring.DecayedActivityWeight(
            ActivityEventType.Search, 0), 8);
    }

    [Theory]
    [InlineData(ActivityEventType.GymView, 1)]
    [InlineData(ActivityEventType.TrainerView, 1)]
    [InlineData(ActivityEventType.MembershipRequest, 3)]
    [InlineData(ActivityEventType.MembershipActivation, 5)]
    [InlineData(ActivityEventType.ReservationCreation, 4)]
    [InlineData(ActivityEventType.ReservationCompletion, 6)]
    [InlineData(ActivityEventType.ReviewCreation, 6)]
    public void ActivityDecay_UsesEveryDocumentedBaseWeight(
        ActivityEventType eventType,
        double expected)
    {
        Assert.Equal(expected, RecommendationScoring.DecayedActivityWeight(eventType, 0), 8);
    }

    [Fact]
    public void BayesianQuality_AppliesFiveVoteGlobalPrior()
    {
        Assert.Equal(0.8, RecommendationScoring.BayesianQuality(0, 0, 4), 8);
        Assert.Equal(0.9, RecommendationScoring.BayesianQuality(5, 5, 4), 8);
    }

    [Fact]
    public void FinalScore_RenormalizesMissingPersonalSignals()
    {
        Assert.Equal(0.5, RecommendationScoring.FinalScore(
            preference: 0,
            hasPreferences: false,
            activity: 0,
            hasActivity: false,
            popularity: 0.5), 8);
        Assert.Equal(0.59, RecommendationScoring.FinalScore(
            preference: 0.8,
            hasPreferences: true,
            activity: 0.5,
            hasActivity: true,
            popularity: 0.2), 8);
    }

    [Fact]
    public void LogNormalize_IsBoundedAndReachesOneAtMaximum()
    {
        Assert.Equal(0, RecommendationScoring.LogNormalize(0, 10), 8);
        Assert.Equal(1, RecommendationScoring.LogNormalize(10, 10), 8);
        Assert.InRange(RecommendationScoring.LogNormalize(3, 10), 0, 1);
    }

    [Fact]
    public void BalancedFeed_SplitsTypesAndFillsMissingCapacityDeterministically()
    {
        var gyms = Enumerable.Range(1, 4).Select(index => new Recommendation
        {
            TargetType = RecommendationTargetType.Gym,
            Score = 1 - (index / 10m),
        });
        var trainers = Enumerable.Range(1, 2).Select(index => new Recommendation
        {
            TargetType = RecommendationTargetType.Trainer,
            Score = 0.5m - (index / 10m),
        });

        var result = RecommendationService.Balance(gyms.Concat(trainers).ToList(), 5);

        Assert.Equal(5, result.Count);
        Assert.Equal(3, result.Count(x => x.TargetType == RecommendationTargetType.Gym));
        Assert.Equal(2, result.Count(x => x.TargetType == RecommendationTargetType.Trainer));
        Assert.Equal(result.OrderByDescending(x => x.Score).Select(x => x.Id), result.Select(x => x.Id));
    }

    [Fact]
    public void Explanation_SelectsStrongestContribution()
    {
        var preferences = new[] { new PreferenceSignal(CityId, TypeId, 1) };

        Assert.Contains("tipu treninga", RecommendationService.BuildReason(
            CityId,
            new HashSet<Guid> { TypeId },
            preferences,
            activity: 0.1,
            preference: 1,
            popularity: 0.1,
            quality: 0.1,
            reservations: 0.1));
        Assert.Contains("aktivnosti", RecommendationService.BuildReason(
            CityId,
            new HashSet<Guid>(),
            preferences,
            activity: 1,
            preference: 0,
            popularity: 0.1,
            quality: 0.1,
            reservations: 0.1));
        Assert.Contains("Visoko ocijenjeno", RecommendationService.BuildReason(
            Guid.NewGuid(),
            new HashSet<Guid>(),
            [],
            activity: 0,
            preference: 0,
            popularity: 0.8,
            quality: 0.9,
            reservations: 0.1));
    }
}
