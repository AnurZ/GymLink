using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymLink.Application.Administration;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Domain.Catalog;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GymLink.IntegrationTests;

public sealed class AdminGymApiTests
{
    private const string Password = "Test123!";
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes";

    [Fact]
    public async Task CentralAdmin_creates_private_gym_and_ownerless_activation_is_rejected()
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

            using var lookups = JsonDocument.Parse(
                await client.GetStringAsync("/api/reference-data/lookups"));
            var cityId = lookups.RootElement.GetProperty("cities")[0].GetProperty("id").GetGuid();

            Authorize(client, member);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await client.PostAsJsonAsync(
                    "/api/admin/gyms",
                    CreateRequest(cityId))).StatusCode);

            Authorize(client, centralAdmin);
            var create = await client.PostAsJsonAsync("/api/admin/gyms", CreateRequest(cityId));
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var gym = await create.Content.ReadFromJsonAsync<AdminGymDto>();
            Assert.NotNull(gym);
            Assert.Equal(TenantStatus.PendingActivation, gym.Status);
            Assert.False(gym.IsPubliclyVisible);
            Assert.Equal(0, gym.ActiveGymAdminCount);

            var search = await client.GetFromJsonAsync<PagedResult<AdminGymDto>>(
                "/api/admin/gyms?query=Stabilization&page=1&pageSize=10");
            var searchedGym = Assert.Single(search!.Items);
            Assert.Equal(gym.Id, searchedGym.Id);
            Assert.Equal(gym.TenantId, searchedGym.TenantId);

            var activate = await client.PostAsync(
                $"/api/admin/tenants/{gym.TenantId}/activate",
                content: null);
            Assert.Equal(HttpStatusCode.Conflict, activate.StatusCode);
            Assert.Equal("tenant_admin_required", await ProblemCodeAsync(activate));

            var assignment = await client.PostAsJsonAsync(
                "/api/admin/users/roles/assign",
                new
                {
                    identifier = member.User.Email,
                    role = RoleNames.GymAdmin,
                    tenantId = gym.TenantId,
                    reason = "Assigned as the first gym administrator.",
                });
            assignment.EnsureSuccessStatusCode();

            var incomplete = await client.PostAsync(
                $"/api/admin/tenants/{gym.TenantId}/activate",
                content: null);
            Assert.Equal(HttpStatusCode.Conflict, incomplete.StatusCode);
            Assert.Equal("tenant_catalog_incomplete", await ProblemCodeAsync(incomplete));

            await using var verification = CreateContext(connectionString);
            var persistedGym = await verification.Gyms.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == gym.Id);
            Assert.False(persistedGym.IsPubliclyVisible);
            Assert.Equal(
                TenantStatus.PendingActivation,
                (await verification.Tenants.SingleAsync(x => x.Id == gym.TenantId)).Status);
            Assert.True(await verification.SecurityAuditRecords.AnyAsync(
                x => x.TargetTenantId == gym.TenantId &&
                     x.TargetId == gym.Id &&
                     x.Action == "gym.created" &&
                     x.TargetType == nameof(Gym)));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static object CreateRequest(Guid cityId) => new
    {
        name = $"Stabilization Gym {Guid.NewGuid():N}",
        description = "A complete gym description used by the integration test.",
        address = "Testna 42",
        cityId,
        latitude = 43.8563m,
        longitude = 18.4131m,
        phoneNumber = "+387 33 555 555",
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
                }));
        });

    private static GymLinkDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GymLinkDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new GymLinkDbContext(options, new TestTenantContext(null));
    }
}
