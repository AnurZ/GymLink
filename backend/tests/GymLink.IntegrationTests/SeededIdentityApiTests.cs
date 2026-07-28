using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using GymLink.Application.Identity;
using GymLink.Domain.Common;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GymLink.IntegrationTests;

public sealed class SeededIdentityApiTests
{
    private const string Password = "Test123!";
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes";

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

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            var accounts = new[]
            {
                new ExpectedAccount("desktop", RoleNames.GymAdmin, "GymLink Sarajevo"),
                new ExpectedAccount("mobile", RoleNames.Member, null),
                new ExpectedAccount("centraladmin", RoleNames.CentralAdmin, null),
                new ExpectedAccount("gymadmin", RoleNames.GymAdmin, "GymLink Mostar"),
                new ExpectedAccount("trainer", RoleNames.Trainer, "GymLink Sarajevo"),
                new ExpectedAccount("member", RoleNames.Member, null),
                new ExpectedAccount("trainer2", RoleNames.Trainer, "GymLink Mostar"),
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
                new AuthenticationHeaderValue("Bearer", sessions["desktop"].AccessToken);
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.GetAsync("/api/tenant/gym")).StatusCode);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", sessions["mobile"].AccessToken);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await client.GetAsync("/api/tenant/gym")).StatusCode);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", sessions["centraladmin"].AccessToken);
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.GetAsync("/api/admin/users")).StatusCode);
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
            Assert.Contains("GymLink Sarajevo", names);
            Assert.Contains("GymLink Mostar", names);

            var original = sessions["member"];
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
            Assert.Equal(7, await verificationContext.UserProfiles.CountAsync());
            Assert.Equal(2, await verificationContext.Gyms.IgnoreQueryFilters().CountAsync());
            Assert.Equal(4, await verificationContext.UserGymAssignments.IgnoreQueryFilters().CountAsync());
            Assert.Equal(2, await verificationContext.TrainerProfiles.IgnoreQueryFilters().CountAsync());
            Assert.Equal(14, await verificationContext.GymWorkingHours.IgnoreQueryFilters().CountAsync());
            Assert.Equal(2, await verificationContext.MembershipPlans.IgnoreQueryFilters().CountAsync());
            Assert.Equal(2, await verificationContext.TrainerServiceOfferings.IgnoreQueryFilters().CountAsync());
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

    private sealed record ExpectedAccount(
        string Username,
        string Role,
        string? TenantName);
}
