using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Application.Messaging;
using GymLink.Application.Reservations;
using GymLink.Domain.Common;
using GymLink.Domain.Engagement;
using GymLink.Domain.Enums;
using GymLink.Domain.Memberships;
using GymLink.Domain.Reservations;
using GymLink.Domain.Tenancy;
using GymLink.Infrastructure.Identity;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GymLink.IntegrationTests;

public sealed class Phase9ChatApiTests
{
    private const string Password = "Test123!";
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Chat_is_pair_scoped_idempotent_realtime_and_read_only_after_ineligibility()
    {
        var connectionString = TestSqlServer.ConnectionString(
            $"GymLink_Phase9_{Guid.NewGuid():N}");
        try
        {
            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            var member = await LoginAsync(client, "member");
            var trainer = await LoginAsync(client, "trainer");
            var nonParticipant = await LoginAsync(client, "mobile");
            var admin = await LoginAsync(client, "desktop");
            var reservationId = await SeedReservationAsync(
                connectionString,
                member.User.Id,
                trainer.User.Id);

            await using (var anonymousHub = CreateHub(factory, null))
            {
                await Assert.ThrowsAsync<HttpRequestException>(
                    () => anonymousHub.StartAsync());
            }

            Authorize(client, member);
            var openedResponse = await client.PostAsJsonAsync(
                "/api/me/conversations",
                new { reservationId });
            openedResponse.EnsureSuccessStatusCode();
            var opened = await openedResponse.Content.ReadFromJsonAsync<ConversationDto>();
            Assert.NotNull(opened);
            Assert.True(opened.CanSend);
            Assert.Equal(trainer.User.Id, opened.CounterpartUserId);
            var loaded = await client.GetFromJsonAsync<ConversationDto>(
                $"/api/me/conversations/{opened.Id}");
            Assert.Equal(opened.Id, loaded!.Id);

            var reopened = await client.PostAsJsonAsync(
                "/api/me/conversations",
                new { reservationId });
            reopened.EnsureSuccessStatusCode();
            Assert.Equal(
                opened.Id,
                (await reopened.Content.ReadFromJsonAsync<ConversationDto>())!.Id);

            Authorize(client, nonParticipant);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.PostAsJsonAsync(
                    "/api/me/conversations",
                    new { reservationId })).StatusCode);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync(
                    $"/api/me/conversations/{opened.Id}")).StatusCode);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync(
                    $"/api/me/conversations/{opened.Id}/messages?take=20")).StatusCode);

            var clientMessageId = Guid.NewGuid();
            Authorize(client, member);
            var send = await client.PostAsJsonAsync(
                $"/api/me/conversations/{opened.Id}/messages",
                new { clientMessageId, text = "  Pozdrav treneru!  " });
            send.EnsureSuccessStatusCode();
            var sent = await send.Content.ReadFromJsonAsync<ChatMessageDto>();
            Assert.NotNull(sent);
            Assert.Equal("Pozdrav treneru!", sent.Text);
            var duplicate = await client.PostAsJsonAsync(
                $"/api/me/conversations/{opened.Id}/messages",
                new { clientMessageId, text = "Pozdrav treneru!" });
            duplicate.EnsureSuccessStatusCode();
            Assert.Equal(
                sent.Id,
                (await duplicate.Content.ReadFromJsonAsync<ChatMessageDto>())!.Id);

            await using (var notificationContext = CreateContext(connectionString))
            {
                var tenantId = await notificationContext.Conversations
                    .IgnoreQueryFilters()
                    .Where(x => x.Id == opened.Id)
                    .Select(x => x.TenantId)
                    .SingleAsync();
                notificationContext.Notifications.Add(new Notification
                {
                    RecipientUserId = trainer.User.Id,
                    TenantId = tenantId,
                    Type = "chat",
                    Title = "Nova poruka",
                    Text = "Imate novu poruku.",
                    TargetType = "conversation",
                    TargetId = opened.Id,
                    CreatedAtUtc = DateTime.UtcNow,
                });
                await notificationContext.SaveChangesAsync();
            }

            Authorize(client, trainer);
            var conversations = await client.GetFromJsonAsync<PagedResult<ConversationDto>>(
                "/api/me/conversations?page=1&pageSize=20");
            var trainerConversation = Assert.Single(conversations!.Items);
            Assert.Equal(1, trainerConversation.UnreadCount);
            Assert.Equal(member.User.Id, trainerConversation.CounterpartUserId);
            var history = await client.GetFromJsonAsync<MessageHistoryDto>(
                $"/api/me/conversations/{opened.Id}/messages?take=20");
            Assert.Equal(sent.Id, Assert.Single(history!.Items).Id);

            var read = await client.PostAsync(
                $"/api/me/conversations/{opened.Id}/read",
                null);
            read.EnsureSuccessStatusCode();
            Assert.Equal(
                1,
                (await read.Content.ReadFromJsonAsync<ConversationReadDto>())!.MarkedReadCount);
            await using (var readVerification = CreateContext(connectionString))
            {
                Assert.NotNull(
                    (await readVerification.Notifications
                        .SingleAsync(x =>
                            x.RecipientUserId == trainer.User.Id &&
                            x.TargetId == opened.Id))
                    .ReadAtUtc);
            }
            conversations = await client.GetFromJsonAsync<PagedResult<ConversationDto>>(
                "/api/me/conversations?page=1&pageSize=20");
            Assert.Equal(0, Assert.Single(conversations!.Items).UnreadCount);

            await using var hub = CreateHub(factory, trainer.AccessToken);
            var received = new TaskCompletionSource<ChatMessageDto>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            hub.On<JsonElement>(
                "message:new",
                payload =>
                {
                    var conversationId = payload.GetProperty("conversationId").GetGuid();
                    if (conversationId == opened.Id)
                    {
                        var message = payload.GetProperty("message")
                            .Deserialize<ChatMessageDto>(JsonOptions);
                        if (message is not null)
                        {
                            received.TrySetResult(message);
                        }
                    }
                });
            await hub.StartAsync();
            await hub.InvokeAsync("conversation:join", opened.Id);

            Authorize(client, member);
            var realtimeMessageId = Guid.NewGuid();
            var realtimeSend = await client.PostAsJsonAsync(
                $"/api/me/conversations/{opened.Id}/messages",
                new { clientMessageId = realtimeMessageId, text = "Vidimo se uskoro." });
            realtimeSend.EnsureSuccessStatusCode();
            var delivered = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(realtimeMessageId, delivered.ClientMessageId);

            var availableConversation = new TaskCompletionSource<Guid>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            hub.On<JsonElement>(
                "conversation:available",
                payload =>
                    availableConversation.TrySetResult(
                        payload.GetProperty("conversationId").GetGuid()));
            var pendingReservationId = await SeedReservationAsync(
                connectionString,
                nonParticipant.User.Id,
                trainer.User.Id,
                confirmed: false,
                startInDays: 3);
            Authorize(client, admin);
            var pendingReservation = await client.GetFromJsonAsync<ReservationDto>(
                $"/api/tenant/reservations/{pendingReservationId}");
            Assert.NotNull(pendingReservation);
            var confirmation = await client.PostAsJsonAsync(
                $"/api/tenant/reservations/{pendingReservationId}/confirm",
                new { concurrencyToken = pendingReservation.ConcurrencyToken });
            confirmation.EnsureSuccessStatusCode();
            var availableId = await availableConversation.Task.WaitAsync(
                TimeSpan.FromSeconds(10));
            Authorize(client, nonParticipant);
            var automaticallyAvailable =
                await client.GetFromJsonAsync<ConversationDto>(
                    $"/api/me/conversations/{availableId}");
            Assert.Equal(
                pendingReservationId,
                automaticallyAvailable!.OriginatingReservationId);

            await using (var tieContext = CreateContext(connectionString))
            {
                var tiedAt = DateTime.UtcNow.AddMinutes(-1);
                await tieContext.Messages
                    .IgnoreQueryFilters()
                    .Where(x => x.ConversationId == opened.Id)
                    .ExecuteUpdateAsync(setters =>
                        setters.SetProperty(x => x.SentAtUtc, tiedAt));
            }

            Authorize(client, trainer);
            var firstCursorPage = await client.GetFromJsonAsync<MessageHistoryDto>(
                $"/api/me/conversations/{opened.Id}/messages?take=1");
            Assert.NotNull(firstCursorPage);
            Assert.True(firstCursorPage.HasMore);
            var firstCursorMessage = Assert.Single(firstCursorPage.Items);
            var cursorTimestamp = Uri.EscapeDataString(
                firstCursorPage.NextBeforeSentAtUtc!.Value.ToString("O"));
            var secondCursorPage = await client.GetFromJsonAsync<MessageHistoryDto>(
                $"/api/me/conversations/{opened.Id}/messages?take=1" +
                $"&beforeSentAtUtc={cursorTimestamp}" +
                $"&beforeId={firstCursorPage.NextBeforeId}");
            var secondCursorMessage = Assert.Single(secondCursorPage!.Items);
            Assert.NotEqual(firstCursorMessage.Id, secondCursorMessage.Id);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await client.GetAsync(
                    $"/api/me/conversations/{opened.Id}/messages?take=101")).StatusCode);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await client.GetAsync(
                    $"/api/me/conversations/{opened.Id}/messages" +
                    $"?take=1&beforeId={firstCursorMessage.Id}")).StatusCode);

            await using var unauthorizedHub = CreateHub(factory, nonParticipant.AccessToken);
            await unauthorizedHub.StartAsync();
            await Assert.ThrowsAsync<HubException>(() =>
                unauthorizedHub.InvokeAsync("conversation:join", opened.Id));
            await Assert.ThrowsAsync<HubException>(() =>
                unauthorizedHub.InvokeAsync(
                    "message:send",
                    opened.Id,
                    Guid.NewGuid(),
                    "Forged message."));

            var revokedReadDelivery = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            hub.On<JsonElement>(
                "conversation:read",
                payload =>
                {
                    if (payload.GetProperty("conversationId").GetGuid() == opened.Id)
                    {
                        revokedReadDelivery.TrySetResult();
                    }
                });

            await using (var deactivate = CreateContext(connectionString))
            {
                var profile = await deactivate.TrainerProfiles
                    .IgnoreQueryFilters()
                    .SingleAsync(x => x.UserId == trainer.User.Id);
                profile.IsActive = false;
                await deactivate.SaveChangesAsync();
            }

            Authorize(client, member);
            var postRevocationRead = await client.PostAsync(
                $"/api/me/conversations/{opened.Id}/read",
                null);
            postRevocationRead.EnsureSuccessStatusCode();
            Assert.False(revokedReadDelivery.Task.IsCompleted);
            await Assert.ThrowsAsync<HubException>(() =>
                hub.InvokeAsync("conversation:read", opened.Id));

            var rejected = await client.PostAsJsonAsync(
                $"/api/me/conversations/{opened.Id}/messages",
                new { clientMessageId = Guid.NewGuid(), text = "Ova poruka ne prolazi." });
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            Assert.Equal("conversation_read_only", await ProblemCodeAsync(rejected));
            var memberConversations =
                await client.GetFromJsonAsync<PagedResult<ConversationDto>>(
                    "/api/me/conversations?page=1&pageSize=20");
            Assert.False(Assert.Single(memberConversations!.Items).CanSend);

            await using var verification = CreateContext(connectionString);
            Assert.Equal(
                2,
                await verification.Messages.IgnoreQueryFilters()
                    .CountAsync(x => x.ConversationId == opened.Id));
            Assert.Equal(
                2,
                await verification.OutboxMessages
                    .CountAsync(x =>
                        x.MessageType == "notification.requested.v1" &&
                        x.Payload.Contains("\"category\":\"chat\"")));
            Assert.Single(
                await verification.Conversations.IgnoreQueryFilters()
                    .Where(x =>
                        x.MemberUserId == member.User.Id &&
                        x.TrainerUserId == trainer.User.Id)
                    .ToListAsync());
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Concurrent_confirmations_create_one_pair_conversation()
    {
        var connectionString = TestSqlServer.ConnectionString(
            $"GymLink_Phase9_Concurrent_{Guid.NewGuid():N}");
        try
        {
            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var firstClient = factory.CreateClient();
            using var secondClient = factory.CreateClient();
            var trainer = await LoginAsync(firstClient, "trainer");
            var member = await RegisterAsync(firstClient);
            var firstReservationId = await SeedReservationAsync(
                connectionString,
                member.User.Id,
                trainer.User.Id,
                confirmed: false,
                startInDays: 4);
            var secondReservationId = await SeedReservationAsync(
                connectionString,
                member.User.Id,
                trainer.User.Id,
                confirmed: false,
                startInDays: 5);
            var firstAdmin = await LoginAsync(firstClient, "desktop");
            var secondAdmin = await LoginAsync(secondClient, "desktop");
            Authorize(firstClient, firstAdmin);
            Authorize(secondClient, secondAdmin);
            var firstReservation = await firstClient
                .GetFromJsonAsync<ReservationDto>(
                    $"/api/tenant/reservations/{firstReservationId}");
            var secondReservation = await secondClient
                .GetFromJsonAsync<ReservationDto>(
                    $"/api/tenant/reservations/{secondReservationId}");

            var confirmations = await Task.WhenAll(
                firstClient.PostAsJsonAsync(
                    $"/api/tenant/reservations/{firstReservationId}/confirm",
                    new { concurrencyToken = firstReservation!.ConcurrencyToken }),
                secondClient.PostAsJsonAsync(
                    $"/api/tenant/reservations/{secondReservationId}/confirm",
                    new { concurrencyToken = secondReservation!.ConcurrencyToken }));
            Assert.All(confirmations, response => response.EnsureSuccessStatusCode());

            await using var verification = CreateContext(connectionString);
            var conversation = Assert.Single(
                await verification.Conversations
                    .IgnoreQueryFilters()
                    .Where(x =>
                        x.MemberUserId == member.User.Id &&
                        x.TrainerUserId == trainer.User.Id)
                    .ToListAsync());
            Assert.True(
                conversation.ReservationId == firstReservationId ||
                conversation.ReservationId == secondReservationId);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<Guid> SeedReservationAsync(
        string connectionString,
        Guid memberUserId,
        Guid trainerUserId,
        bool confirmed = true,
        int startInDays = 2)
    {
        await using var context = CreateContext(connectionString);
        var trainer = await context.TrainerProfiles.IgnoreQueryFilters()
            .SingleAsync(x => x.UserId == trainerUserId);
        var offering = await context.TrainerServiceOfferings.IgnoreQueryFilters()
            .FirstAsync(x => x.TrainerProfileId == trainer.Id);
        var gym = await context.Gyms.IgnoreQueryFilters()
            .SingleAsync(x => x.TenantId == trainer.TenantId);
        var plan = await context.MembershipPlans.IgnoreQueryFilters()
            .FirstAsync(x => x.GymId == gym.Id);
        var now = DateTime.UtcNow;
        var membership = await context.Memberships
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x =>
                x.TenantId == trainer.TenantId &&
                x.MemberUserId == memberUserId &&
                x.GymId == gym.Id);
        if (membership is null)
        {
            var membershipRequest = new MembershipRequest
            {
                TenantId = trainer.TenantId,
                MemberUserId = memberUserId,
                GymId = gym.Id,
                MembershipPlanId = plan.Id,
                RequestedAtUtc = now,
                CreatedAtUtc = now,
            };
            membershipRequest.Approve(trainerUserId, now);
            membership = new Membership(
                trainer.TenantId,
                memberUserId,
                gym.Id,
                plan.Id,
                membershipRequest.Id,
                plan.Name,
                plan.DurationDays,
                plan.Price,
                plan.Currency,
                trainerUserId,
                now);
            context.MembershipRequests.Add(membershipRequest);
            context.Memberships.Add(membership);
        }
        var reservation = new AppointmentReservation(
            trainer.TenantId,
            memberUserId,
            trainer.Id,
            offering.Id,
            null,
            membership.Id,
            now.AddDays(startInDays),
            offering.DurationMinutes,
            offering.Price,
            offering.Currency);
        if (confirmed)
        {
            reservation.Confirm(trainerUserId, now);
        }
        context.AppointmentReservations.Add(reservation);
        if (!await context.UserGymAssignments.IgnoreQueryFilters().AnyAsync(x =>
                x.TenantId == trainer.TenantId &&
                x.UserId == memberUserId &&
                x.Role == RoleNames.Member))
        {
            context.UserGymAssignments.Add(new UserGymAssignment
            {
                TenantId = trainer.TenantId,
                UserId = memberUserId,
                Role = RoleNames.Member,
                Status = AssignmentStatus.Active,
                StartsAtUtc = now,
                Reason = "Phase 9 integration test.",
                CreatedAtUtc = now,
            });
        }
        await context.SaveChangesAsync();
        return reservation.Id;
    }

    private static HubConnection CreateHub(
        WebApplicationFactory<Program> factory,
        string? accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, "/hubs/chat"),
                options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                })
            .WithAutomaticReconnect()
            .Build();

    private static async Task<AuthSessionDto> LoginAsync(
        HttpClient client,
        string identifier)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { identifier, password = Password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSessionDto>())!;
    }

    private static async Task<AuthSessionDto> RegisterAsync(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var suffix = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username = $"chat-{suffix}",
                email = $"chat-{suffix}@gymlink.test",
                displayName = "Concurrent Chat Member",
                password = Password,
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSessionDto>())!;
    }

    private static void Authorize(HttpClient client, AuthSessionDto session) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<
            Dictionary<string, object>>();
        return problem!["title"].ToString()!;
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
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
            builder.UseSetting("SignalR:EnableDetailedErrors", "true");
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
                    ["RabbitMq:Enabled"] = "false",
                    ["SignalR:EnableDetailedErrors"] = "true",
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
