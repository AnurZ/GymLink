using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymLink.Application.GymImages;
using GymLink.Application.Identity;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GymLink.IntegrationTests;

public sealed class Phase94GymImageGalleryApiTests
{
    private const string Password = "Test123!";
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes";
    private static readonly JsonSerializerOptions WebJson =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Gym_gallery_is_ordered_bounded_audited_tenant_scoped_and_cleaned_up()
    {
        var databaseName = $"GymLink_Phase94_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        var storageRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            $"GymLink_Phase94_Images_{Guid.NewGuid():N}"));
        try
        {
            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString, storageRoot);
            using var client = factory.CreateClient();
            var admin = await LoginAsync(client, "admin.respect");
            var otherAdmin = await LoginAsync(client, "admin.arena");
            var member = await LoginAsync(client, "mobile2");
            var trainer = await LoginAsync(client, "respecttrainer1");
            var centralAdmin = await LoginAsync(client, "centraladmin");

            Authorize(client, admin);
            var initial = await GetGalleryAsync(client);
            var external = Assert.Single(initial.Images);
            Assert.Null(external.ContentType);
            Assert.True(external.IsPrimary);

            var maximumJpeg = JpegBytes(5 * 1024 * 1024);
            var gallery = await UploadAsync(
                client,
                $"/api/tenant/gym/images/{external.Id}/content",
                maximumJpeg,
                "cover.jpg",
                "image/jpeg",
                external.ConcurrencyToken);
            var cover = Assert.Single(gallery.Images);
            Assert.Equal("image/jpeg", cover.ContentType);
            Assert.Single(Directory.GetFiles(storageRoot));

            client.DefaultRequestHeaders.Authorization = null;
            var stored = await client.GetAsync(cover.ImageUrl);
            stored.EnsureSuccessStatusCode();
            Assert.Equal("image/jpeg", stored.Content.Headers.ContentType?.MediaType);
            Assert.Equal(maximumJpeg.LongLength, stored.Content.Headers.ContentLength);

            Authorize(client, admin);
            var stale = await UploadResponseAsync(
                client,
                $"/api/tenant/gym/images/{cover.Id}/content",
                PngBytes(),
                "stale.png",
                "image/png",
                external.ConcurrencyToken);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("concurrency_conflict", await ProblemCodeAsync(stale));
            Assert.Single(Directory.GetFiles(storageRoot));

            var spoofed = await UploadResponseAsync(
                client,
                "/api/tenant/gym/images",
                JpegBytes(32),
                "spoofed.png",
                "image/png");
            Assert.Equal(HttpStatusCode.BadRequest, spoofed.StatusCode);
            Assert.Equal("invalid_gym_image", await ProblemCodeAsync(spoofed));

            var empty = await UploadResponseAsync(
                client,
                "/api/tenant/gym/images",
                [],
                "empty.jpg",
                "image/jpeg");
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
            Assert.Equal("invalid_gym_image", await ProblemCodeAsync(empty));

            var unsafeName = await UploadResponseAsync(
                client,
                "/api/tenant/gym/images",
                PngBytes(),
                "../escape.png",
                "image/png");
            Assert.Equal(HttpStatusCode.BadRequest, unsafeName.StatusCode);
            Assert.Equal("invalid_gym_image", await ProblemCodeAsync(unsafeName));

            var oversized = await UploadResponseAsync(
                client,
                "/api/tenant/gym/images",
                JpegBytes((5 * 1024 * 1024) + 1),
                "oversized.jpg",
                "image/jpeg");
            Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
            Assert.Equal("invalid_gym_image", await ProblemCodeAsync(oversized));

            gallery = await UploadAsync(
                client,
                "/api/tenant/gym/images",
                PngBytes(),
                "second.png",
                "image/png");
            gallery = await UploadAsync(
                client,
                "/api/tenant/gym/images",
                WebPBytes(),
                "third.webp",
                "image/webp");
            gallery = await UploadAsync(
                client,
                "/api/tenant/gym/images",
                JpegBytes(32),
                "fourth.jpeg",
                "image/jpeg");
            gallery = await UploadAsync(
                client,
                "/api/tenant/gym/images",
                PngBytes(),
                "fifth.png",
                "image/png");
            Assert.Equal(5, gallery.Images.Count);
            Assert.Equal(5, Directory.GetFiles(storageRoot).Length);

            var managedCover = gallery.Images[0];
            gallery = await UploadAsync(
                client,
                $"/api/tenant/gym/images/{managedCover.Id}/content",
                PngBytes(),
                "new-cover.png",
                "image/png",
                managedCover.ConcurrencyToken);
            Assert.Equal(5, Directory.GetFiles(storageRoot).Length);

            var sixth = await UploadResponseAsync(
                client,
                "/api/tenant/gym/images",
                PngBytes(),
                "sixth.png",
                "image/png");
            Assert.Equal(HttpStatusCode.Conflict, sixth.StatusCode);
            Assert.Equal("gym_image_limit_reached", await ProblemCodeAsync(sixth));

            var incompleteReorder = await client.PutAsJsonAsync(
                "/api/tenant/gym/images/order",
                new
                {
                    images = gallery.Images.Take(4).Select(x => new
                    {
                        imageId = x.Id,
                        concurrencyToken = x.ConcurrencyToken,
                    }),
                });
            Assert.Equal(HttpStatusCode.BadRequest, incompleteReorder.StatusCode);
            Assert.Equal("gym_image_order_invalid", await ProblemCodeAsync(incompleteReorder));

            var reversed = gallery.Images.Reverse().ToArray();
            var reorder = await client.PutAsJsonAsync(
                "/api/tenant/gym/images/order",
                new
                {
                    images = reversed.Select(x => new
                    {
                        imageId = x.Id,
                        concurrencyToken = x.ConcurrencyToken,
                    }),
                });
            reorder.EnsureSuccessStatusCode();
            gallery = await reorder.Content.ReadFromJsonAsync<GymImageGalleryDto>()
                ?? throw new InvalidOperationException("Reorder returned no gallery.");
            Assert.Equal(reversed.Select(x => x.Id), gallery.Images.Select(x => x.Id));
            Assert.True(gallery.Images[0].IsPrimary);
            Assert.All(gallery.Images.Skip(1), image => Assert.False(image.IsPrimary));
            Assert.Equal(Enumerable.Range(0, 5), gallery.Images.Select(x => x.SortOrder));

            var primary = gallery.Images[0];
            Authorize(client, otherAdmin);
            var crossTenant = await client.SendAsync(new HttpRequestMessage(
                HttpMethod.Delete,
                $"/api/tenant/gym/images/{primary.Id}")
            {
                Content = JsonContent.Create(new
                {
                    concurrencyToken = primary.ConcurrencyToken,
                }),
            });
            Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

            Authorize(client, member);
            var memberDenied = await UploadResponseAsync(
                client,
                "/api/tenant/gym/images",
                PngBytes(),
                "member.png",
                "image/png");
            Assert.Equal(HttpStatusCode.Forbidden, memberDenied.StatusCode);

            Authorize(client, trainer);
            var trainerDenied = await UploadResponseAsync(
                client,
                "/api/tenant/gym/images",
                PngBytes(),
                "trainer.png",
                "image/png");
            Assert.Equal(HttpStatusCode.Forbidden, trainerDenied.StatusCode);

            Authorize(client, centralAdmin);
            var centralAdminDenied = await UploadResponseAsync(
                client,
                "/api/tenant/gym/images",
                PngBytes(),
                "central-admin.png",
                "image/png");
            Assert.Equal(HttpStatusCode.Forbidden, centralAdminDenied.StatusCode);

            client.DefaultRequestHeaders.Authorization = null;
            var unauthenticated = await UploadResponseAsync(
                client,
                "/api/tenant/gym/images",
                PngBytes(),
                "unauthenticated.png",
                "image/png");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

            Authorize(client, admin);
            gallery = await RemoveAsync(client, primary);
            Assert.Equal(4, gallery.Images.Count);
            Assert.True(gallery.Images[0].IsPrimary);
            Assert.Equal(4, Directory.GetFiles(storageRoot).Length);

            while (gallery.Images.Count > 0)
            {
                gallery = await RemoveAsync(client, gallery.Images[0]);
            }

            Assert.Empty(Directory.GetFiles(storageRoot));

            await using var verification = CreateContext(connectionString);
            var gym = await verification.Gyms.IgnoreQueryFilters()
                .SingleAsync(x => x.Name == "Sportska Akademija Respect");
            var actions = await verification.SecurityAuditRecords
                .Where(x => x.TargetId == gym.Id && x.Action.StartsWith("gym_image."))
                .Select(x => x.Action)
                .ToListAsync();
            Assert.Contains("gym_image.replaced", actions);
            Assert.Equal(4, actions.Count(x => x == "gym_image.uploaded"));
            Assert.Equal(5, actions.Count(x => x == "gym_image.removed"));
            Assert.Contains("gym_image.reordered", actions);
            Assert.Contains("gym_image.primary_changed", actions);
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

    private static async Task<GymImageGalleryDto> GetGalleryAsync(HttpClient client)
    {
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/tenant/gym"));
        var gallery = document.RootElement.GetProperty("imageGallery");
        return JsonSerializer.Deserialize<GymImageGalleryDto>(
            gallery.GetRawText(),
            WebJson)
            ?? throw new InvalidOperationException("Gym response had no gallery.");
    }

    private static async Task<GymImageGalleryDto> RemoveAsync(
        HttpClient client,
        GymImageManagementDto image)
    {
        var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/tenant/gym/images/{image.Id}")
        {
            Content = JsonContent.Create(new
            {
                concurrencyToken = image.ConcurrencyToken,
            }),
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GymImageGalleryDto>()
            ?? throw new InvalidOperationException("Remove returned no gallery.");
    }

    private static async Task<GymImageGalleryDto> UploadAsync(
        HttpClient client,
        string path,
        byte[] bytes,
        string fileName,
        string contentType,
        string? concurrencyToken = null)
    {
        var response = await UploadResponseAsync(
            client,
            path,
            bytes,
            fileName,
            contentType,
            concurrencyToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GymImageGalleryDto>()
            ?? throw new InvalidOperationException("Upload returned no gallery.");
    }

    private static async Task<HttpResponseMessage> UploadResponseAsync(
        HttpClient client,
        string path,
        byte[] bytes,
        string fileName,
        string contentType,
        string? concurrencyToken = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        if (concurrencyToken is not null)
        {
            form.Add(new StringContent(concurrencyToken), "concurrencyToken");
        }

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
            builder.UseSetting(
                "FileStorage:RootPath",
                Path.Combine(storageRoot, "trainers"));
            builder.UseSetting("FileStorage:RequestPath", "/uploads/trainer-images");
            builder.UseSetting("FileStorage:GymRootPath", storageRoot);
            builder.UseSetting("FileStorage:GymRequestPath", "/uploads/gym-images");
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
                    ["FileStorage:RootPath"] = Path.Combine(storageRoot, "trainers"),
                    ["FileStorage:RequestPath"] = "/uploads/trainer-images",
                    ["FileStorage:GymRootPath"] = storageRoot,
                    ["FileStorage:GymRequestPath"] = "/uploads/gym-images",
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
