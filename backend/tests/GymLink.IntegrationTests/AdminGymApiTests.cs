using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymLink.Application.Administration;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Application.ReferenceData;
using GymLink.Domain.Catalog;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.ReferenceData;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GymLink.IntegrationTests;

public sealed class AdminGymApiTests
{
    private const string Password = "Test123!";
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes";

    [Fact]
    public async Task CentralAdmin_creates_complete_private_gym_and_can_activate_it()
    {
        var databaseName = $"GymLink_AdminGym_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        try
        {
            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
            var centralAdmin = await LoginAsync(client, "centraladmin");
            var member = await LoginAsync(client, "member");
            var secondMember = await LoginAsync(client, "mobile");

            Authorize(client, centralAdmin);
            var countries = await client.GetFromJsonAsync<PagedResult<CountryDto>>(
                "/api/admin/reference-data/countries?query=BIH&isActive=true&page=1&pageSize=10");
            var country = Assert.Single(countries!.Items, x => x.Code == "BIH");
            var locations = await client.GetFromJsonAsync<PagedResult<CityDto>>(
                $"/api/admin/reference-data/cities?countryId={country.Id}&isActive=true&page=1&pageSize=100");
            Assert.True(locations!.TotalCount >= 140);
            var sarajevoLocations = await client.GetFromJsonAsync<PagedResult<CityDto>>(
                $"/api/admin/reference-data/cities?query=Sarajevo&countryId={country.Id}&isActive=true&page=1&pageSize=10");
            var cityId = Assert.Single(
                sarajevoLocations!.Items,
                x => x.Name == "Sarajevo").Id;
            var equipment = await client.GetFromJsonAsync<PagedResult<EquipmentDto>>(
                "/api/admin/reference-data/equipment?isActive=true&page=1&pageSize=10");
            var trainingTypes = await client.GetFromJsonAsync<PagedResult<TrainingTypeDto>>(
                "/api/admin/reference-data/training-types?isActive=true&page=1&pageSize=10");
            var equipmentId = Assert.Single(equipment!.Items.Take(1)).Id;
            var trainingTypeId = Assert.Single(trainingTypes!.Items.Take(1)).Id;
            var accentedLocations = await client.GetFromJsonAsync<PagedResult<CityDto>>(
                $"/api/admin/reference-data/cities?query=%C5%BDiv&countryId={country.Id}&isActive=true&page=1&pageSize=10");
            Assert.Contains(accentedLocations!.Items, x => x.Name == "Živinice");

            var candidates = await client.GetFromJsonAsync<PagedResult<AdminUserDto>>(
                "/api/admin/users?query=Role%20Test&role=Member&isActive=true&page=1&pageSize=10");
            Assert.Contains(candidates!.Items, x => x.Email == member.User.Email);

            Authorize(client, member);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await client.PostAsJsonAsync(
                    "/api/admin/gyms",
                    CreateRequest(cityId, member.User.Id, equipmentId, trainingTypeId))).StatusCode);

            Authorize(client, centralAdmin);
            var create = await client.PostAsJsonAsync(
                "/api/admin/gyms",
                CreateRequest(cityId, member.User.Id, equipmentId, trainingTypeId));
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var gym = await create.Content.ReadFromJsonAsync<AdminGymDto>();
            Assert.NotNull(gym);
            Assert.Equal(TenantStatus.PendingActivation, gym.Status);
            Assert.False(gym.IsPubliclyVisible);
            Assert.Equal(1, gym.ActiveGymAdminCount);
            Assert.True(gym.CanActivate);
            Assert.Empty(gym.MissingActivationRequirements);

            var search = await client.GetFromJsonAsync<PagedResult<AdminGymDto>>(
                "/api/admin/gyms?query=Stabilization&page=1&pageSize=10");
            var searchedGym = Assert.Single(search!.Items);
            Assert.Equal(gym.Id, searchedGym.Id);
            Assert.Equal(gym.TenantId, searchedGym.TenantId);

            var secondCreate = await client.PostAsJsonAsync(
                "/api/admin/gyms",
                CreateRequest(cityId, secondMember.User.Id, equipmentId, trainingTypeId));
            Assert.Equal(HttpStatusCode.Created, secondCreate.StatusCode);
            var secondGym = await secondCreate.Content.ReadFromJsonAsync<AdminGymDto>();
            Assert.NotNull(secondGym);

            await using (var makeIncomplete = CreateContext(connectionString))
            {
                var plan = await makeIncomplete.MembershipPlans.IgnoreQueryFilters()
                    .SingleAsync(x => x.TenantId == secondGym.TenantId);
                plan.IsActive = false;
                await makeIncomplete.SaveChangesAsync();
            }
            var incomplete = await client.PostAsync(
                $"/api/admin/tenants/{secondGym.TenantId}/activate",
                content: null);
            Assert.Equal(HttpStatusCode.Conflict, incomplete.StatusCode);
            Assert.Equal("tenant_catalog_incomplete", await ProblemCodeAsync(incomplete));

            var activate = await client.PostAsync(
                $"/api/admin/tenants/{gym.TenantId}/activate",
                content: null);
            Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

            var repeatedAssignment = await client.PostAsJsonAsync(
                "/api/admin/users/roles/assign",
                new
                {
                    identifier = member.User.Email,
                    role = RoleNames.GymAdmin,
                    tenantId = gym.TenantId,
                    reason = "Repeat the same assignment.",
                });
            repeatedAssignment.EnsureSuccessStatusCode();

            var assignedElsewhere = await client.PostAsJsonAsync(
                "/api/admin/users/roles/assign",
                new
                {
                    identifier = member.User.Email,
                    role = RoleNames.GymAdmin,
                    tenantId = secondGym.TenantId,
                    reason = "Attempt to move without revocation.",
                });
            Assert.Equal(HttpStatusCode.Conflict, assignedElsewhere.StatusCode);
            Assert.Equal(
                "gym_admin_already_assigned",
                await ProblemCodeAsync(assignedElsewhere));
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.GetAsync("/health")).StatusCode);

            await using var verification = CreateContext(connectionString);
            var persistedGym = await verification.Gyms.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == gym.Id);
            Assert.True(persistedGym.IsPubliclyVisible);
            Assert.Equal(
                TenantStatus.Active,
                (await verification.Tenants.SingleAsync(x => x.Id == gym.TenantId)).Status);
            Assert.True(await verification.SecurityAuditRecords.AnyAsync(
                x => x.TargetTenantId == gym.TenantId &&
                     x.TargetId == gym.Id &&
                     x.Action == "gym.created" &&
                     x.TargetType == nameof(Gym)));
            Assert.Equal(
                1,
                await verification.UserGymAssignments.IgnoreQueryFilters().CountAsync(
                    x => x.TenantId == gym.TenantId &&
                         x.Role == RoleNames.GymAdmin &&
                         x.Status == AssignmentStatus.Active));
            Assert.True(await verification.UserGymAssignments.IgnoreQueryFilters().AnyAsync(
                x => x.TenantId == secondGym.TenantId &&
                     x.Role == RoleNames.GymAdmin &&
                     x.Status == AssignmentStatus.Active));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Concurrent_complete_gym_creation_with_same_GymAdmin_has_one_winner()
    {
        var databaseName = $"GymLink_AdminAssignment_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        try
        {
            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var setupClient = factory.CreateClient();
            var centralAdmin = await LoginAsync(setupClient, "centraladmin");
            var firstCandidate = await LoginAsync(setupClient, "member");
            Authorize(setupClient, centralAdmin);

            var countries = await setupClient.GetFromJsonAsync<PagedResult<CountryDto>>(
                "/api/admin/reference-data/countries?query=BIH&isActive=true&page=1&pageSize=10");
            var country = Assert.Single(countries!.Items, x => x.Code == "BIH");
            var cities = await setupClient.GetFromJsonAsync<PagedResult<CityDto>>(
                $"/api/admin/reference-data/cities?query=Sarajevo&countryId={country.Id}&isActive=true&page=1&pageSize=10");
            var cityId = Assert.Single(cities!.Items, x => x.Name == "Sarajevo").Id;
            var equipment = await setupClient.GetFromJsonAsync<PagedResult<EquipmentDto>>(
                "/api/admin/reference-data/equipment?isActive=true&page=1&pageSize=10");
            var trainingTypes = await setupClient.GetFromJsonAsync<PagedResult<TrainingTypeDto>>(
                "/api/admin/reference-data/training-types?isActive=true&page=1&pageSize=10");
            var equipmentId = Assert.Single(equipment!.Items.Take(1)).Id;
            var trainingTypeId = Assert.Single(trainingTypes!.Items.Take(1)).Id;

            using var firstClient = factory.CreateClient();
            using var secondClient = factory.CreateClient();
            Authorize(firstClient, centralAdmin);
            Authorize(secondClient, centralAdmin);
            var responses = await Task.WhenAll(
                firstClient.PostAsJsonAsync(
                    "/api/admin/gyms",
                    CreateRequest(
                        cityId,
                        firstCandidate.User.Id,
                        equipmentId,
                        trainingTypeId)),
                secondClient.PostAsJsonAsync(
                    "/api/admin/gyms",
                    CreateRequest(
                        cityId,
                        firstCandidate.User.Id,
                        equipmentId,
                        trainingTypeId)));

            Assert.Single(responses, x => x.IsSuccessStatusCode);
            var conflict = Assert.Single(
                responses,
                x => x.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal("gym_admin_already_assigned", await ProblemCodeAsync(conflict));

            await using var verification = CreateContext(connectionString);
            Assert.Equal(
                1,
                await verification.UserGymAssignments.IgnoreQueryFilters().CountAsync(
                    x => x.UserId == firstCandidate.User.Id &&
                         x.Role == RoleNames.GymAdmin &&
                         x.Status == AssignmentStatus.Active));
            Assert.Equal(
                1,
                await verification.Tenants.CountAsync(
                    x => x.Name.StartsWith("Stabilization Gym")));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Location_catalog_migration_reuses_existing_BiH_and_city_rows()
    {
        var databaseName = $"GymLink_LocationCatalog_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        Guid countryId;
        Guid sarajevoId;
        try
        {
            await using (var before = CreateContext(connectionString))
            {
                var migrator = before.GetService<IMigrator>();
                await migrator.MigrateAsync("20260728140017_Phase7NotificationsWorker");
                var country = new Country
                {
                    Code = "BIH",
                    Name = "Bosnia and Herzegovina",
                };
                countryId = country.Id;
                var sarajevo = new City
                {
                    CountryId = countryId,
                    Name = "Sarajevo",
                };
                sarajevoId = sarajevo.Id;
                before.Countries.Add(country);
                before.Cities.Add(sarajevo);
                await before.SaveChangesAsync();
                await migrator.MigrateAsync();
            }

            await using var verification = CreateContext(connectionString);
            Assert.Equal(
                countryId,
                (await verification.Countries.SingleAsync(x => x.Code == "BIH")).Id);
            Assert.Equal(
                "Bosnia and Herzegovina",
                (await verification.Countries.SingleAsync(x => x.Code == "BIH")).Name);
            Assert.Equal(
                sarajevoId,
                (await verification.Cities.SingleAsync(
                    x => x.CountryId == countryId && x.Name == "Sarajevo")).Id);
            Assert.True(await verification.Cities.CountAsync(
                x => x.CountryId == countryId && x.IsActive) >= 140);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task CentralAdmin_location_search_is_bounded_cached_and_maps_Sarajevo()
    {
        var databaseName = $"GymLink_LocationSearch_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        var handler = new NominatimHandler(
            """
            [
              {
                "osm_type": "relation",
                "osm_id": 100,
                "display_name": "Grad Sarajevo, Kanton Sarajevo, Federacija Bosne i Hercegovine, Bosna i Hercegovina",
                "lat": "43.856300",
                "lon": "18.413100",
                "address": {
                  "city": "Grad Sarajevo",
                  "state": "Federacija Bosne i Hercegovine",
                  "country_code": "ba"
                }
              },
              {
                "osm_type": "node",
                "osm_id": 101,
                "display_name": "Trg oslobođenja, Sarajevo, Kanton Sarajevo, Bosna i Hercegovina",
                "lat": "43.858000",
                "lon": "18.421000",
                "address": {
                  "city": "Sarajevo",
                  "neighbourhood": "Mjesna zajednica Trg oslobođenja",
                  "state": "Federacija Bosne i Hercegovine",
                  "country_code": "ba"
                }
              },
              {
                "osm_type": "relation",
                "osm_id": 102,
                "display_name": "Trnovo, Kanton Sarajevo, Federacija Bosne i Hercegovine, Bosna i Hercegovina",
                "lat": "43.665000",
                "lon": "18.445000",
                "address": {
                  "municipality": "Općina Trnovo",
                  "state": "Federacija Bosne i Hercegovine",
                  "country_code": "ba"
                }
              },
              {
                "osm_type": "relation",
                "osm_id": 103,
                "display_name": "Trnovo, Istočno Sarajevo, Republika Srpska, Bosna i Hercegovina",
                "lat": "43.658000",
                "lon": "18.448000",
                "address": {
                  "municipality": "Opština Trnovo",
                  "state": "Republika Srpska",
                  "country_code": "ba"
                }
              },
              {
                "osm_type": "node",
                "osm_id": 104,
                "display_name": "Sarajevo, Hrvatska",
                "lat": "45.800000",
                "lon": "16.000000",
                "address": {
                  "city": "Sarajevo",
                  "country_code": "hr"
                }
              }
            ]
            """);
        try
        {
            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString, handler);
            using var client = factory.CreateClient();
            var centralAdmin = await LoginAsync(client, "centraladmin");
            var member = await LoginAsync(client, "member");

            Authorize(client, member);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await client.GetAsync(
                    "/api/admin/locations/search?query=Sarajevo")).StatusCode);

            Authorize(client, centralAdmin);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await client.GetAsync("/api/admin/locations/search?query=S")).StatusCode);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await client.GetAsync(
                    $"/api/admin/locations/search?query={new string('a', 201)}")).StatusCode);
            var first = await client.GetFromJsonAsync<LocationSearchResultDto[]>(
                "/api/admin/locations/search?query=Sarajevo");
            var second = await client.GetFromJsonAsync<LocationSearchResultDto[]>(
                "/api/admin/locations/search?query=Sarajevo");

            Assert.Equal(4, first!.Length);
            Assert.Equal(2, first.Count(result => result.CityName == "Sarajevo"));
            Assert.Contains(first, result => result.CityName == "Trnovo (Federacija BiH)");
            Assert.Contains(first, result => result.CityName == "Trnovo (Republika Srpska)");
            Assert.Equal(first, second);
            Assert.Equal(1, handler.RequestCount);
            Assert.Contains("countrycodes=ba", handler.RequestUri!.Query);
            Assert.Contains("layer=address", handler.RequestUri.Query);
            Assert.Contains("limit=8", handler.RequestUri.Query);
            Assert.Contains("accept-language=bs", handler.RequestUri.Query);
            Assert.Contains("GymLink", handler.UserAgent);

            handler.StatusCode = HttpStatusCode.BadGateway;
            var unavailable = await client.GetAsync(
                "/api/admin/locations/search?query=Mostar");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal("location_search_unavailable", await ProblemCodeAsync(unavailable));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task CentralAdmin_reverse_geocoding_resolves_BiH_map_clicks_safely()
    {
        var databaseName = $"GymLink_ReverseGeocoding_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        var handler = new NominatimHandler(
            """
            {
              "osm_type": "way",
              "osm_id": 200,
              "display_name": "Zmaja od Bosne 12, Sarajevo, Bosna i Hercegovina",
              "lat": "43.856900",
              "lon": "18.412500",
              "address": {
                "road": "Zmaja od Bosne",
                "house_number": "12",
                "city": "Sarajevo",
                "state": "Federacija Bosne i Hercegovine",
                "country_code": "ba"
              }
            }
            """);
        try
        {
            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString, handler);
            using var client = factory.CreateClient();
            var centralAdmin = await LoginAsync(client, "centraladmin");
            var member = await LoginAsync(client, "member");

            Authorize(client, member);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await client.GetAsync(
                    "/api/admin/locations/reverse?latitude=43.8563&longitude=18.4131")).StatusCode);

            Authorize(client, centralAdmin);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await client.GetAsync(
                    "/api/admin/locations/reverse?latitude=91&longitude=18.4131")).StatusCode);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await client.GetAsync(
                    "/api/admin/locations/reverse?longitude=18.4131")).StatusCode);

            var first = await client.GetFromJsonAsync<LocationReverseResultDto>(
                "/api/admin/locations/reverse?latitude=43.856301&longitude=18.413101");
            var cached = await client.GetFromJsonAsync<LocationReverseResultDto>(
                "/api/admin/locations/reverse?latitude=43.856304&longitude=18.413104");

            Assert.NotNull(first);
            Assert.Equal("Zmaja od Bosne 12, Sarajevo, Bosna i Hercegovina", first.Address);
            Assert.Equal("Sarajevo", first.CityName);
            Assert.Equal(first, cached);
            Assert.Equal(1, handler.RequestCount);
            Assert.Contains("/reverse", handler.RequestUri!.AbsolutePath);
            Assert.Contains("lat=43.85630", handler.RequestUri.Query);
            Assert.Contains("lon=18.41310", handler.RequestUri.Query);
            Assert.Contains("format=jsonv2", handler.RequestUri.Query);
            Assert.Contains("addressdetails=1", handler.RequestUri.Query);
            Assert.Contains("zoom=18", handler.RequestUri.Query);
            Assert.Contains("layer=address", handler.RequestUri.Query);
            Assert.Contains("accept-language=bs", handler.RequestUri.Query);

            handler.ResponseBody =
                """
                {
                  "osm_type": "way",
                  "osm_id": 201,
                  "display_name": "Dubrovnik, Hrvatska",
                  "lat": "42.6500",
                  "lon": "18.0900",
                  "address": {
                    "city": "Dubrovnik",
                    "country_code": "hr"
                  }
                }
                """;
            var outside = await client.GetAsync(
                "/api/admin/locations/reverse?latitude=42.6500&longitude=18.0900");
            Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);
            Assert.Equal("location_outside_bih", await ProblemCodeAsync(outside));
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.GetAsync("/health")).StatusCode);

            handler.ResponseBody =
                """
                {
                  "osm_type": "way",
                  "osm_id": 202,
                  "display_name": "Nepoznata lokacija, Bosna i Hercegovina",
                  "lat": "44.1000",
                  "lon": "17.9000",
                  "address": {
                    "village": "Nepostojeće mjesto",
                    "country_code": "ba"
                  }
                }
                """;
            var unmapped = await client.GetAsync(
                "/api/admin/locations/reverse?latitude=44.1000&longitude=17.9000");
            Assert.Equal(HttpStatusCode.NotFound, unmapped.StatusCode);
            Assert.Equal("location_not_resolved", await ProblemCodeAsync(unmapped));

            handler.StatusCode = HttpStatusCode.NotFound;
            var noResult = await client.GetAsync(
                "/api/admin/locations/reverse?latitude=44.2000&longitude=17.8000");
            Assert.Equal(HttpStatusCode.NotFound, noResult.StatusCode);
            Assert.Equal("location_not_resolved", await ProblemCodeAsync(noResult));

            handler.StatusCode = HttpStatusCode.BadGateway;
            var unavailable = await client.GetAsync(
                "/api/admin/locations/reverse?latitude=44.3000&longitude=17.7000");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal("location_search_unavailable", await ProblemCodeAsync(unavailable));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static object CreateRequest(
        Guid cityId,
        Guid gymAdminUserId,
        Guid equipmentId,
        Guid trainingTypeId) => new
        {
            name = $"Stabilization Gym {Guid.NewGuid():N}",
            description = "A complete gym description used by the integration test.",
            address = "Testna 42",
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
            gymAdminAssignmentReason = "Assigned during complete gym creation.",
        };

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

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        HttpMessageHandler? geocodingHandler = null) =>
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
            builder.UseSetting("Geocoding:Enabled", geocodingHandler is null ? "false" : "true");
            builder.UseSetting("Geocoding:BaseUrl", "https://nominatim.test");
            builder.UseSetting("Geocoding:UserAgent", "GymLink.Tests/1.0");
            builder.UseSetting("Geocoding:TimeoutSeconds", "5");
            builder.UseSetting("Geocoding:CacheHours", "24");
            builder.UseSetting("Geocoding:MinimumIntervalMilliseconds", "1000");
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
                    ["Geocoding:Enabled"] = geocodingHandler is null ? "false" : "true",
                    ["Geocoding:BaseUrl"] = "https://nominatim.test",
                    ["Geocoding:UserAgent"] = "GymLink.Tests/1.0",
                    ["Geocoding:TimeoutSeconds"] = "5",
                    ["Geocoding:CacheHours"] = "24",
                    ["Geocoding:MinimumIntervalMilliseconds"] = "1000",
                }));
            if (geocodingHandler is not null)
            {
                builder.ConfigureServices(services =>
                    services.AddHttpClient("Nominatim")
                        .ConfigurePrimaryHttpMessageHandler(() => geocodingHandler));
            }
        });

    private static GymLinkDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GymLinkDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new GymLinkDbContext(options, new TestTenantContext(null));
    }

    private sealed class NominatimHandler(string responseBody) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string UserAgent { get; private set; } = string.Empty;
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string ResponseBody { get; set; } = responseBody;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            RequestUri = request.RequestUri;
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(
                    ResponseBody,
                    System.Text.Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}
