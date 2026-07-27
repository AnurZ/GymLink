using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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

    [Theory]
    [InlineData("/api/auth/password-reset")]
    [InlineData("/api/auth/forgot-password")]
    [InlineData("/api/auth/reset-password")]
    public async Task Phase_three_exposes_no_password_reset_endpoint(string path)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(path, new { email = "member@gymlink.local" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__GymLink",
            TestSqlServer.ConnectionString("GymLinkApiTests"));
        Environment.SetEnvironmentVariable("Jwt__Issuer", "GymLink.Tests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "GymLink.Tests.Client");
        Environment.SetEnvironmentVariable(
            "Jwt__SigningKey",
            "integration-test-signing-key-at-least-32-bytes");
        Environment.SetEnvironmentVariable("Seed__Enabled", "false");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
        });
    }
}
