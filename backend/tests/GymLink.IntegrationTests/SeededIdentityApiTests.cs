using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using GymLink.Application.Administration;
using GymLink.Application.Catalog;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Application.Recommendations;
using GymLink.Application.Reservations;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Tenancy;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GymLink.IntegrationTests;

public sealed class SeededIdentityApiTests
{
    private const string Password = "Test123!";
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes";
    private static readonly JsonSerializerOptions WebJsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Gym_admin_can_read_anonymous_reviews_for_an_inactive_tenant_trainer()
    {
        var databaseName = $"GymLink_InactiveTrainerReviews_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        try
        {
            await using (var migrationContext = CreateContext(connectionString))
            {
                await migrationContext.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            var gymAdmin = await LoginAsync(client, "admin.respect");
            var otherGymAdmin = await LoginAsync(client, "admin.oxide");
            var member = await LoginAsync(client, "mobile1");
            var trainer = await LoginAsync(client, "respecttrainer1");

            Guid trainerProfileId;
            List<Guid> expectedReviewIds;
            await using (var baseline = CreateContext(connectionString))
            {
                trainerProfileId = await baseline.TrainerProfiles.IgnoreQueryFilters()
                    .Where(item => item.UserId == trainer.User.Id)
                    .Select(item => item.Id)
                    .SingleAsync();
                expectedReviewIds = await baseline.Reviews.IgnoreQueryFilters()
                    .Where(item => item.TrainerProfileId == trainerProfileId)
                    .OrderByDescending(item => item.CreatedAtUtc)
                    .ThenBy(item => item.Id)
                    .Select(item => item.Id)
                    .ToListAsync();
            }
            Assert.NotEmpty(expectedReviewIds);

            Authorize(client, gymAdmin);
            var deactivate = await client.PostAsJsonAsync(
                $"/api/tenant/trainers/{trainerProfileId}/deactivate",
                new { reason = "Trainer is temporarily unavailable" });
            deactivate.EnsureSuccessStatusCode();

            var response = await client.GetAsync(
                $"/api/trainers/{trainerProfileId}/reviews?page=1&pageSize=2");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var reviews = JsonSerializer.Deserialize<PagedResult<ReviewDto>>(
                json,
                WebJsonOptions);
            Assert.NotNull(reviews);
            Assert.Equal(1, reviews.Page);
            Assert.Equal(2, reviews.PageSize);
            Assert.Equal(expectedReviewIds.Count, reviews.TotalCount);
            Assert.Equal(expectedReviewIds.Take(2), reviews.Items.Select(item => item.Id));
            Assert.All(reviews.Items, item => Assert.InRange(item.Rating, 1, 5));
            Assert.DoesNotContain("reviewerUserId", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("reservationId", json, StringComparison.OrdinalIgnoreCase);

            Authorize(client, otherGymAdmin);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync(
                    $"/api/trainers/{trainerProfileId}/reviews?page=1&pageSize=10")).StatusCode);

            Authorize(client, member);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync(
                    $"/api/trainers/{trainerProfileId}/reviews?page=1&pageSize=10")).StatusCode);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Central_admin_trainer_revocation_and_account_deactivation_use_reversible_lifecycle()
    {
        var databaseName = $"GymLink_TrainerAdministration_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        try
        {
            await using (var migrationContext = CreateContext(connectionString))
            {
                await migrationContext.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            var centralAdmin = await LoginAsync(client, "centraladmin");
            var gymAdmin = await LoginAsync(client, "admin.respect");
            var originalTrainer = await LoginAsync(client, "respecttrainer1");
            Guid profileId;
            Guid assignmentId;
            int offeringCount;
            int scheduleCount;
            await using (var baseline = CreateContext(connectionString))
            {
                var profile = await baseline.TrainerProfiles.IgnoreQueryFilters()
                    .SingleAsync(item => item.UserId == originalTrainer.User.Id);
                profileId = profile.Id;
                assignmentId = await baseline.UserGymAssignments.IgnoreQueryFilters()
                    .Where(item =>
                        item.UserId == originalTrainer.User.Id &&
                        item.Role == RoleNames.Trainer)
                    .Select(item => item.Id)
                    .SingleAsync();
                offeringCount = await baseline.TrainerServiceOfferings.IgnoreQueryFilters()
                    .CountAsync(item => item.TrainerProfileId == profileId);
                scheduleCount = await baseline.TrainerAvailabilitySchedules.IgnoreQueryFilters()
                    .CountAsync(item => item.TrainerProfileId == profileId);
            }

            Authorize(client, centralAdmin);
            var revocation = await client.PostAsJsonAsync(
                "/api/admin/users/roles/revoke",
                new
                {
                    identifier = originalTrainer.User.Email,
                    reason = "Trainer contract is temporarily paused",
                });
            revocation.EnsureSuccessStatusCode();
            Assert.Equal(
                RoleNames.Member,
                (await revocation.Content.ReadFromJsonAsync<AdminUserDto>())?.Role);
            Authorize(client, originalTrainer);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await client.GetAsync("/api/profile")).StatusCode);

            Authorize(client, gymAdmin);
            var restored = await client.PostAsJsonAsync(
                $"/api/tenant/trainers/{profileId}/reactivate",
                new { reason = "Trainer contract resumed" });
            restored.EnsureSuccessStatusCode();
            Assert.True((await restored.Content.ReadFromJsonAsync<TrainerDto>())?.IsActive);
            var restoredTrainer = await LoginAsync(client, "respecttrainer1");

            Authorize(client, centralAdmin);
            var deactivation = await client.PostAsJsonAsync(
                "/api/admin/users/deactivate",
                new
                {
                    identifier = restoredTrainer.User.Email,
                    reason = "Account access is temporarily disabled",
                });
            deactivation.EnsureSuccessStatusCode();
            var inactiveAccount = await deactivation.Content.ReadFromJsonAsync<AdminUserDto>();
            Assert.NotNull(inactiveAccount);
            Assert.False(inactiveAccount.IsActive);
            Assert.Equal(RoleNames.Member, inactiveAccount.Role);

            var accountReactivation = await client.PostAsJsonAsync(
                "/api/admin/users/reactivate",
                new
                {
                    identifier = restoredTrainer.User.Email,
                    reason = "Account access restored",
                });
            accountReactivation.EnsureSuccessStatusCode();
            var memberAccount = await accountReactivation.Content.ReadFromJsonAsync<AdminUserDto>();
            Assert.NotNull(memberAccount);
            Assert.True(memberAccount.IsActive);
            Assert.Equal(RoleNames.Member, memberAccount.Role);
            var memberSession = await LoginAsync(client, "respecttrainer1");
            Assert.Equal(RoleNames.Member, memberSession.User.Role);

            Authorize(client, gymAdmin);
            var explicitReactivation = await client.PostAsJsonAsync(
                $"/api/tenant/trainers/{profileId}/reactivate",
                new { reason = "Gym administrator restored Trainer access" });
            explicitReactivation.EnsureSuccessStatusCode();
            Authorize(client, memberSession);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await client.GetAsync("/api/profile")).StatusCode);

            await using var verification = CreateContext(connectionString);
            var profileAfter = await verification.TrainerProfiles.IgnoreQueryFilters()
                .SingleAsync(item => item.UserId == originalTrainer.User.Id);
            Assert.Equal(profileId, profileAfter.Id);
            Assert.True(profileAfter.IsActive);
            var assignmentAfter = await verification.UserGymAssignments.IgnoreQueryFilters()
                .SingleAsync(item =>
                    item.UserId == originalTrainer.User.Id &&
                    item.Role == RoleNames.Trainer);
            Assert.Equal(assignmentId, assignmentAfter.Id);
            Assert.Equal(AssignmentStatus.Active, assignmentAfter.Status);
            Assert.Equal(offeringCount, await verification.TrainerServiceOfferings
                .IgnoreQueryFilters().CountAsync(item => item.TrainerProfileId == profileId));
            Assert.Equal(scheduleCount, await verification.TrainerAvailabilitySchedules
                .IgnoreQueryFilters().CountAsync(item => item.TrainerProfileId == profileId));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Manually_replaced_preferences_regenerate_and_influence_recommendations()
    {
        var databaseName = $"GymLink_ManualPreferences_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);

        try
        {
            await using (var migrationContext = CreateContext(connectionString))
            {
                await migrationContext.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            var member = await LoginAsync(client, "mobile1");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", member.AccessToken);

            var clearResponse = await client.PutAsJsonAsync(
                "/api/me/preferences",
                new { items = Array.Empty<object>() });
            clearResponse.EnsureSuccessStatusCode();
            var baseline = await client.GetFromJsonAsync<RecommendationFeedDto>(
                "/api/me/recommendations?limit=20");
            Assert.NotNull(baseline);

            var baselineGym = baseline.Items
                .Where(x => x.TargetType == RecommendationTargetType.Gym)
                .OrderBy(x => x.Score)
                .ThenBy(x => x.TargetId)
                .First();
            var baselineGymOrder = baseline.Items
                .Where(x => x.TargetType == RecommendationTargetType.Gym)
                .ToList();
            var baselineRank = baselineGymOrder.FindIndex(
                x => x.TargetId == baselineGym.TargetId);
            Guid cityId;
            Guid trainingTypeId;
            await using (var context = CreateContext(connectionString))
            {
                var matchingReference = await (
                        from gym in context.Gyms.IgnoreQueryFilters().AsNoTracking()
                        join link in context.GymTrainingTypes.IgnoreQueryFilters().AsNoTracking()
                            on gym.Id equals link.GymId
                        where gym.Id == baselineGym.TargetId
                        orderby link.TrainingTypeId
                        select new { gym.CityId, link.TrainingTypeId })
                    .FirstAsync();
                cityId = matchingReference.CityId;
                trainingTypeId = matchingReference.TrainingTypeId;
            }

            var replaceResponse = await client.PutAsJsonAsync(
                "/api/me/preferences",
                new
                {
                    items = new[]
                    {
                        new { cityId, trainingTypeId },
                    },
                });
            replaceResponse.EnsureSuccessStatusCode();
            var savedPreferences = await replaceResponse.Content
                .ReadFromJsonAsync<IReadOnlyList<PreferenceDto>>();
            var savedPreference = Assert.Single(savedPreferences!);
            Assert.Equal(1.0m, savedPreference.Weight);

            var personalized = await client.GetFromJsonAsync<RecommendationFeedDto>(
                "/api/me/recommendations?limit=20");
            Assert.NotNull(personalized);
            var personalizedGym = Assert.Single(
                personalized.Items,
                x => x.TargetType == RecommendationTargetType.Gym &&
                     x.TargetId == baselineGym.TargetId);
            Assert.True(personalizedGym.Score > baselineGym.Score);
            Assert.Contains("preferiranom tipu treninga", personalizedGym.Reason);
            var personalizedRank = personalized.Items
                .Where(x => x.TargetType == RecommendationTargetType.Gym)
                .ToList()
                .FindIndex(x => x.TargetId == baselineGym.TargetId);
            Assert.True(personalizedRank < baselineRank);
            Assert.True(personalized.GeneratedAtUtc > baseline.GeneratedAtUtc);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task CentralAdmin_user_search_handles_seeded_members_with_multiple_active_gym_assignments()
    {
        var databaseName = $"GymLink_AdminUsers_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);

        try
        {
            await using (var migrationContext = CreateContext(connectionString))
            {
                await migrationContext.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
            var centralAdmin = await LoginAsync(client, "centraladmin");

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", centralAdmin.AccessToken);
            var users = await client.GetFromJsonAsync<PagedResult<AdminUserDto>>(
                "/api/admin/users?query=mobile1&page=1&pageSize=10");
            var member = Assert.Single(users!.Items);
            Assert.Equal(RoleNames.Member, member.Role);
            Assert.Null(member.Assignment);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task User_and_trainer_candidate_searches_use_constant_query_counts()
    {
        var databaseName = $"GymLink_QueryCounts_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        var queryCounter = new QueryCountingDbCommandInterceptor();

        try
        {
            await using (var migrationContext = CreateContext(connectionString))
            {
                await migrationContext.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString, queryCounter);
            using var client = factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

            var centralAdmin = await LoginAsync(client, "centraladmin");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", centralAdmin.AccessToken);
            _ = await client.GetAsync("/api/admin/users?page=1&pageSize=1");
            var oneUserQueries = await CountQueriesAsync(
                client,
                queryCounter,
                "/api/admin/users?page=1&pageSize=1");
            queryCounter.Reset();
            var manyUsersResponse = await client.GetAsync("/api/admin/users?page=1&pageSize=20");
            manyUsersResponse.EnsureSuccessStatusCode();
            var manyUsers = await manyUsersResponse.Content
                .ReadFromJsonAsync<PagedResult<AdminUserDto>>();
            Assert.NotNull(manyUsers);
            Assert.True(manyUsers.Items.Count > 1);
            var manyUserQueries = queryCounter.Count;
            Assert.Equal(oneUserQueries, manyUserQueries);

            string gymAdminUsername;
            await using (var context = CreateContext(connectionString))
            {
                var now = DateTime.UtcNow;
                var tenantId = await context.Memberships.IgnoreQueryFilters()
                    .Where(membership =>
                        membership.Status == MembershipStatus.Active &&
                        membership.EndsAtUtc > now)
                    .GroupBy(membership => membership.TenantId)
                    .Where(group => group.Count() > 1)
                    .OrderByDescending(group => group.Count())
                    .Select(group => group.Key)
                    .FirstAsync();
                var gymAdminId = await context.UserGymAssignments.IgnoreQueryFilters()
                    .Where(assignment =>
                        assignment.TenantId == tenantId &&
                        assignment.Role == RoleNames.GymAdmin &&
                        assignment.Status == AssignmentStatus.Active)
                    .Select(assignment => assignment.UserId)
                    .SingleAsync();
                gymAdminUsername = await context.Users
                    .Where(user => user.Id == gymAdminId)
                    .Select(user => user.UserName!)
                    .SingleAsync();
            }

            var gymAdmin = await LoginAsync(client, gymAdminUsername);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", gymAdmin.AccessToken);
            _ = await client.GetAsync("/api/tenant/trainer-candidates?page=1&pageSize=1");
            var oneCandidateQueries = await CountQueriesAsync(
                client,
                queryCounter,
                "/api/tenant/trainer-candidates?page=1&pageSize=1");
            queryCounter.Reset();
            var manyCandidatesResponse = await client.GetAsync(
                "/api/tenant/trainer-candidates?page=1&pageSize=20");
            manyCandidatesResponse.EnsureSuccessStatusCode();
            var manyCandidates = await manyCandidatesResponse.Content
                .ReadFromJsonAsync<PagedResult<TrainerCandidateDto>>();
            Assert.NotNull(manyCandidates);
            Assert.True(manyCandidates.Items.Count > 1);
            var manyCandidateQueries = queryCounter.Count;
            Assert.Equal(oneCandidateQueries, manyCandidateQueries);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Public_catalog_and_personal_registration_lists_are_bounded_and_deterministic()
    {
        var databaseName = $"GymLink_BoundedCatalog_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);

        try
        {
            await using (var migrationContext = CreateContext(connectionString))
            {
                await migrationContext.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

            var gyms = await client.GetFromJsonAsync<PagedResult<JsonElement>>(
                "/api/gyms?query=Sportska%20Akademija%20Respect&page=1&pageSize=1");
            var gymId = Assert.Single(gyms!.Items).GetProperty("id").GetGuid();
            var defaultTrainers = await client.GetFromJsonAsync<PagedResult<TrainerDto>>(
                $"/api/gyms/{gymId}/trainers");
            Assert.NotNull(defaultTrainers);
            Assert.Equal(1, defaultTrainers.Page);
            Assert.Equal(20, defaultTrainers.PageSize);
            Assert.Equal(2, defaultTrainers.TotalCount);
            Assert.Equal(1, defaultTrainers.TotalPages);

            var firstTrainerPage = await client.GetFromJsonAsync<PagedResult<TrainerDto>>(
                $"/api/gyms/{gymId}/trainers?page=1&pageSize=1");
            var secondTrainerPage = await client.GetFromJsonAsync<PagedResult<TrainerDto>>(
                $"/api/gyms/{gymId}/trainers?page=2&pageSize=1");
            var firstTrainer = Assert.Single(firstTrainerPage!.Items);
            var secondTrainer = Assert.Single(secondTrainerPage!.Items);
            Assert.True(string.CompareOrdinal(firstTrainer.DisplayName, secondTrainer.DisplayName) < 0);
            Assert.Equal(firstTrainerPage.TotalCount, secondTrainerPage.TotalCount);

            var plans = await client.GetFromJsonAsync<PagedResult<JsonElement>>(
                $"/api/gyms/{gymId}/membership-plans?page=1&pageSize=100");
            Assert.NotNull(plans);
            Assert.Equal(100, plans.PageSize);
            Assert.Equal(plans.TotalCount, (long)plans.Items.Count);
            var offerings = await client.GetFromJsonAsync<PagedResult<JsonElement>>(
                $"/api/trainers/{firstTrainer.Id}/offerings?page=1&pageSize=100");
            Assert.NotNull(offerings);
            Assert.Equal(100, offerings.PageSize);
            Assert.Equal(offerings.TotalCount, (long)offerings.Items.Count);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await client.GetAsync(
                    $"/api/gyms/{gymId}/trainers?page=1&pageSize=101")).StatusCode);

            var member = await LoginAsync(client, "mobile1");
            await using (var context = CreateContext(connectionString))
            {
                var cityId = await context.Cities.AsNoTracking()
                    .Where(city => city.IsActive)
                    .OrderBy(city => city.Name)
                    .Select(city => city.Id)
                    .FirstAsync();
                var submittedAt = DateTime.UtcNow.AddDays(-3);
                context.GymRegistrationRequests.AddRange(
                    Registration(member.User.Id, cityId, "Alpha", GymRegistrationStatus.Rejected, submittedAt),
                    Registration(member.User.Id, cityId, "Beta", GymRegistrationStatus.Approved, submittedAt.AddDays(1)),
                    Registration(member.User.Id, cityId, "Gamma", GymRegistrationStatus.Submitted, submittedAt.AddDays(2)));
                await context.SaveChangesAsync();
            }

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", member.AccessToken);
            var defaultRegistrations = await client.GetFromJsonAsync<PagedResult<JsonElement>>(
                "/api/gym-registration-requests/mine");
            Assert.NotNull(defaultRegistrations);
            Assert.Equal(1, defaultRegistrations.Page);
            Assert.Equal(20, defaultRegistrations.PageSize);
            Assert.Equal(3, defaultRegistrations.TotalCount);
            Assert.Equal("Gamma", defaultRegistrations.Items[0].GetProperty("gymName").GetString());
            var secondRegistrationPage = await client.GetFromJsonAsync<PagedResult<JsonElement>>(
                "/api/gym-registration-requests/mine?page=2&pageSize=1");
            Assert.Equal("Beta", Assert.Single(secondRegistrationPage!.Items)
                .GetProperty("gymName").GetString());
            var rejectedRegistrations = await client.GetFromJsonAsync<PagedResult<JsonElement>>(
                "/api/gym-registration-requests/mine?status=Rejected&page=1&pageSize=20");
            Assert.Equal(1, rejectedRegistrations!.TotalCount);
            Assert.Equal("Alpha", Assert.Single(rejectedRegistrations.Items)
                .GetProperty("gymName").GetString());
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Seed_is_idempotent_and_every_documented_account_authenticates()
    {
        var databaseName = $"GymLink_Phase3_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);

        try
        {
            await using (var migrationContext = CreateContext(connectionString))
            {
                await migrationContext.Database.MigrateAsync();
            }

            await using (var firstFactory = CreateFactory(connectionString))
            {
                using var firstClient = firstFactory.CreateClient();
                Assert.Equal(HttpStatusCode.OK, (await firstClient.GetAsync("/health")).StatusCode);
            }
            List<(Guid UserId, RecommendationTargetType TargetType, Guid TargetId,
                decimal Score, string Reason)> firstGeneration;
            Dictionary<Guid, string> firstTrainerImageUrls;
            await using (var firstVerification = CreateContext(connectionString))
            {
                firstGeneration = (await firstVerification.Recommendations.AsNoTracking()
                        .ToListAsync())
                    .OrderBy(x => x.UserId)
                    .ThenBy(x => x.TargetType)
                    .ThenBy(x => x.TargetId)
                    .Select(x => (x.UserId, x.TargetType, x.TargetId, x.Score, x.Reason))
                    .ToList();
                Assert.Equal(72, firstGeneration.Count);
                var firstTrainerImages = await firstVerification.TrainerProfiles
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .ToListAsync();
                Assert.Equal(12, firstTrainerImages.Count);
                Assert.All(firstTrainerImages, AssertSeededTrainerImage);
                Assert.Equal(
                    12,
                    firstTrainerImages.Select(x => x.ImageUrl).Distinct().Count());
                firstTrainerImageUrls = firstTrainerImages.ToDictionary(
                    x => x.Id,
                    x => x.ImageUrl!);

                var reorderedGymImages = await firstVerification.GymImages
                    .IgnoreQueryFilters()
                    .Where(x => x.StorageKey.StartsWith("seed/oxide/"))
                    .OrderBy(x => x.SortOrder)
                    .ToListAsync();
                Assert.Equal(2, reorderedGymImages.Count);

                reorderedGymImages[0].IsPrimary = false;
                reorderedGymImages[0].SortOrder = 2;
                await firstVerification.SaveChangesAsync();

                reorderedGymImages[1].IsPrimary = true;
                reorderedGymImages[1].SortOrder = 0;
                await firstVerification.SaveChangesAsync();

                reorderedGymImages[0].SortOrder = 1;
                await firstVerification.SaveChangesAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            var invalidLogin = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { identifier = "member", password = "wrong-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, invalidLogin.StatusCode);
            using (var problem = JsonDocument.Parse(
                       await invalidLogin.Content.ReadAsStringAsync()))
            {
                Assert.Equal(
                    "invalid_credentials",
                    problem.RootElement.GetProperty("title").GetString());
                Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
            }
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.GetAsync("/health")).StatusCode);

            var accounts = new[]
            {
                new ExpectedAccount("centraladmin", RoleNames.CentralAdmin, null),
                new ExpectedAccount("admin.arena", RoleNames.GymAdmin, "Arena Sport Centar"),
                new ExpectedAccount("admin.perfectfit", RoleNames.GymAdmin, "Perfect Fit"),
                new ExpectedAccount("admin.respect", RoleNames.GymAdmin, "Sportska Akademija Respect"),
                new ExpectedAccount("admin.oxide", RoleNames.GymAdmin, "Oxide Gym"),
                new ExpectedAccount("admin.fitfactory", RoleNames.GymAdmin, "Fit Factory"),
                new ExpectedAccount("admin.iskra", RoleNames.GymAdmin, "Fitness Club Iskra"),
                new ExpectedAccount("arenatrainer1", RoleNames.Trainer, "Arena Sport Centar"),
                new ExpectedAccount("arenatrainer2", RoleNames.Trainer, "Arena Sport Centar"),
                new ExpectedAccount("perfectfittrainer1", RoleNames.Trainer, "Perfect Fit"),
                new ExpectedAccount("perfectfittrainer2", RoleNames.Trainer, "Perfect Fit"),
                new ExpectedAccount("respecttrainer1", RoleNames.Trainer, "Sportska Akademija Respect"),
                new ExpectedAccount("respecttrainer2", RoleNames.Trainer, "Sportska Akademija Respect"),
                new ExpectedAccount("oxidetrainer1", RoleNames.Trainer, "Oxide Gym"),
                new ExpectedAccount("oxidetrainer2", RoleNames.Trainer, "Oxide Gym"),
                new ExpectedAccount("fitfactorytrainer1", RoleNames.Trainer, "Fit Factory"),
                new ExpectedAccount("fitfactorytrainer2", RoleNames.Trainer, "Fit Factory"),
                new ExpectedAccount("iskratrainer1", RoleNames.Trainer, "Fitness Club Iskra"),
                new ExpectedAccount("iskratrainer2", RoleNames.Trainer, "Fitness Club Iskra"),
                new ExpectedAccount("mobile1", RoleNames.Member, null),
                new ExpectedAccount("mobile2", RoleNames.Member, null),
                new ExpectedAccount("mobile3", RoleNames.Member, null),
                new ExpectedAccount("mobile4", RoleNames.Member, null),
            };

            var sessions = new Dictionary<string, AuthSessionDto>(StringComparer.Ordinal);
            foreach (var expected in accounts)
            {
                var byUsername = await LoginAsync(client, expected.Username);
                var byEmail = await LoginAsync(client, $"{expected.Username}@gymlink.local");
                Assert.Equal(expected.Role, byUsername.User.Role);
                Assert.Equal(expected.Role, byEmail.User.Role);
                Assert.Equal(expected.TenantName, byUsername.User.Tenant?.Name);
                Assert.Equal(expected.TenantName, byEmail.User.Tenant?.Name);
                AssertTokenClaims(byUsername.AccessToken, expected);
                sessions[expected.Username] = byUsername;
            }

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", sessions["admin.respect"].AccessToken);
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.GetAsync("/api/tenant/gym")).StatusCode);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", sessions["mobile1"].AccessToken);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await client.GetAsync("/api/tenant/gym")).StatusCode);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", sessions["centraladmin"].AccessToken);
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.GetAsync("/api/admin/users")).StatusCode);
            var secondCentralAdmin = await client.PostAsJsonAsync(
                "/api/admin/users/roles/assign",
                new
                {
                    identifier = "mobile2",
                    role = RoleNames.CentralAdmin,
                    reason = "Not permitted",
                });
            Assert.Equal(HttpStatusCode.Conflict, secondCentralAdmin.StatusCode);
            using (var problem = JsonDocument.Parse(
                       await secondCentralAdmin.Content.ReadAsStringAsync()))
            {
                Assert.Equal(
                    "central_admin_fixed",
                    problem.RootElement.GetProperty("title").GetString());
            }
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.GetAsync(
                    "/api/admin/gym-registration-requests?page=1&pageSize=10")).StatusCode);

            client.DefaultRequestHeaders.Authorization = null;
            using var catalog = JsonDocument.Parse(
                await (await client.GetAsync("/api/gyms")).Content.ReadAsStringAsync());
            var names = catalog.RootElement.GetProperty("items")
                .EnumerateArray()
                .Select(x => x.GetProperty("name").GetString())
                .ToArray();
            Assert.Equal(6, names.Length);
            Assert.Contains("Arena Sport Centar", names);
            Assert.Contains("Perfect Fit", names);
            Assert.Contains("Sportska Akademija Respect", names);
            Assert.Contains("Oxide Gym", names);
            Assert.Contains("Fit Factory", names);
            Assert.Contains("Fitness Club Iskra", names);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", sessions["mobile1"].AccessToken);
            var preferences = await client.GetFromJsonAsync<IReadOnlyList<PreferenceDto>>(
                "/api/me/preferences");
            Assert.Equal(2, preferences!.Count);
            Assert.Collection(
                preferences,
                preference => Assert.Equal(1.0m, preference.Weight),
                preference => Assert.Equal(0.7m, preference.Weight));
            var feed = await client.GetFromJsonAsync<RecommendationFeedDto>(
                "/api/me/recommendations?limit=10");
            Assert.NotNull(feed);
            Assert.Equal(10, feed.Items.Count);
            Assert.Contains(feed.Items, x => x.TargetType == RecommendationTargetType.Gym);
            Assert.Contains(feed.Items, x => x.TargetType == RecommendationTargetType.Trainer);
            Assert.All(feed.Items, x => Assert.False(string.IsNullOrWhiteSpace(x.Reason)));
            var unchangedFeed = await client.GetFromJsonAsync<RecommendationFeedDto>(
                "/api/me/recommendations?limit=10");
            Assert.Equal(feed.GeneratedAtUtc, unchangedFeed!.GeneratedAtUtc);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await client.GetAsync("/api/me/recommendations?limit=0")).StatusCode);

            var concurrentRefreshes = await Task.WhenAll(
                client.PostAsync("/api/me/recommendations/refresh?limit=10", null),
                client.PostAsync("/api/me/recommendations/refresh?limit=10", null));
            Assert.All(concurrentRefreshes, response => response.EnsureSuccessStatusCode());

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", sessions["respecttrainer1"].AccessToken);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await client.GetAsync("/api/me/recommendations")).StatusCode);

            var original = sessions["mobile2"];
            var refreshResponse = await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new { refreshToken = original.RefreshToken });
            refreshResponse.EnsureSuccessStatusCode();
            var replacement = await refreshResponse.Content.ReadFromJsonAsync<AuthSessionDto>();
            Assert.NotNull(replacement);
            Assert.NotEqual(original.RefreshToken, replacement.RefreshToken);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", original.AccessToken);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/profile")).StatusCode);

            client.DefaultRequestHeaders.Authorization = null;
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync(
                    "/api/auth/refresh",
                    new { refreshToken = original.RefreshToken })).StatusCode);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", replacement.AccessToken);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/profile")).StatusCode);

            await using var verificationContext = CreateContext(connectionString);
            Assert.Equal(23, await verificationContext.UserProfiles.CountAsync());
            Assert.Equal(6, await verificationContext.Gyms.IgnoreQueryFilters().CountAsync());
            Assert.Equal(30, await verificationContext.UserGymAssignments.IgnoreQueryFilters().CountAsync());
            Assert.Equal(12, await verificationContext.TrainerProfiles.IgnoreQueryFilters().CountAsync());
            Assert.Equal(42, await verificationContext.GymWorkingHours.IgnoreQueryFilters().CountAsync());
            Assert.Equal(12, await verificationContext.MembershipPlans.IgnoreQueryFilters().CountAsync());
            Assert.Equal(36, await verificationContext.TrainerServiceOfferings.IgnoreQueryFilters().CountAsync());
            Assert.Equal(12, await verificationContext.TrainerAvailabilitySchedules.IgnoreQueryFilters().CountAsync());
            Assert.Equal(120, await verificationContext.TrainerWeeklyShifts.IgnoreQueryFilters().CountAsync());
            Assert.Equal(12, await verificationContext.MembershipRequests.IgnoreQueryFilters().CountAsync());
            Assert.Equal(12, await verificationContext.Memberships.IgnoreQueryFilters().CountAsync());
            Assert.Equal(48, await verificationContext.AppointmentReservations.IgnoreQueryFilters().CountAsync());
            Assert.Equal(24, await verificationContext.AppointmentReservations.IgnoreQueryFilters()
                .CountAsync(x => x.Status == ReservationStatus.Completed));
            Assert.Equal(24, await verificationContext.AppointmentReservations.IgnoreQueryFilters()
                .CountAsync(x => x.Status == ReservationStatus.Confirmed));
            Assert.Equal(24, await verificationContext.Reviews.IgnoreQueryFilters().CountAsync());
            Assert.Equal(12, await verificationContext.GymReviews.IgnoreQueryFilters().CountAsync());
            Assert.Equal(12, await verificationContext.GymImages.IgnoreQueryFilters().CountAsync());
            Assert.Equal(8, await verificationContext.UserPreferences.CountAsync());
            Assert.Equal(184, await verificationContext.ActivityHistory.CountAsync());
            Assert.Equal(72, await verificationContext.Recommendations.CountAsync());

            var gyms = await verificationContext.Gyms.IgnoreQueryFilters().ToListAsync();
            var tenants = await verificationContext.Tenants.ToListAsync();
            var gymIds = gyms.Select(x => x.Id).ToHashSet();
            var trainerProfiles = await verificationContext.TrainerProfiles
                .IgnoreQueryFilters()
                .ToListAsync();
            var trainerIds = trainerProfiles.Select(x => x.Id).ToHashSet();
            var hours = await verificationContext.GymWorkingHours
                .IgnoreQueryFilters()
                .ToListAsync();
            var images = await verificationContext.GymImages
                .IgnoreQueryFilters()
                .ToListAsync();
            var reorderedPrimary = Assert.Single(images, image =>
                image.StorageKey == "seed/oxide/gallery-2");
            Assert.True(reorderedPrimary.IsPrimary);
            Assert.Equal(0, reorderedPrimary.SortOrder);
            var reorderedSecondary = Assert.Single(images, image =>
                image.StorageKey == "seed/oxide/primary");
            Assert.False(reorderedSecondary.IsPrimary);
            Assert.Equal(1, reorderedSecondary.SortOrder);
            Assert.All(images, image =>
            {
                Assert.Contains("auto=format", image.PublicUrl);
                Assert.Contains("fit=crop", image.PublicUrl);
                Assert.Contains("w=1200", image.PublicUrl);
                Assert.Contains("q=75", image.PublicUrl);
            });
            var plans = await verificationContext.MembershipPlans
                .IgnoreQueryFilters()
                .ToListAsync();
            var gymEquipment = await verificationContext.GymEquipment
                .IgnoreQueryFilters()
                .ToListAsync();
            var gymTrainingTypes = await verificationContext.GymTrainingTypes
                .IgnoreQueryFilters()
                .ToListAsync();
            var assignments = await verificationContext.UserGymAssignments
                .IgnoreQueryFilters()
                .ToListAsync();
            var specializations = await verificationContext.TrainerTrainingTypes
                .IgnoreQueryFilters()
                .ToListAsync();
            var offerings = await verificationContext.TrainerServiceOfferings
                .IgnoreQueryFilters()
                .ToListAsync();
            var schedules = await verificationContext.TrainerAvailabilitySchedules
                .IgnoreQueryFilters()
                .ToListAsync();
            var shifts = await verificationContext.TrainerWeeklyShifts
                .IgnoreQueryFilters()
                .ToListAsync();

            Assert.All(gyms, gym =>
            {
                Assert.True(gym.IsPubliclyVisible);
                Assert.False(string.IsNullOrWhiteSpace(gym.Address));
                Assert.False(string.IsNullOrWhiteSpace(gym.PhoneNumber));
                Assert.Contains(tenants, tenant =>
                    tenant.Id == gym.TenantId && tenant.Status == TenantStatus.Active);
                Assert.Equal(7, hours.Count(x => x.GymId == gym.Id));
                var gymImages = images
                    .Where(x => x.GymId == gym.Id)
                    .OrderBy(x => x.SortOrder)
                    .ToList();
                Assert.Equal(2, gymImages.Count);
                Assert.True(gymImages[0].IsPrimary);
                Assert.False(gymImages[1].IsPrimary);
                Assert.Equal([0, 1], gymImages.Select(x => x.SortOrder).ToArray());
                Assert.All(gymImages, image =>
                    Assert.StartsWith(
                        "https://images.unsplash.com/",
                        image.PublicUrl,
                        StringComparison.Ordinal));
                Assert.Equal(2, gymImages.Select(x => x.PublicUrl).Distinct().Count());
                Assert.Equal(2, plans.Count(x => x.TenantId == gym.TenantId && x.IsActive));
                Assert.Contains(gymEquipment, x =>
                    x.GymId == gym.Id && x.Quantity > 0 && !string.IsNullOrWhiteSpace(x.Notes));
                Assert.Contains(gymTrainingTypes, x => x.GymId == gym.Id);
                Assert.Single(assignments, x =>
                    x.TenantId == gym.TenantId &&
                    x.Role == RoleNames.GymAdmin &&
                    x.Status == AssignmentStatus.Active);
                Assert.Equal(2, trainerProfiles.Count(x => x.TenantId == gym.TenantId));
            });

            Assert.All(trainerProfiles, trainer =>
            {
                AssertSeededTrainerImage(trainer);
                Assert.Equal(firstTrainerImageUrls[trainer.Id], trainer.ImageUrl);
                Assert.Equal(2, specializations.Count(x => x.TrainerProfileId == trainer.Id));
                var trainerOfferings = offerings.Where(x => x.TrainerProfileId == trainer.Id).ToList();
                Assert.Equal(3, trainerOfferings.Count);
                Assert.Contains(trainerOfferings, x =>
                    x.Name == "Personalni trening 60 min" && x.DurationMinutes == 60);
                Assert.Contains(trainerOfferings, x =>
                    x.Name == "Personalni trening 90 min" && x.DurationMinutes == 90);
                Assert.Equal(1, trainerOfferings.Count(x =>
                    !x.Name.StartsWith("Personalni trening", StringComparison.Ordinal) &&
                    x.DurationMinutes == 60));
                var schedule = Assert.Single(schedules, x => x.TrainerProfileId == trainer.Id);
                var trainerShifts = shifts.Where(x =>
                    x.TrainerAvailabilityScheduleId == schedule.Id).ToList();
                Assert.Equal(10, trainerShifts.Count);
                Assert.All(trainerShifts, x => Assert.InRange(x.DayOfWeek, DayOfWeek.Monday, DayOfWeek.Friday));
                Assert.Equal(5, trainerShifts.Count(x => x.Period == TrainerShiftPeriod.Morning));
                Assert.Equal(5, trainerShifts.Count(x => x.Period == TrainerShiftPeriod.Evening));
            });

            foreach (var trainer in trainerProfiles)
            {
                using var response = await client.GetAsync(trainer.ImageUrl);
                response.EnsureSuccessStatusCode();
                Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
                Assert.Equal(trainer.ImageFileSizeBytes, response.Content.Headers.ContentLength);
                Assert.True(response.Headers.CacheControl?.Public);
                Assert.Equal(TimeSpan.FromDays(365), response.Headers.CacheControl?.MaxAge);
                Assert.Contains("immutable", response.Headers.GetValues("Cache-Control").Single());
            }

            var membershipRequests = await verificationContext.MembershipRequests
                .IgnoreQueryFilters()
                .ToListAsync();
            var memberships = await verificationContext.Memberships
                .IgnoreQueryFilters()
                .ToListAsync();
            foreach (var memberUsername in new[] { "mobile1", "mobile2", "mobile3", "mobile4" })
            {
                var memberId = sessions[memberUsername].User.Id;
                Assert.Equal(3, memberships.Count(x => x.MemberUserId == memberId));
                Assert.Equal(3, assignments.Count(x =>
                    x.UserId == memberId &&
                    x.Role == RoleNames.Member &&
                    x.Status == AssignmentStatus.Active));
            }

            Assert.All(membershipRequests, request =>
                Assert.Equal(MembershipRequestStatus.Approved, request.Status));
            Assert.All(memberships, membership =>
            {
                Assert.Equal(MembershipStatus.Active, membership.Status);
                Assert.NotNull(membership.StartsAtUtc);
                Assert.NotNull(membership.EndsAtUtc);
                Assert.Equal(90, (membership.EndsAtUtc!.Value - membership.StartsAtUtc!.Value).TotalDays);
                Assert.Contains(membershipRequests, request =>
                    request.Id == membership.MembershipRequestId &&
                    request.MemberUserId == membership.MemberUserId &&
                    request.TenantId == membership.TenantId);
            });

            var reservations = await verificationContext.AppointmentReservations
                .IgnoreQueryFilters()
                .OrderBy(x => x.StartsAtUtc)
                .ToListAsync();
            var completed = reservations.Where(x => x.Status == ReservationStatus.Completed).ToList();
            var confirmed = reservations.Where(x => x.Status == ReservationStatus.Confirmed).ToList();
            Assert.Equal(new DateTime(2026, 8, 3, 7, 0, 0, DateTimeKind.Utc), completed[0].StartsAtUtc);
            Assert.Equal(new DateTime(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc), completed[^1].StartsAtUtc);
            Assert.Equal(new DateTime(2026, 8, 24, 14, 0, 0, DateTimeKind.Utc), confirmed[0].StartsAtUtc);
            Assert.Equal(new DateTime(2026, 9, 25, 16, 0, 0, DateTimeKind.Utc), confirmed[^1].StartsAtUtc);
            foreach (var trainerId in trainerIds)
            {
                var trainerReservations = reservations
                    .Where(x => x.TrainerProfileId == trainerId)
                    .OrderBy(x => x.StartsAtUtc)
                    .ToList();
                Assert.Equal(2, trainerReservations.Count(x => x.Status == ReservationStatus.Completed));
                Assert.Equal(2, trainerReservations.Count(x => x.Status == ReservationStatus.Confirmed));
                for (var index = 1; index < trainerReservations.Count; index++)
                {
                    Assert.True(trainerReservations[index - 1].EndsAtUtc <= trainerReservations[index].StartsAtUtc);
                }
            }

            var trainerReviews = await verificationContext.Reviews.IgnoreQueryFilters().ToListAsync();
            var gymReviews = await verificationContext.GymReviews.IgnoreQueryFilters().ToListAsync();
            Assert.All(trainerReviews, review => Assert.InRange(review.Rating, 3, 5));
            Assert.All(gymReviews, review => Assert.InRange(review.Rating, 3, 5));
            Assert.All(trainerProfiles, trainer =>
            {
                var ratings = trainerReviews.Where(x => x.TrainerProfileId == trainer.Id).ToList();
                Assert.Equal(ratings.Count, trainer.ReviewCount);
                Assert.Equal(
                    decimal.Round(ratings.Average(x => (decimal)x.Rating), 2),
                    trainer.AverageRating);
            });
            Assert.All(gyms, gym =>
            {
                var ratings = gymReviews.Where(x => x.GymId == gym.Id).ToList();
                Assert.Equal(ratings.Count, gym.ReviewCount);
                Assert.Equal(
                    decimal.Round(ratings.Average(x => (decimal)x.Rating), 2),
                    gym.AverageRating);
            });

            var activities = await verificationContext.ActivityHistory.ToListAsync();
            Assert.All(activities.Where(x => x.TargetType == RecommendationTargetType.Gym), activity =>
            {
                Assert.NotNull(activity.TargetId);
                Assert.Contains(activity.TargetId.Value, gymIds);
                Assert.Equal(
                    gyms.Single(x => x.Id == activity.TargetId.Value).TenantId,
                    activity.TargetTenantId);
            });
            Assert.All(activities.Where(x => x.TargetType == RecommendationTargetType.Trainer), activity =>
            {
                Assert.NotNull(activity.TargetId);
                Assert.Contains(activity.TargetId.Value, trainerIds);
                Assert.Equal(
                    trainerProfiles.Single(x => x.Id == activity.TargetId.Value).TenantId,
                    activity.TargetTenantId);
            });
            var recommendations = await verificationContext.Recommendations.ToListAsync();
            var secondGeneration = recommendations
                .OrderBy(x => x.UserId)
                .ThenBy(x => x.TargetType)
                .ThenBy(x => x.TargetId)
                .Select(x => (x.UserId, x.TargetType, x.TargetId, x.Score, x.Reason))
                .ToList();
            Assert.Equal(firstGeneration.Count, secondGeneration.Count);
            for (var index = 0; index < firstGeneration.Count; index++)
            {
                Assert.Equal(firstGeneration[index].UserId, secondGeneration[index].UserId);
                Assert.Equal(firstGeneration[index].TargetType, secondGeneration[index].TargetType);
                Assert.Equal(firstGeneration[index].TargetId, secondGeneration[index].TargetId);
                Assert.Equal(firstGeneration[index].Reason, secondGeneration[index].Reason);
                Assert.InRange(
                    decimal.Abs(firstGeneration[index].Score - secondGeneration[index].Score),
                    0m,
                    0.00001m);
            }
            Assert.Equal(
                recommendations.Count,
                recommendations.Select(x => new { x.UserId, x.TargetType, x.TargetId }).Distinct().Count());
            Assert.All(recommendations, recommendation =>
            {
                Assert.InRange(recommendation.Score, 0, 1);
                Assert.Equal("gymlink-hybrid-v1", recommendation.AlgorithmVersion);
                Assert.False(string.IsNullOrWhiteSpace(recommendation.Reason));
                if (recommendation.TargetType == RecommendationTargetType.Gym)
                {
                    Assert.Contains(recommendation.TargetId, gymIds);
                    Assert.Equal(
                        gyms.Single(x => x.Id == recommendation.TargetId).TenantId,
                        recommendation.TargetTenantId);
                }
                else
                {
                    Assert.Contains(recommendation.TargetId, trainerIds);
                    Assert.Equal(
                        trainerProfiles.Single(x => x.Id == recommendation.TargetId).TenantId,
                        recommendation.TargetTenantId);
                }
            });
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<AuthSessionDto> LoginAsync(HttpClient client, string identifier)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { identifier, password = Password });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthSessionDto>()
            ?? throw new InvalidOperationException("Login returned no session.");
    }

    private static void Authorize(HttpClient client, AuthSessionDto session) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

    private static void AssertSeededTrainerImage(
        GymLink.Domain.Trainers.TrainerProfile trainer)
    {
        Assert.False(string.IsNullOrWhiteSpace(trainer.ImageStorageKey));
        Assert.EndsWith(".jpg", trainer.ImageStorageKey, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(trainer.ImageUrl));
        Assert.StartsWith("/uploads/trainer-images/", trainer.ImageUrl, StringComparison.Ordinal);
        Assert.Equal("image/jpeg", trainer.ImageContentType);
        Assert.InRange(
            trainer.ImageFileSizeBytes!.Value,
            1,
            150 * 1024);
    }

    private static void AssertTokenClaims(string value, ExpectedAccount expected)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(value);
        Assert.Equal(
            expected.Role,
            token.Claims.Single(x => x.Type == ClaimTypes.Role).Value);
        Assert.Single(token.Claims, x => x.Type == JwtRegisteredClaimNames.Sub);
        Assert.Single(token.Claims, x => x.Type == JwtRegisteredClaimNames.Jti);
        Assert.Single(token.Claims, x => x.Type == JwtRegisteredClaimNames.Iat);
        Assert.Single(token.Claims, x => x.Type == JwtRegisteredClaimNames.Nbf);
        Assert.Single(token.Claims, x => x.Type == JwtRegisteredClaimNames.Exp);
        Assert.Single(token.Claims, x => x.Type == "sid");
        Assert.Single(token.Claims, x => x.Type == "token_version");
        if (expected.TenantName is null)
        {
            Assert.DoesNotContain(token.Claims, x => x.Type is "tenant_id" or "tenant_role");
        }
        else
        {
            Assert.Single(token.Claims, x => x.Type == "tenant_id");
            Assert.Equal(
                expected.Role,
                token.Claims.Single(x => x.Type == "tenant_role").Value);
        }
    }

    private static async Task<int> CountQueriesAsync(
        HttpClient client,
        QueryCountingDbCommandInterceptor queryCounter,
        string path)
    {
        queryCounter.Reset();
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return queryCounter.Count;
    }

    private static GymRegistrationRequest Registration(
        Guid applicantUserId,
        Guid cityId,
        string gymName,
        GymRegistrationStatus status,
        DateTime submittedAtUtc) => new()
        {
            ApplicantUserId = applicantUserId,
            ProposedGymName = gymName,
            ProposedDescription = $"Description for {gymName}",
            ProposedAddress = $"{gymName} address",
            CityId = cityId,
            Latitude = 43.8563m,
            Longitude = 18.4131m,
            Status = status,
            SubmittedAtUtc = submittedAtUtc,
            DecisionReason = status is GymRegistrationStatus.Approved or GymRegistrationStatus.Rejected
                ? "Reviewed for pagination test"
                : null,
        };

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        QueryCountingDbCommandInterceptor? queryCounter = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:GymLink", connectionString);
            builder.UseSetting("Jwt:Issuer", "GymLink.Tests");
            builder.UseSetting("Jwt:Audience", "GymLink.Tests.Client");
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("Jwt:AccessTokenMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenDays", "30");
            builder.UseSetting(
                "PasswordReset:CodePepper",
                "integration-test-reset-pepper-at-least-32-bytes");
            builder.UseSetting("Seed:Enabled", "true");
            builder.UseSetting("Seed:DefaultPassword", Password);
            builder.UseSetting("RabbitMq:Enabled", "false");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:GymLink"] = connectionString,
                    ["Jwt:Issuer"] = "GymLink.Tests",
                    ["Jwt:Audience"] = "GymLink.Tests.Client",
                    ["Jwt:SigningKey"] = SigningKey,
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Jwt:RefreshTokenDays"] = "30",
                    ["PasswordReset:CodePepper"] =
                        "integration-test-reset-pepper-at-least-32-bytes",
                    ["Seed:Enabled"] = "true",
                    ["Seed:DefaultPassword"] = Password,
                    ["RabbitMq:Enabled"] = "false",
                }));
            if (queryCounter is not null)
            {
                builder.ConfigureServices(services =>
                    services.AddDbContext<GymLinkDbContext>((_, options) =>
                        options.AddInterceptors(queryCounter)));
            }
        });

    private static GymLinkDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GymLinkDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new GymLinkDbContext(options, new TestTenantContext(null));
    }

    private sealed record ExpectedAccount(
        string Username,
        string Role,
        string? TenantName);
}
