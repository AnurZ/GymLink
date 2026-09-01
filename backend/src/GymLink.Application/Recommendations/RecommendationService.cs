using System.ComponentModel.DataAnnotations;
using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Recommendations;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Recommendations;

internal sealed class RecommendationService(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser,
    IIdentityAccountManager accounts,
    IApplicationTransaction transaction,
    IRecommendationActivityRecorder activityRecorder,
    TimeProvider timeProvider) : IRecommendationService
{
    private const int DefaultPersistedPerType = 20;
    private static readonly TimeSpan ResultLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan PopularityWindow = TimeSpan.FromDays(180);
    private static readonly SemaphoreSlim[] GenerationLocks =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<IReadOnlyList<PreferenceDto>> GetPreferencesAsync(
        CancellationToken cancellationToken) =>
        await LoadPreferenceDtosAsync(RequireCurrentUser(), cancellationToken);

    public async Task<IReadOnlyList<PreferenceDto>> ReplacePreferencesAsync(
        ReplacePreferencesRequest request,
        CancellationToken cancellationToken)
    {
        var userId = RequireCurrentUser();
        if (request.Items.Count > RecommendationScoring.PreferenceWeights.Length)
        {
            throw new ConflictException(
                "preference_limit_exceeded",
                "A Member may save at most three recommendation preferences.");
        }

        if (request.Items.Any(x => x.CityId == Guid.Empty || x.TrainingTypeId == Guid.Empty))
        {
            throw new ConflictException(
                "preference_reference_required",
                "Every preference requires a city and training type.");
        }

        if (request.Items.DistinctBy(x => new { x.CityId, x.TrainingTypeId }).Count() !=
            request.Items.Count)
        {
            throw new ConflictException(
                "preference_duplicate",
                "The same city and training type preference cannot be selected twice.");
        }

        var cityIds = request.Items.Select(x => x.CityId).Distinct().ToArray();
        var trainingTypeIds = request.Items.Select(x => x.TrainingTypeId).Distinct().ToArray();
        if (await dbContext.Cities.CountAsync(
                x => cityIds.Contains(x.Id) && x.IsActive,
                cancellationToken) != cityIds.Length)
        {
            throw new NotFoundException(
                "city_not_found",
                "One or more selected cities were not found.");
        }

        if (await dbContext.TrainingTypes.CountAsync(
                x => trainingTypeIds.Contains(x.Id) && x.IsActive,
                cancellationToken) != trainingTypeIds.Length)
        {
            throw new NotFoundException(
                "training_type_not_found",
                "One or more selected training types were not found.");
        }

        await transaction.ExecuteSerializableAsync(async token =>
        {
            var oldPreferences = await dbContext.UserPreferences
                .Where(x => x.UserId == userId)
                .ToListAsync(token);
            dbContext.UserPreferences.RemoveRange(oldPreferences);
            for (var index = 0; index < request.Items.Count; index++)
            {
                var item = request.Items[index];
                dbContext.UserPreferences.Add(new UserPreference
                {
                    UserId = userId,
                    PreferredCityId = item.CityId,
                    PreferredTrainingTypeId = item.TrainingTypeId,
                    Weight = RecommendationScoring.PreferenceWeights[index],
                });
            }

            var oldRecommendations = await dbContext.Recommendations
                .Where(x => x.UserId == userId)
                .ToListAsync(token);
            dbContext.Recommendations.RemoveRange(oldRecommendations);
            await activityRecorder.RecordWorkflowAsync(
                userId,
                null,
                null,
                null,
                ActivityEventType.PreferredTrainingTypeChange,
                Guid.NewGuid(),
                timeProvider.GetUtcNow().UtcDateTime,
                token);
            await dbContext.SaveChangesAsync(token);
            return true;
        }, cancellationToken);

        return await LoadPreferenceDtosAsync(userId, cancellationToken);
    }

    public async Task<RecommendationFeedDto> GetAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateLimit(limit);
        var userId = RequireCurrentUser();
        if (await IsStaleAsync(userId, cancellationToken))
        {
            await GenerateAsync(userId, cancellationToken);
        }

        return await BuildFeedAsync(userId, limit, cancellationToken);
    }

    public async Task<RecommendationFeedDto> RefreshAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateLimit(limit);
        var userId = RequireCurrentUser();
        await GenerateAsync(userId, cancellationToken);
        return await BuildFeedAsync(userId, limit, cancellationToken);
    }

    public async Task GenerateForUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        foreach (var userId in userIds.Distinct())
        {
            await GenerateAsync(userId, cancellationToken);
        }
    }

    private async Task GenerateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var generationLock = GenerationLocks[(userId.GetHashCode() & int.MaxValue) %
            GenerationLocks.Length];
        await generationLock.WaitAsync(cancellationToken);
        try
        {
            await transaction.ExecuteSerializableAsync(async token =>
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var candidates = await LoadCandidatesAsync(token);
                var preferences = await dbContext.UserPreferences.AsNoTracking()
                    .Where(x => x.UserId == userId)
                    .OrderByDescending(x => x.Weight)
                    .ThenBy(x => x.Id)
                    .Select(x => new PreferenceSignal(
                        x.PreferredCityId,
                        x.PreferredTrainingTypeId,
                        (double)x.Weight))
                    .ToListAsync(token);
                var activities = await dbContext.ActivityHistory.AsNoTracking()
                    .Where(x => x.OccurredAtUtc <= now)
                    .ToListAsync(token);

                var trainerToGym = candidates.Trainers.ToDictionary(x => x.Id, x => x.GymId);
                var ownRaw = BuildActivityScores(
                    activities.Where(x => x.UserId == userId),
                    trainerToGym,
                    now);
                var globalRaw = BuildActivityScores(activities, trainerToGym, now);
                var maxOwnGym = MaxOrZero(ownRaw.Gyms.Values);
                var maxOwnTrainer = MaxOrZero(ownRaw.Trainers.Values);
                var maxGlobalGym = MaxOrZero(globalRaw.Gyms.Values);
                var maxGlobalTrainer = MaxOrZero(globalRaw.Trainers.Values);
                var hasTargetActivity = maxOwnGym > 0 || maxOwnTrainer > 0;

                var reservationCutoff = now - PopularityWindow;
                var reservationByTrainer = await dbContext.AppointmentReservations
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x => x.StartsAtUtc >= reservationCutoff && x.StartsAtUtc <= now &&
                        (x.Status == ReservationStatus.Confirmed ||
                         x.Status == ReservationStatus.Completed))
                    .GroupBy(x => x.TrainerProfileId)
                    .Select(x => new { Id = x.Key, Count = x.Count() })
                    .ToDictionaryAsync(x => x.Id, x => x.Count, token);
                var reservationByTenant = await dbContext.AppointmentReservations
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x => x.StartsAtUtc >= reservationCutoff && x.StartsAtUtc <= now &&
                        (x.Status == ReservationStatus.Confirmed ||
                         x.Status == ReservationStatus.Completed))
                    .GroupBy(x => x.TenantId)
                    .Select(x => new { Id = x.Key, Count = x.Count() })
                    .ToDictionaryAsync(x => x.Id, x => x.Count, token);
                var maxTrainerReservations = reservationByTrainer.Values.DefaultIfEmpty().Max();
                var maxGymReservations = reservationByTenant.Values.DefaultIfEmpty().Max();
                var globalAverage = WeightedGlobalRating(candidates);

                var scored = new List<ScoredCandidate>();
                foreach (var gym in candidates.Gyms)
                {
                    var preference = RecommendationScoring.PreferenceScore(
                        preferences,
                        gym.CityId,
                        gym.TrainingTypeIds);
                    var activity = Normalize(ownRaw.Gyms, gym.Id, maxOwnGym);
                    var quality = RecommendationScoring.BayesianQuality(
                        gym.RatingAverage,
                        gym.RatingCount,
                        globalAverage);
                    var reservations = RecommendationScoring.LogNormalize(
                        reservationByTenant.GetValueOrDefault(gym.TenantId),
                        maxGymReservations);
                    var engagement = RecommendationScoring.LogNormalize(
                        globalRaw.Gyms.GetValueOrDefault(gym.Id),
                        maxGlobalGym);
                    var popularity = (0.5 * quality) + (0.3 * reservations) + (0.2 * engagement);
                    var final = RecommendationScoring.FinalScore(
                        preference,
                        preferences.Count > 0,
                        activity,
                        hasTargetActivity,
                        popularity);
                    scored.Add(new ScoredCandidate(
                        RecommendationTargetType.Gym,
                        gym.Id,
                        gym.TenantId,
                        gym.Name,
                        final,
                        BuildReason(
                            gym.CityId,
                            gym.TrainingTypeIds,
                            preferences,
                            activity,
                            preference,
                            popularity,
                            quality,
                            reservations)));
                }

                foreach (var trainer in candidates.Trainers)
                {
                    var preference = RecommendationScoring.PreferenceScore(
                        preferences,
                        trainer.CityId,
                        trainer.TrainingTypeIds);
                    var activity = Normalize(ownRaw.Trainers, trainer.Id, maxOwnTrainer);
                    var quality = RecommendationScoring.BayesianQuality(
                        trainer.RatingAverage,
                        trainer.RatingCount,
                        globalAverage);
                    var reservations = RecommendationScoring.LogNormalize(
                        reservationByTrainer.GetValueOrDefault(trainer.Id),
                        maxTrainerReservations);
                    var engagement = RecommendationScoring.LogNormalize(
                        globalRaw.Trainers.GetValueOrDefault(trainer.Id),
                        maxGlobalTrainer);
                    var popularity = (0.5 * quality) + (0.3 * reservations) + (0.2 * engagement);
                    var final = RecommendationScoring.FinalScore(
                        preference,
                        preferences.Count > 0,
                        activity,
                        hasTargetActivity,
                        popularity);
                    scored.Add(new ScoredCandidate(
                        RecommendationTargetType.Trainer,
                        trainer.Id,
                        trainer.TenantId,
                        trainer.Name,
                        final,
                        BuildReason(
                            trainer.CityId,
                            trainer.TrainingTypeIds,
                            preferences,
                            activity,
                            preference,
                            popularity,
                            quality,
                            reservations)));
                }

                var selected = scored
                    .GroupBy(x => x.TargetType)
                    .SelectMany(group => group
                        .OrderByDescending(x => x.Score)
                        .ThenBy(x => x.Name)
                        .ThenBy(x => x.TargetId)
                        .Take(DefaultPersistedPerType))
                    .ToList();
                var old = await dbContext.Recommendations
                    .Where(x => x.UserId == userId)
                    .ToListAsync(token);
                dbContext.Recommendations.RemoveRange(old);
                dbContext.Recommendations.AddRange(selected.Select(x => new Recommendation
                {
                    UserId = userId,
                    TargetTenantId = x.TenantId,
                    TargetType = x.TargetType,
                    TargetId = x.TargetId,
                    Score = decimal.Round((decimal)x.Score, 6, MidpointRounding.AwayFromZero),
                    AlgorithmVersion = RecommendationScoring.AlgorithmVersion,
                    GeneratedAtUtc = now,
                    Reason = x.Reason,
                }));
                await dbContext.SaveChangesAsync(token);
                return true;
            }, cancellationToken);
        }
        finally
        {
            generationLock.Release();
        }
    }

    private async Task<RecommendationFeedDto> BuildFeedAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var candidates = await LoadCandidatesAsync(cancellationToken);
        var gyms = candidates.Gyms.ToDictionary(x => x.Id);
        var trainers = candidates.Trainers.ToDictionary(x => x.Id);
        var stored = await dbContext.Recommendations.AsNoTracking()
            .Where(x => x.UserId == userId &&
                        x.AlgorithmVersion == RecommendationScoring.AlgorithmVersion)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.TargetType)
            .ThenBy(x => x.TargetId)
            .ToListAsync(cancellationToken);
        var available = stored.Where(x =>
                x.TargetType == RecommendationTargetType.Gym
                    ? gyms.ContainsKey(x.TargetId)
                    : trainers.ContainsKey(x.TargetId))
            .ToList();
        var selected = Balance(available, limit);
        var items = selected.Select(row =>
        {
            if (row.TargetType == RecommendationTargetType.Gym)
            {
                var gym = gyms[row.TargetId];
                return new RecommendationItemDto(
                    row.TargetType,
                    row.TargetId,
                    gym.Id,
                    gym.Name,
                    gym.City,
                    gym.ImageUrl,
                    gym.RatingAverage,
                    gym.RatingCount,
                    row.Score,
                    row.AlgorithmVersion,
                    row.GeneratedAtUtc,
                    row.Reason);
            }

            var trainer = trainers[row.TargetId];
            return new RecommendationItemDto(
                row.TargetType,
                row.TargetId,
                trainer.GymId,
                trainer.Name,
                trainer.TrainingTypes.Count == 0
                    ? trainer.City
                    : string.Join(" · ", trainer.TrainingTypes),
                trainer.ImageUrl,
                trainer.RatingAverage,
                trainer.RatingCount,
                row.Score,
                row.AlgorithmVersion,
                row.GeneratedAtUtc,
                row.Reason);
        }).ToList();
        var generatedAt = selected.Select(x => x.GeneratedAtUtc).DefaultIfEmpty().Max();
        var summary = await BuildActivitySummaryAsync(userId, cancellationToken);
        return new RecommendationFeedDto(
            items,
            summary,
            RecommendationScoring.AlgorithmVersion,
            generatedAt);
    }

    private async Task<bool> IsStaleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var generated = await dbContext.Recommendations.AsNoTracking()
            .Where(x => x.UserId == userId &&
                        x.AlgorithmVersion == RecommendationScoring.AlgorithmVersion)
            .OrderByDescending(x => x.GeneratedAtUtc)
            .Select(x => (DateTime?)x.GeneratedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (!generated.HasValue || now - generated.Value >= ResultLifetime)
        {
            return true;
        }

        if (await dbContext.Recommendations.AsNoTracking()
            .AnyAsync(x => x.UserId == userId &&
                x.AlgorithmVersion != RecommendationScoring.AlgorithmVersion,
                cancellationToken))
        {
            return true;
        }

        var latestPreference = await dbContext.UserPreferences.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.UpdatedAtUtc ?? x.CreatedAtUtc)
            .OrderByDescending(x => x)
            .Select(x => (DateTime?)x)
            .FirstOrDefaultAsync(cancellationToken);
        var latestActivity = await dbContext.ActivityHistory.AsNoTracking()
            .Where(x => x.UserId == userId && x.OccurredAtUtc <= now)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => (DateTime?)x.OccurredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return latestPreference > generated || latestActivity > generated;
    }

    private async Task<IReadOnlyList<PreferenceDto>> LoadPreferenceDtosAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from preference in dbContext.UserPreferences.AsNoTracking()
                join city in dbContext.Cities.AsNoTracking()
                    on preference.PreferredCityId equals city.Id
                join trainingType in dbContext.TrainingTypes.AsNoTracking()
                    on preference.PreferredTrainingTypeId equals trainingType.Id
                where preference.UserId == userId
                orderby preference.Weight descending, preference.Id
                select new
                {
                    preference.PreferredCityId,
                    City = city.Name,
                    preference.PreferredTrainingTypeId,
                    TrainingType = trainingType.Name,
                    preference.Weight,
                })
            .ToListAsync(cancellationToken);
        return rows.Select((x, index) => new PreferenceDto(
                index + 1,
                x.PreferredCityId,
                x.City,
                x.PreferredTrainingTypeId,
                x.TrainingType,
                x.Weight))
            .ToList();
    }

    private async Task<CandidateSet> LoadCandidatesAsync(CancellationToken cancellationToken)
    {
        var trainerUserIds = await accounts.GetUserIdsInRoleAsync(
            RoleNames.Trainer,
            cancellationToken);
        var gymRows = await (
                from gym in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                join tenant in dbContext.Tenants.AsNoTracking() on gym.TenantId equals tenant.Id
                join city in dbContext.Cities.AsNoTracking() on gym.CityId equals city.Id
                where gym.IsPubliclyVisible && tenant.Status == TenantStatus.Active
                orderby gym.Name, gym.Id
                select new
                {
                    gym.Id,
                    gym.TenantId,
                    gym.CityId,
                    City = city.Name,
                    gym.Name,
                    gym.AverageRating,
                    gym.ReviewCount,
                    ImageUrl = dbContext.GymImages.IgnoreQueryFilters()
                        .Where(x => x.GymId == gym.Id && x.IsPrimary)
                        .Select(x => x.PublicUrl)
                        .FirstOrDefault(),
                })
            .ToListAsync(cancellationToken);
        var gymIds = gymRows.Select(x => x.Id).ToArray();
        var gymTypes = await (
                from link in dbContext.GymTrainingTypes.IgnoreQueryFilters().AsNoTracking()
                join type in dbContext.TrainingTypes.AsNoTracking() on link.TrainingTypeId equals type.Id
                where gymIds.Contains(link.GymId)
                select new { link.GymId, type.Id, type.Name })
            .ToListAsync(cancellationToken);
        var gymTypeLookup = gymTypes.GroupBy(x => x.GymId).ToDictionary(
            x => x.Key,
            x => x.Select(item => item.Id).ToHashSet());

        var trainerRows = await (
                from trainer in dbContext.CanonicalActiveTrainers()
                join user in dbContext.UserProfiles.AsNoTracking() on trainer.UserId equals user.Id
                join gym in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                    on trainer.TenantId equals gym.TenantId
                join tenant in dbContext.Tenants.AsNoTracking() on gym.TenantId equals tenant.Id
                join city in dbContext.Cities.AsNoTracking() on gym.CityId equals city.Id
                where trainerUserIds.Contains(trainer.UserId) &&
                      gym.IsPubliclyVisible && tenant.Status == TenantStatus.Active
                orderby user.DisplayName, trainer.Id
                select new
                {
                    trainer.Id,
                    trainer.TenantId,
                    GymId = gym.Id,
                    gym.CityId,
                    City = city.Name,
                    Name = user.DisplayName,
                    trainer.AverageRating,
                    trainer.ReviewCount,
                    trainer.ImageUrl,
                })
            .ToListAsync(cancellationToken);
        var trainerIds = trainerRows.Select(x => x.Id).ToArray();
        var trainerTypes = await (
                from link in dbContext.TrainerTrainingTypes.IgnoreQueryFilters().AsNoTracking()
                join type in dbContext.TrainingTypes.AsNoTracking() on link.TrainingTypeId equals type.Id
                where trainerIds.Contains(link.TrainerProfileId)
                orderby type.Name
                select new { link.TrainerProfileId, type.Id, type.Name })
            .ToListAsync(cancellationToken);
        var trainerTypeLookup = trainerTypes.GroupBy(x => x.TrainerProfileId).ToDictionary(
            x => x.Key,
            x => x.ToList());

        return new CandidateSet(
            gymRows.Select(x => new GymCandidate(
                    x.Id,
                    x.TenantId,
                    x.CityId,
                    x.City,
                    x.Name,
                    x.ImageUrl,
                    x.AverageRating,
                    x.ReviewCount,
                    gymTypeLookup.GetValueOrDefault(x.Id) ?? []))
                .ToList(),
            trainerRows.Select(x =>
            {
                var types = trainerTypeLookup.GetValueOrDefault(x.Id) ?? [];
                return new TrainerCandidate(
                    x.Id,
                    x.TenantId,
                    x.GymId,
                    x.CityId,
                    x.City,
                    x.Name,
                    x.ImageUrl,
                    x.AverageRating,
                    x.ReviewCount,
                    types.Select(type => type.Id).ToHashSet(),
                    types.Select(type => type.Name).ToList());
            }).ToList());
    }

    private async Task<RecommendationActivitySummaryDto> BuildActivitySummaryAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = now - TimeSpan.FromDays(90);
        var reservationCount = await dbContext.AppointmentReservations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(x => x.MemberUserId == userId && x.StartsAtUtc >= cutoff &&
                x.StartsAtUtc <= now &&
                (x.Status == ReservationStatus.Confirmed ||
                 x.Status == ReservationStatus.Completed), cancellationToken);
        var mostFrequent = await (
                from reservation in dbContext.AppointmentReservations.IgnoreQueryFilters().AsNoTracking()
                join offering in dbContext.TrainerServiceOfferings.IgnoreQueryFilters().AsNoTracking()
                    on reservation.TrainerServiceOfferingId equals offering.Id
                join type in dbContext.TrainingTypes.AsNoTracking()
                    on offering.TrainingTypeId equals type.Id
                where reservation.MemberUserId == userId && reservation.StartsAtUtc >= cutoff &&
                      reservation.StartsAtUtc <= now &&
                      (reservation.Status == ReservationStatus.Confirmed ||
                       reservation.Status == ReservationStatus.Completed)
                group type by new { type.Id, type.Name } into grouping
                orderby grouping.Count() descending, grouping.Key.Name
                select grouping.Key.Name)
            .FirstOrDefaultAsync(cancellationToken);
        var preferredCity = await (
                from preference in dbContext.UserPreferences.AsNoTracking()
                join city in dbContext.Cities.AsNoTracking()
                    on preference.PreferredCityId equals city.Id
                where preference.UserId == userId
                orderby preference.Weight descending, preference.Id
                select city.Name)
            .FirstOrDefaultAsync(cancellationToken);
        return new RecommendationActivitySummaryDto(
            mostFrequent,
            decimal.Round(reservationCount / (90m / 7m), 1, MidpointRounding.AwayFromZero),
            preferredCity);
    }

    private static ActivityScores BuildActivityScores(
        IEnumerable<ActivityHistory> activities,
        Dictionary<Guid, Guid> trainerToGym,
        DateTime now)
    {
        var gyms = new Dictionary<Guid, double>();
        var trainers = new Dictionary<Guid, double>();
        foreach (var activity in activities)
        {
            if (!activity.TargetType.HasValue || !activity.TargetId.HasValue)
            {
                continue;
            }

            var weight = RecommendationScoring.DecayedActivityWeight(
                activity.EventType,
                (now - activity.OccurredAtUtc).TotalDays);
            if (weight <= 0)
            {
                continue;
            }

            if (activity.TargetType == RecommendationTargetType.Gym)
            {
                Add(gyms, activity.TargetId.Value, weight);
            }
            else
            {
                Add(trainers, activity.TargetId.Value, weight);
                if (trainerToGym.TryGetValue(activity.TargetId.Value, out var gymId))
                {
                    Add(gyms, gymId, weight * 0.5);
                }
            }
        }

        return new ActivityScores(gyms, trainers);
    }

    internal static List<Recommendation> Balance(
        IReadOnlyList<Recommendation> rows,
        int limit)
    {
        var firstTypeCount = (limit + 1) / 2;
        var secondTypeCount = limit / 2;
        var selected = rows.Where(x => x.TargetType == RecommendationTargetType.Gym)
            .Take(firstTypeCount)
            .Concat(rows.Where(x => x.TargetType == RecommendationTargetType.Trainer)
                .Take(secondTypeCount))
            .ToList();
        if (selected.Count < limit)
        {
            selected.AddRange(rows.Where(x => selected.All(item => item.Id != x.Id))
                .Take(limit - selected.Count));
        }

        return selected.OrderByDescending(x => x.Score)
            .ThenBy(x => x.TargetType)
            .ThenBy(x => x.TargetId)
            .ToList();
    }

    internal static string BuildReason(
        Guid cityId,
        IReadOnlySet<Guid> trainingTypes,
        IReadOnlyList<PreferenceSignal> preferences,
        double activity,
        double preference,
        double popularity,
        double quality,
        double reservations)
    {
        var activityContribution = 0.3 * activity;
        var preferenceContribution = 0.5 * preference;
        var popularityContribution = 0.2 * popularity;
        if (activityContribution >= preferenceContribution &&
            activityContribution >= popularityContribution && activity > 0)
        {
            return "Preporučeno na osnovu vaših ranijih aktivnosti i rezervacija.";
        }

        if (preferenceContribution >= popularityContribution && preference > 0)
        {
            if (preferences.Any(x => trainingTypes.Contains(x.TrainingTypeId)))
            {
                return "Slično vašem preferiranom tipu treninga.";
            }

            if (preferences.Any(x => x.CityId == cityId))
            {
                return "Odgovara vašoj preferiranoj lokaciji.";
            }
        }

        if (quality >= reservations && quality >= 0.6)
        {
            return "Visoko ocijenjeno među članovima GymLinka.";
        }

        return "Popularan izbor na GymLinku.";
    }

    private static double WeightedGlobalRating(CandidateSet candidates)
    {
        var values = candidates.Gyms.Select(x => (x.RatingAverage, x.RatingCount))
            .Concat(candidates.Trainers.Select(x => (x.RatingAverage, x.RatingCount)))
            .Where(x => x.RatingCount > 0)
            .ToList();
        var count = values.Sum(x => x.RatingCount);
        return count == 0
            ? 4
            : values.Sum(x => (double)x.RatingAverage * x.RatingCount) / count;
    }

    private Guid RequireCurrentUser() =>
        currentUser.UserId ?? throw new AuthorizationDeniedException(
            "member_required",
            "An authenticated Member is required.");

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 20)
        {
            throw new ValidationException(
                "Recommendation limit must be between 1 and 20.");
        }
    }

    private static void Add(Dictionary<Guid, double> scores, Guid id, double value) =>
        scores[id] = scores.GetValueOrDefault(id) + value;

    private static double Normalize(
        IReadOnlyDictionary<Guid, double> values,
        Guid id,
        double maximum) =>
        maximum <= 0 ? 0 : values.GetValueOrDefault(id) / maximum;

    private static double MaxOrZero(IEnumerable<double> values) =>
        values.DefaultIfEmpty().Max();

    private sealed record CandidateSet(
        IReadOnlyList<GymCandidate> Gyms,
        IReadOnlyList<TrainerCandidate> Trainers);

    private sealed record GymCandidate(
        Guid Id,
        Guid TenantId,
        Guid CityId,
        string City,
        string Name,
        string? ImageUrl,
        decimal RatingAverage,
        int RatingCount,
        IReadOnlySet<Guid> TrainingTypeIds);

    private sealed record TrainerCandidate(
        Guid Id,
        Guid TenantId,
        Guid GymId,
        Guid CityId,
        string City,
        string Name,
        string? ImageUrl,
        decimal RatingAverage,
        int RatingCount,
        IReadOnlySet<Guid> TrainingTypeIds,
        IReadOnlyList<string> TrainingTypes);

    private sealed record ActivityScores(
        Dictionary<Guid, double> Gyms,
        Dictionary<Guid, double> Trainers);

    private sealed record ScoredCandidate(
        RecommendationTargetType TargetType,
        Guid TargetId,
        Guid TenantId,
        string Name,
        double Score,
        string Reason);
}
