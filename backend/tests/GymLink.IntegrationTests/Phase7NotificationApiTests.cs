using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Application.Messaging;
using GymLink.Domain.Engagement;
using GymLink.Infrastructure.Messaging;
using GymLink.Infrastructure.Persistence;
using GymLink.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GymLink.IntegrationTests;

public sealed class Phase7NotificationApiTests
{
    private const string OriginalPassword = "Test123!";
    private const string NewPassword = "Reset123!";
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes";
    private const string CodePepper = "integration-test-reset-pepper-at-least-32-bytes";

    [Fact]
    public async Task Reset_revokes_sessions_and_notifications_are_user_scoped()
    {
        var connectionString = TestSqlServer.ConnectionString(
            $"GymLink_Phase7_{Guid.NewGuid():N}");
        try
        {
            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            var member = await LoginAsync(client, "member", OriginalPassword);
            var mobile = await LoginAsync(client, "mobile", OriginalPassword);
            Guid memberId;
            Guid mobileId;
            await using (var seeded = CreateContext(connectionString))
            {
                memberId = await seeded.Set<GymLinkIdentityUser>()
                    .Where(x => x.UserName == "member")
                    .Select(x => x.Id)
                    .SingleAsync();
                mobileId = await seeded.Set<GymLinkIdentityUser>()
                    .Where(x => x.UserName == "mobile")
                    .Select(x => x.Id)
                    .SingleAsync();
                Assert.True(await seeded.UserProfiles.AnyAsync(
                    x => x.Id == memberId && x.IsActive));
            }

            var forgot = await client.PostAsJsonAsync(
                "/api/auth/forgot-password",
                new { email = member.User.Email });
            Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);

            Guid challengeId;
            await using (var verification = CreateContext(connectionString))
            {
                var challenge = await verification.PasswordResetChallenges
                    .SingleAsync(x => x.UserId == memberId);
                challengeId = challenge.Id;
                Assert.Single(
                    await verification.OutboxMessages
                        .Where(x => x.MessageType == "password-reset.requested.v1")
                        .ToListAsync());
            }

            var codeService = new PasswordResetCodeService(
                Options.Create(new PasswordResetOptions { CodePepper = CodePepper }));
            var reset = await client.PostAsJsonAsync(
                "/api/auth/reset-password",
                new
                {
                    email = member.User.Email,
                    code = codeService.DeriveCode(challengeId),
                    newPassword = NewPassword,
                });
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new { identifier = "member", password = OriginalPassword })).StatusCode);
            member = await LoginAsync(client, "member", NewPassword);

            Guid memberNotificationId;
            Guid mobileNotificationId;
            await using (var setup = CreateContext(connectionString))
            {
                var challenge = await setup.PasswordResetChallenges
                    .SingleAsync(x => x.Id == challengeId);
                Assert.NotNull(challenge.ConsumedAtUtc);
                Assert.DoesNotContain(
                    await setup.RefreshTokenSessions
                        .Where(x => x.UserId == memberId)
                        .ToListAsync(),
                    x => x.RevokedAtUtc is null && x.CreatedAtUtc < challenge.ConsumedAtUtc);

                var memberNotification = NewNotification(memberId, "member");
                var mobileNotification = NewNotification(mobileId, "mobile");
                setup.Notifications.AddRange(memberNotification, mobileNotification);
                await setup.SaveChangesAsync();
                memberNotificationId = memberNotification.Id;
                mobileNotificationId = mobileNotification.Id;
            }

            Authorize(client, member.AccessToken);
            var page = await client.GetFromJsonAsync<PagedResult<NotificationDto>>(
                "/api/me/notifications?page=1&pageSize=20");
            var own = Assert.Single(page!.Items);
            Assert.Equal(memberNotificationId, own.Id);
            Assert.DoesNotContain(page.Items, x => x.Id == mobileNotificationId);

            var crossUser = await client.PostAsJsonAsync(
                $"/api/me/notifications/{mobileNotificationId}/read",
                new { concurrencyToken = own.ConcurrencyToken });
            Assert.Equal(HttpStatusCode.NotFound, crossUser.StatusCode);

            var marked = await client.PostAsJsonAsync(
                $"/api/me/notifications/{memberNotificationId}/read",
                new { concurrencyToken = own.ConcurrencyToken });
            Assert.Equal(HttpStatusCode.OK, marked.StatusCode);
            var unread = await client.GetFromJsonAsync<UnreadNotificationCountDto>(
                "/api/me/notifications/unread-count");
            Assert.Equal(0, unread!.Count);
            Assert.Equal(
                HttpStatusCode.NoContent,
                (await client.PostAsync("/api/me/notifications/read-all", null)).StatusCode);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static Notification NewNotification(Guid recipient, string suffix) =>
        new()
        {
            RecipientUserId = recipient,
            Type = "test.notification",
            Title = $"Notification {suffix}",
            Text = "Safe notification body.",
            CorrelationId = Guid.NewGuid().ToString("N"),
            SourceMessageId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
        };

    private static async Task<AuthSessionDto> LoginAsync(
        HttpClient client,
        string identifier,
        string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { identifier, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSessionDto>())!;
    }

    private static void Authorize(HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:GymLink", connectionString);
            builder.UseSetting("Jwt:Issuer", "GymLink.Tests");
            builder.UseSetting("Jwt:Audience", "GymLink.Tests.Client");
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("PasswordReset:CodePepper", CodePepper);
            builder.UseSetting("RabbitMq:Enabled", "false");
            builder.UseSetting("Seed:Enabled", "true");
            builder.UseSetting("Seed:DefaultPassword", OriginalPassword);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:GymLink"] = connectionString,
                    ["Jwt:Issuer"] = "GymLink.Tests",
                    ["Jwt:Audience"] = "GymLink.Tests.Client",
                    ["Jwt:SigningKey"] = SigningKey,
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Jwt:RefreshTokenDays"] = "30",
                    ["PasswordReset:CodePepper"] = CodePepper,
                    ["RabbitMq:Enabled"] = "false",
                    ["Seed:Enabled"] = "true",
                    ["Seed:DefaultPassword"] = OriginalPassword,
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
