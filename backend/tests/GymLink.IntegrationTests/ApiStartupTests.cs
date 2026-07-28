using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace GymLink.IntegrationTests;

public sealed class ApiStartupTests
{
    [Fact]
    public async Task Development_api_exposes_health_and_swagger()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        var swagger = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        using var document = JsonDocument.Parse(await swagger.Content.ReadAsStringAsync());
        var requirement = document.RootElement.GetProperty("security")[0];
        Assert.True(requirement.TryGetProperty("Bearer", out _));
    }

    [Fact]
    public async Task Protected_catalog_writes_require_a_bearer_token()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/admin/reference-data/countries",
            new { code = "BIH", name = "Bosnia and Herzegovina" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Registration_rejects_role_injection()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username = "injected",
                email = "injected@example.test",
                displayName = "Injected User",
                password = "Test123!",
                role = "CentralAdmin",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Forgot_password_is_enumeration_safe()
    {
        var connectionString = TestSqlServer.ConnectionString(
            $"GymLink_Phase7_{Guid.NewGuid():N}");
        try
        {
            await using (var context = CreateContext(connectionString))
            {
                await context.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            var response = await client.PostAsJsonAsync(
                "/api/auth/forgot-password",
                new { email = "unknown@example.test" });

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        finally
        {
            await using var context = CreateContext(connectionString);
            await context.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Reset_password_validates_code_and_password()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new
            {
                email = "member@gymlink.local",
                code = "12",
                newPassword = "weak",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string? connectionString = null)
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__GymLink",
            connectionString ?? TestSqlServer.ConnectionString("GymLinkApiTests"));
        Environment.SetEnvironmentVariable("Jwt__Issuer", "GymLink.Tests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "GymLink.Tests.Client");
        Environment.SetEnvironmentVariable(
            "Jwt__SigningKey",
            "integration-test-signing-key-at-least-32-bytes");
        Environment.SetEnvironmentVariable(
            "PasswordReset__CodePepper",
            "integration-test-reset-pepper-at-least-32-bytes");
        Environment.SetEnvironmentVariable("Seed__Enabled", "false");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
        });
    }

    private static GymLinkDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GymLinkDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new GymLinkDbContext(options, new TestTenantContext(null));
    }
}
