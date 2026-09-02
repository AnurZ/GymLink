using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymLink.Application.Administration;
using GymLink.Application.Identity;
using GymLink.Application.Registration;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.ReferenceData;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GymLink.IntegrationTests;

public sealed class Review05GymCreationValidationTests
{
    private const string Password = "Test123!";
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes";

    [Fact]
    public async Task Registration_approval_revalidates_active_BiH_city_and_duplicate_atomically()
    {
        var databaseName = $"GymLink_Review05Validation_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        try
        {
            await MigrateAsync(connectionString);
            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

            var centralAdmin = await LoginAsync(client, "centraladmin");
            var validApplicant = await RegisterAsync(client, "Valid Registration Applicant");
            var duplicateApplicant = await RegisterAsync(client, "Duplicate Registration Applicant");
            var inactiveCityApplicant = await RegisterAsync(client, "Inactive City Applicant");
            var inactiveCountryApplicant = await RegisterAsync(client, "Inactive Country Applicant");
            var foreignCityApplicant = await RegisterAsync(client, "Foreign City Applicant");
            var directCandidate = await RegisterAsync(client, "Direct Gym Candidate");

            Guid bihCountryId;
            Guid validCityId;
            Guid inactiveCityId;
            Guid inactiveCountryCityId;
            Guid foreignCityId;
            Guid equipmentId;
            Guid trainingTypeId;
            ExistingGymIdentity existingGym;
            await using (var setup = CreateContext(connectionString))
            {
                bihCountryId = await setup.Countries
                    .Where(country => country.Code == "BIH")
                    .Select(country => country.Id)
                    .SingleAsync();
                validCityId = await setup.Cities
                    .Where(city => city.CountryId == bihCountryId && city.IsActive)
                    .OrderBy(city => city.Name)
                    .Select(city => city.Id)
                    .FirstAsync();
                var inactiveCity = new City
                {
                    CountryId = bihCountryId,
                    Name = $"Review05 inactive city {Guid.NewGuid():N}",
                };
                var inactiveCountryCity = new City
                {
                    CountryId = bihCountryId,
                    Name = $"Review05 inactive country city {Guid.NewGuid():N}",
                };
                var foreignCountry = new Country
                {
                    Code = $"R{Guid.NewGuid():N}"[..3].ToUpperInvariant(),
                    Name = $"Review05 foreign country {Guid.NewGuid():N}",
                };
                var foreignCity = new City
                {
                    CountryId = foreignCountry.Id,
                    Name = $"Review05 foreign city {Guid.NewGuid():N}",
                };
                setup.Cities.AddRange(inactiveCity, inactiveCountryCity, foreignCity);
                setup.Countries.Add(foreignCountry);
                await setup.SaveChangesAsync();
                inactiveCityId = inactiveCity.Id;
                inactiveCountryCityId = inactiveCountryCity.Id;
                foreignCityId = foreignCity.Id;
                equipmentId = await setup.Equipment
                    .Where(item => item.IsActive)
                    .Select(item => item.Id)
                    .FirstAsync();
                trainingTypeId = await setup.TrainingTypes
                    .Where(item => item.IsActive)
                    .Select(item => item.Id)
                    .FirstAsync();
                existingGym = await setup.Gyms.IgnoreQueryFilters()
                    .OrderBy(gym => gym.Name)
                    .Select(gym => new ExistingGymIdentity(
                        gym.CityId,
                        gym.Name,
                        gym.Address))
                    .FirstAsync();
            }

            var validRequest = await SubmitAsync(
                client,
                validApplicant,
                validCityId,
                $"  Review05 valid gym {Guid.NewGuid():N}  ",
                "  Review05 valid address  ");
            Authorize(client, centralAdmin);
            var validApproval = await client.PostAsJsonAsync(
                $"/api/admin/gym-registration-requests/{validRequest.Id}/approve",
                new { reason = "Validated immediately before creation." });
            validApproval.EnsureSuccessStatusCode();
            var approved = await validApproval.Content.ReadFromJsonAsync<GymRegistrationDto>();
            Assert.NotNull(approved);
            Assert.Equal(GymRegistrationStatus.Approved, approved.Status);
            Assert.NotNull(approved.CreatedTenantId);

            await using (var verification = CreateContext(connectionString))
            {
                var gym = await verification.Gyms.IgnoreQueryFilters()
                    .SingleAsync(item => item.TenantId == approved.CreatedTenantId);
                Assert.Equal(validRequest.GymName, gym.Name);
                Assert.Equal(validRequest.Address, gym.Address);
                Assert.False(gym.IsPubliclyVisible);
                Assert.Equal(
                    TenantStatus.PendingActivation,
                    (await verification.Tenants.SingleAsync(
                        tenant => tenant.Id == approved.CreatedTenantId)).Status);
                Assert.True(await verification.UserGymAssignments.IgnoreQueryFilters().AnyAsync(
                    assignment => assignment.TenantId == approved.CreatedTenantId &&
                                  assignment.UserId == validApplicant.User.Id &&
                                  assignment.Role == RoleNames.GymAdmin &&
                                  assignment.Status == AssignmentStatus.Active));
            }

            var duplicateRequest = await SubmitAsync(
                client,
                duplicateApplicant,
                existingGym.CityId,
                $"  {existingGym.Name}  ",
                $"  {existingGym.Address}  ");
            await AssertApprovalFailureIsAtomicAsync(
                client,
                connectionString,
                centralAdmin,
                duplicateApplicant,
                duplicateRequest.Id,
                HttpStatusCode.Conflict,
                "gym_already_exists");

            var inactiveCityRequest = await SubmitAsync(
                client,
                inactiveCityApplicant,
                inactiveCityId,
                $"Review05 inactive city gym {Guid.NewGuid():N}",
                "Inactive city address");
            await using (var mutation = CreateContext(connectionString))
            {
                (await mutation.Cities.SingleAsync(city => city.Id == inactiveCityId)).IsActive = false;
                await mutation.SaveChangesAsync();
            }
            await AssertApprovalFailureIsAtomicAsync(
                client,
                connectionString,
                centralAdmin,
                inactiveCityApplicant,
                inactiveCityRequest.Id,
                HttpStatusCode.NotFound,
                "city_not_found");

            var inactiveCountryRequest = await SubmitAsync(
                client,
                inactiveCountryApplicant,
                inactiveCountryCityId,
                $"Review05 inactive country gym {Guid.NewGuid():N}",
                "Inactive country address");
            await using (var mutation = CreateContext(connectionString))
            {
                (await mutation.Countries.SingleAsync(
                    country => country.Id == bihCountryId)).IsActive = false;
                await mutation.SaveChangesAsync();
            }
            await AssertApprovalFailureIsAtomicAsync(
                client,
                connectionString,
                centralAdmin,
                inactiveCountryApplicant,
                inactiveCountryRequest.Id,
                HttpStatusCode.NotFound,
                "city_not_found");
            await using (var mutation = CreateContext(connectionString))
            {
                (await mutation.Countries.SingleAsync(
                    country => country.Id == bihCountryId)).IsActive = true;
                await mutation.SaveChangesAsync();
            }

            var foreignRequest = await SubmitAsync(
                client,
                foreignCityApplicant,
                foreignCityId,
                $"Review05 foreign city gym {Guid.NewGuid():N}",
                "Foreign city address");
            await AssertApprovalFailureIsAtomicAsync(
                client,
                connectionString,
                centralAdmin,
                foreignCityApplicant,
                foreignRequest.Id,
                HttpStatusCode.NotFound,
                "city_not_found");

            Authorize(client, centralAdmin);
            var directForeign = await client.PostAsJsonAsync(
                "/api/admin/gyms",
                CreateDirectRequest(
                    foreignCityId,
                    directCandidate.User.Id,
                    equipmentId,
                    trainingTypeId,
                    $"Review05 direct foreign {Guid.NewGuid():N}",
                    "Direct foreign address"));
            Assert.Equal(HttpStatusCode.NotFound, directForeign.StatusCode);
            Assert.Equal("city_not_found", await ProblemCodeAsync(directForeign));

            var directDuplicate = await client.PostAsJsonAsync(
                "/api/admin/gyms",
                CreateDirectRequest(
                    existingGym.CityId,
                    directCandidate.User.Id,
                    equipmentId,
                    trainingTypeId,
                    $"  {existingGym.Name}  ",
                    $"  {existingGym.Address}  "));
            Assert.Equal(HttpStatusCode.Conflict, directDuplicate.StatusCode);
            Assert.Equal("gym_already_exists", await ProblemCodeAsync(directDuplicate));
        }
        finally
        {
            await DeleteDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task Concurrent_registration_approval_and_direct_creation_have_one_winner()
    {
        var databaseName = $"GymLink_Review05Concurrency_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        try
        {
            await MigrateAsync(connectionString);
            await using var factory = CreateFactory(connectionString);
            using var setupClient = factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await setupClient.GetAsync("/health")).StatusCode);
            var centralAdmin = await LoginAsync(setupClient, "centraladmin");
            var registrationApplicant = await RegisterAsync(
                setupClient,
                "Concurrent Registration Applicant");
            var directCandidate = await RegisterAsync(
                setupClient,
                "Concurrent Direct Candidate");
            Guid cityId;
            Guid equipmentId;
            Guid trainingTypeId;
            await using (var setup = CreateContext(connectionString))
            {
                cityId = await (
                        from city in setup.Cities
                        join country in setup.Countries on city.CountryId equals country.Id
                        where city.IsActive && country.IsActive && country.Code == "BIH"
                        orderby city.Name
                        select city.Id)
                    .FirstAsync();
                equipmentId = await setup.Equipment
                    .Where(item => item.IsActive)
                    .Select(item => item.Id)
                    .FirstAsync();
                trainingTypeId = await setup.TrainingTypes
                    .Where(item => item.IsActive)
                    .Select(item => item.Id)
                    .FirstAsync();
            }

            var name = $"Review05 concurrent gym {Guid.NewGuid():N}";
            const string address = "Review05 concurrent address";
            var registration = await SubmitAsync(
                setupClient,
                registrationApplicant,
                cityId,
                name,
                address);

            using var approvalClient = factory.CreateClient();
            using var directClient = factory.CreateClient();
            Authorize(approvalClient, centralAdmin);
            Authorize(directClient, centralAdmin);
            var responses = await Task.WhenAll(
                approvalClient.PostAsJsonAsync(
                    $"/api/admin/gym-registration-requests/{registration.Id}/approve",
                    new { reason = "Concurrent registration approval." }),
                directClient.PostAsJsonAsync(
                    "/api/admin/gyms",
                    CreateDirectRequest(
                        cityId,
                        directCandidate.User.Id,
                        equipmentId,
                        trainingTypeId,
                        name,
                        address)));

            Assert.Single(responses, response => response.IsSuccessStatusCode);
            var conflict = Assert.Single(
                responses,
                response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal("gym_already_exists", await ProblemCodeAsync(conflict));

            await using var verification = CreateContext(connectionString);
            Assert.Equal(
                1,
                await verification.Gyms.IgnoreQueryFilters().CountAsync(
                    gym => gym.CityId == cityId &&
                           gym.Name == name &&
                           gym.Address == address));
            var registrationStatus = await verification.GymRegistrationRequests
                .Where(request => request.Id == registration.Id)
                .Select(request => request.Status)
                .SingleAsync();
            Assert.Equal(
                responses[0].IsSuccessStatusCode
                    ? GymRegistrationStatus.Approved
                    : GymRegistrationStatus.Submitted,
                registrationStatus);
        }
        finally
        {
            await DeleteDatabaseAsync(connectionString);
        }
    }

    private static async Task AssertApprovalFailureIsAtomicAsync(
        HttpClient client,
        string connectionString,
        AuthSessionDto centralAdmin,
        AuthSessionDto applicant,
        Guid registrationId,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        ApprovalSnapshot before;
        await using (var context = CreateContext(connectionString))
        {
            before = await SnapshotAsync(context, applicant.User.Id);
        }

        Authorize(client, centralAdmin);
        var response = await client.PostAsJsonAsync(
            $"/api/admin/gym-registration-requests/{registrationId}/approve",
            new { reason = "Approval must fail atomically." });
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedCode, await ProblemCodeAsync(response));

        await using (var context = CreateContext(connectionString))
        {
            Assert.Equal(before, await SnapshotAsync(context, applicant.User.Id));
            var registration = await context.GymRegistrationRequests
                .SingleAsync(request => request.Id == registrationId);
            Assert.Equal(GymRegistrationStatus.Submitted, registration.Status);
            Assert.Null(registration.DecidedAtUtc);
            Assert.Null(registration.DecidedByUserId);
            Assert.Null(registration.CreatedTenantId);
        }

        Authorize(client, applicant);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/api/gym-registration-requests/mine?page=1&pageSize=10")).StatusCode);
    }

    private static async Task<ApprovalSnapshot> SnapshotAsync(
        GymLinkDbContext context,
        Guid applicantUserId) =>
        new(
            await context.Tenants.CountAsync(),
            await context.Gyms.IgnoreQueryFilters().CountAsync(),
            await context.UserGymAssignments.IgnoreQueryFilters().CountAsync(),
            await context.SecurityAuditRecords.CountAsync(),
            await context.OutboxMessages.CountAsync(),
            await context.RefreshTokenSessions.CountAsync(
                session => session.UserId == applicantUserId && session.RevokedAtUtc == null),
            await context.RefreshTokenSessions.CountAsync(
                session => session.UserId == applicantUserId && session.RevokedAtUtc != null));

    private static async Task<GymRegistrationDto> SubmitAsync(
        HttpClient client,
        AuthSessionDto applicant,
        Guid cityId,
        string name,
        string address)
    {
        Authorize(client, applicant);
        var response = await client.PostAsJsonAsync(
            "/api/gym-registration-requests",
            new
            {
                gymName = name,
                description = "A complete proposed gym description for review finding five.",
                address,
                cityId,
                latitude = 43.8563m,
                longitude = 18.4131m,
                phoneNumber = "+387 33 555 555",
            });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GymRegistrationDto>()
            ?? throw new InvalidOperationException("Gym registration returned no response body.");
    }

    private static object CreateDirectRequest(
        Guid cityId,
        Guid gymAdminUserId,
        Guid equipmentId,
        Guid trainingTypeId,
        string name,
        string address) => new
        {
            name,
            description = "A complete direct gym description for review finding five.",
            address,
            cityId,
            latitude = 43.8563m,
            longitude = 18.4131m,
            phoneNumber = "+387 33 555 555",
            workingHours = Enumerable.Range(0, 7).Select(day => new
            {
                dayOfWeek = day,
                opensAt = day is 0 or 6 ? null : "08:00:00",
                closesAt = day is 0 or 6 ? null : "22:00:00",
                isClosed = day is 0 or 6,
            }),
            equipmentIds = new[] { equipmentId },
            trainingTypeIds = new[] { trainingTypeId },
            membershipPlan = new
            {
                name = "Standard",
                durationDays = 30,
                price = 50m,
                currency = "BAM",
            },
            gymAdminUserId,
        };

    private static async Task<AuthSessionDto> RegisterAsync(
        HttpClient client,
        string displayName)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var suffix = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username = $"review05-{suffix}",
                email = $"review05-{suffix}@gymlink.local",
                displayName,
                password = Password,
            });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthSessionDto>()
            ?? throw new InvalidOperationException("Registration returned no session.");
    }

    private static async Task<AuthSessionDto> LoginAsync(HttpClient client, string identifier)
    {
        client.DefaultRequestHeaders.Authorization = null;
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

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("title").GetString()
            ?? throw new InvalidOperationException("Problem response had no title.");
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
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
            builder.UseSetting("Geocoding:Enabled", "false");
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
                    ["Geocoding:Enabled"] = "false",
                }));
        });

    private static async Task MigrateAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync();
    }

    private static async Task DeleteDatabaseAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        await context.Database.EnsureDeletedAsync();
    }

    private static GymLinkDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GymLinkDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new GymLinkDbContext(options, new TestTenantContext(null));
    }

    private sealed record ExistingGymIdentity(Guid CityId, string Name, string Address);

    private sealed record ApprovalSnapshot(
        int TenantCount,
        int GymCount,
        int AssignmentCount,
        int AuditCount,
        int OutboxCount,
        int ActiveApplicantSessionCount,
        int RevokedApplicantSessionCount);
}
