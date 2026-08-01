using GymLink.Domain.Enums;

namespace GymLink.Application.Recommendations;

internal static class RecommendationScoring
{
    internal const string AlgorithmVersion = "gymlink-hybrid-v1";
    internal static readonly decimal[] PreferenceWeights = [1.0m, 0.7m, 0.4m];

    private static readonly Dictionary<ActivityEventType, double> ActivityWeights =
        new Dictionary<ActivityEventType, double>
        {
            [ActivityEventType.GymView] = 1,
            [ActivityEventType.TrainerView] = 1,
            [ActivityEventType.MembershipRequest] = 3,
            [ActivityEventType.MembershipActivation] = 5,
            [ActivityEventType.ReservationCreation] = 4,
            [ActivityEventType.ReservationCompletion] = 6,
            [ActivityEventType.ReviewCreation] = 6,
        };

    internal static double PreferenceScore(
        IReadOnlyList<PreferenceSignal> preferences,
        Guid cityId,
        IReadOnlySet<Guid> trainingTypeIds)
    {
        if (preferences.Count == 0)
        {
            return 0;
        }

        var totalWeight = preferences.Sum(x => x.Weight);
        return totalWeight == 0
            ? 0
            : preferences.Sum(x => x.Weight *
                ((x.CityId == cityId ? 0.4 : 0) +
                 (trainingTypeIds.Contains(x.TrainingTypeId) ? 0.6 : 0))) / totalWeight;
    }

    internal static double DecayedActivityWeight(ActivityEventType eventType, double ageDays)
    {
        if (!ActivityWeights.TryGetValue(eventType, out var weight))
        {
            return 0;
        }

        return weight * Math.Pow(0.5, Math.Max(0, ageDays) / 60d);
    }

    internal static double BayesianQuality(decimal average, int count, double globalAverage)
    {
        const int priorCount = 5;
        var value = (((double)average * count) + (globalAverage * priorCount)) /
                    (count + priorCount);
        return Math.Clamp(value / 5d, 0, 1);
    }

    internal static double LogNormalize(int value, int maximum) =>
        LogNormalize((double)value, maximum);

    internal static double LogNormalize(double value, double maximum) =>
        maximum <= 0 || value <= 0
            ? 0
            : Math.Log(1 + value) / Math.Log(1 + maximum);

    internal static double FinalScore(
        double preference,
        bool hasPreferences,
        double activity,
        bool hasActivity,
        double popularity)
    {
        var weighted = 0.2 * popularity;
        var totalWeight = 0.2;
        if (hasPreferences)
        {
            weighted += 0.5 * preference;
            totalWeight += 0.5;
        }

        if (hasActivity)
        {
            weighted += 0.3 * activity;
            totalWeight += 0.3;
        }

        return Math.Clamp(weighted / totalWeight, 0, 1);
    }
}

internal sealed record PreferenceSignal(Guid CityId, Guid TrainingTypeId, double Weight);
