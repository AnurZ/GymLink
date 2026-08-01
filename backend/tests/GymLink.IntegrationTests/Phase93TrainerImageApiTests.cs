using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymLink.Application.Catalog;
using GymLink.Application.Identity;
using GymLink.Application.TrainerImages;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GymLink.IntegrationTests;

public sealed class Phase93TrainerImageApiTests
{
    private const string Password = "Test123!";
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes";

    [Fact]
    public async Task Trainer_images_are_validated_audited_tenant_scoped_and_cleaned_up()
    {
        var databaseName = $"GymLink_Phase93_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        var storageRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            $"GymLink_Phase93_Images_{Guid.NewGuid():N}"));
        try
        {
            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString, storageRoot);
            using var client = factory.CreateClient();
            var trainerSession = await LoginAsync(client, "respecttrainer1");
            var adminSession = await LoginAsync(client, "admin.respect");
            var otherAdminSession = await LoginAsync(client, "admin.arena");
            var memberSession = await LoginAsync(client, "mobile2");
            var otherTrainerSession = await LoginAsync(client, "arenatrainer1");
            var centralAdminSession = await LoginAsync(client, "centraladmin");

            Authorize(client, trainerSession);
            var profile = await client.GetFromJsonAsync<UserProfileDto>("/api/profile");
            Assert.NotNull(profile?.TrainerImage);
            var trainerId = profile.TrainerProfileId!.Value;
            var initialToken = profile.TrainerImage.ConcurrencyToken;
            var originalSeedFileName = Path.GetFileName(profile.TrainerImage.ImageUrl);
            var untouchedSeedFiles = Directory.GetFiles(storageRoot)
                .Where(x => !string.Equals(
                    Path.GetFileName(x),
                    originalSeedFileName,
                    StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(11, untouchedSeedFiles.Count);

            var maximumJpeg = JpegBytes(5 * 1024 * 1024);
            var uploaded = await UploadAsync(
                client,
                "/api/profile/trainer-image",
                maximumJpeg,
                "trainer.jpg",
                "image/jpeg",
                initialToken);
            Assert.StartsWith("/uploads/trainer-images/", uploaded.ImageUrl);
            Assert.Equal("image/jpeg", uploaded.ContentType);
            Assert.Equal(maximumJpeg.LongLength, uploaded.FileSizeBytes);
            Assert.Equal(12, Directory.GetFiles(storageRoot).Length);
            Assert.All(untouchedSeedFiles, path => Assert.True(File.Exists(path)));

            client.DefaultRequestHeaders.Authorization = null;
            var storedImage = await client.GetAsync(uploaded.ImageUrl);
            storedImage.EnsureSuccessStatusCode();
            Assert.Equal("image/jpeg", storedImage.Content.Headers.ContentType?.MediaType);
            Assert.Equal(maximumJpeg.LongLength, storedImage.Content.Headers.ContentLength);

            var gymId = await FindGymAsync(client, "Sportska Akademija Respect");
            var publicTrainers = await client.GetFromJsonAsync<IReadOnlyList<TrainerDto>>(
                $"/api/gyms/{gymId}/trainers");
            var publicTrainer = Assert.Single(
                publicTrainers!,
                x => x.DisplayName == "Emir Hadžić");
            Assert.Equal(uploaded.ImageUrl, publicTrainer.ImageUrl);
            Assert.Null(publicTrainer.ManagementImage);

            Authorize(client, trainerSession);
            var stale = await UploadResponseAsync(
                client,
                "/api/profile/trainer-image",
                JpegBytes(32),
                "stale.jpg",
                "image/jpeg",
                initialToken);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("concurrency_conflict", await ProblemCodeAsync(stale));
            Assert.Equal(12, Directory.GetFiles(storageRoot).Length);
            Assert.All(untouchedSeedFiles, path => Assert.True(File.Exists(path)));

            var spoofed = await UploadResponseAsync(
                client,
                "/api/profile/trainer-image",
                JpegBytes(32),
                "spoofed.png",
                "image/png",
                uploaded.ConcurrencyToken);
            Assert.Equal(HttpStatusCode.BadRequest, spoofed.StatusCode);
            Assert.Equal("invalid_trainer_image", await ProblemCodeAsync(spoofed));

            var unsafeName = await UploadResponseAsync(
                client,
                "/api/profile/trainer-image",
                JpegBytes(32),
                "../escape.jpg",
                "image/jpeg",
                uploaded.ConcurrencyToken);
            Assert.Equal(HttpStatusCode.BadRequest, unsafeName.StatusCode);
            Assert.Equal("invalid_trainer_image", await ProblemCodeAsync(unsafeName));

            var empty = await UploadResponseAsync(
                client,
                "/api/profile/trainer-image",
                [],
                "empty.jpg",
                "image/jpeg",
                uploaded.ConcurrencyToken);
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
            Assert.Equal("invalid_trainer_image", await ProblemCodeAsync(empty));

            var oversized = await UploadResponseAsync(
                client,
                "/api/profile/trainer-image",
                JpegBytes((5 * 1024 * 1024) + 1),
                "oversized.jpg",
                "image/jpeg",
                uploaded.ConcurrencyToken);
            Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);

            var webp = await UploadAsync(
                client,
                "/api/profile/trainer-image",
                WebPBytes(),
                "replacement.webp",
                "image/webp",
                uploaded.ConcurrencyToken);
            Assert.Equal("image/webp", webp.ContentType);
            Assert.Equal(12, Directory.GetFiles(storageRoot).Length);
            Assert.All(untouchedSeedFiles, path => Assert.True(File.Exists(path)));

            Authorize(client, otherAdminSession);
            var crossTenant = await UploadResponseAsync(
                client,
                $"/api/tenant/trainers/{trainerId}/image",
                PngBytes(),
                "other.png",
                "image/png",
                webp.ConcurrencyToken);
            Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

            Authorize(client, memberSession);
            var memberDenied = await UploadResponseAsync(
                client,
                $"/api/tenant/trainers/{trainerId}/image",
                PngBytes(),
                "member.png",
                "image/png",
                webp.ConcurrencyToken);
            Assert.Equal(HttpStatusCode.Forbidden, memberDenied.StatusCode);

            Authorize(client, otherTrainerSession);
            var trainerDenied = await UploadResponseAsync(
                client,
                $"/api/tenant/trainers/{trainerId}/image",
                PngBytes(),
                "trainer.png",
                "image/png",
                webp.ConcurrencyToken);
            Assert.Equal(HttpStatusCode.Forbidden, trainerDenied.StatusCode);

            Authorize(client, centralAdminSession);
            var centralAdminDenied = await UploadResponseAsync(
                client,
                $"/api/tenant/trainers/{trainerId}/image",
                PngBytes(),
                "central.png",
                "image/png",
                webp.ConcurrencyToken);
            Assert.Equal(HttpStatusCode.Forbidden, centralAdminDenied.StatusCode);

            Authorize(client, adminSession);
            var replaced = await UploadAsync(
                client,
                $"/api/tenant/trainers/{trainerId}/image",
                PngBytes(),
                "replacement.png",
                "image/png",
                webp.ConcurrencyToken);
            Assert.Equal("image/png", replaced.ContentType);
            Assert.Equal(12, Directory.GetFiles(storageRoot).Length);
            Assert.All(untouchedSeedFiles, path => Assert.True(File.Exists(path)));

            var removedResponse = await client.SendAsync(new HttpRequestMessage(
                HttpMethod.Delete,
                $"/api/tenant/trainers/{trainerId}/image")
            {
                Content = JsonContent.Create(new
                {
                    concurrencyToken = replaced.ConcurrencyToken,
                }),
            });
            removedResponse.EnsureSuccessStatusCode();
            var removed = await removedResponse.Content.ReadFromJsonAsync<TrainerImageDto>();
            Assert.Null(removed?.ImageUrl);
            Assert.Equal(
                untouchedSeedFiles.Order(StringComparer.OrdinalIgnoreCase),
                Directory.GetFiles(storageRoot).Order(StringComparer.OrdinalIgnoreCase));

            await using var verification = CreateContext(connectionString);
            var audits = await verification.SecurityAuditRecords
                .Where(x => x.TargetId == trainerId && x.Action.StartsWith("trainer_image."))
                .OrderBy(x => x.OccurredAtUtc)
                .ToListAsync();
            Assert.Equal(
                [
                    "trainer_image.replaced",
                    "trainer_image.replaced",
                    "trainer_image.replaced",
                    "trainer_image.removed",
                ],
                audits.Select(x => x.Action));
            Assert.All(audits, audit => Assert.NotNull(audit.TargetTenantId));
        }
        finally
        {
            await using (var cleanup = CreateContext(connectionString))
            {
                await cleanup.Database.EnsureDeletedAsync();
            }
            var tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (Directory.Exists(storageRoot) &&
                storageRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    private static async Task<TrainerImageDto> UploadAsync(
        HttpClient client,
        string path,
        byte[] bytes,
        string fileName,
        string contentType,
        string concurrencyToken)
    {
        var response = await UploadResponseAsync(
            client,
            path,
            bytes,
            fileName,
            contentType,
            concurrencyToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TrainerImageDto>()
            ?? throw new InvalidOperationException("Upload returned no image metadata.");
    }

    private static async Task<HttpResponseMessage> UploadResponseAsync(
        HttpClient client,
        string path,
        byte[] bytes,
        string fileName,
        string contentType,
        string concurrencyToken)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        form.Add(new StringContent(concurrencyToken), "concurrencyToken");
        return await client.PostAsync(path, form);
    }

    private static byte[] JpegBytes(int length)
    {
        var bytes = new byte[length];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        return bytes;
    }

    private static byte[] PngBytes() =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private static byte[] WebPBytes() =>
        [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50];

    private static async Task<Guid> FindGymAsync(HttpClient client, string name)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var response = JsonDocument.Parse(
            await client.GetStringAsync($"/api/gyms?query={Uri.EscapeDataString(name)}"));
        return response.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();
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

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        string storageRoot) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:GymLink", connectionString);
            builder.UseSetting("Jwt:Issuer", "GymLink.Tests");
            builder.UseSetting("Jwt:Audience", "GymLink.Tests.Client");
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting(
                "PasswordReset:CodePepper",
                "integration-test-reset-pepper-at-least-32-bytes");
            builder.UseSetting("RabbitMq:Enabled", "false");
            builder.UseSetting("Seed:Enabled", "true");
            builder.UseSetting("Seed:DefaultPassword", Password);
            builder.UseSetting("FileStorage:RootPath", storageRoot);
            builder.UseSetting("FileStorage:RequestPath", "/uploads/trainer-images");
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
                    ["RabbitMq:Enabled"] = "false",
                    ["Seed:Enabled"] = "true",
                    ["Seed:DefaultPassword"] = Password,
                    ["FileStorage:RootPath"] = storageRoot,
                    ["FileStorage:RequestPath"] = "/uploads/trainer-images",
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
