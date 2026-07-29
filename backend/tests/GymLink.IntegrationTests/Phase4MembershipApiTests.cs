using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymLink.Application.Catalog;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Application.Memberships;
using GymLink.Application.Payments;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GymLink.IntegrationTests;

public sealed class Phase4MembershipApiTests
{
    private const string Password = "Test123!";
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes";

    [Fact]
    public async Task Selecting_membership_plan_immediately_opens_checkout_and_payment_activates()
    {
        var databaseName = $"GymLink_ImmediateMembership_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        try
        {
            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            var member = await LoginAsync(client, "member");
            var planId = await FindPlanAsync(client, "GymLink Sarajevo");
            Authorize(client, member);

            var checkout = await client.PostAsJsonAsync(
                "/api/payments/memberships/checkout",
                new { membershipPlanId = planId });
            checkout.EnsureSuccessStatusCode();
            var checkoutResult =
                await checkout.Content.ReadFromJsonAsync<CheckoutSessionDto>();
            Assert.NotNull(checkoutResult);
            Assert.StartsWith("https://checkout.test/", checkoutResult.CheckoutUrl);
            var gateway = Assert.IsType<TestPaymentGateway>(
                factory.Services.GetRequiredService<IPaymentGateway>());
            Assert.Equal(member.User.Email, gateway.LastCheckoutRequest?.CustomerEmail);

            var pending = Assert.Single((await GetMineAsync(client)).Items);
            Assert.Equal(MembershipStatus.PendingPayment, pending.Status);
            var providerSessionId = $"cs_test_{checkoutResult.PaymentId:N}";
            (await client.PostAsync(
                "/api/webhooks/stripe",
                new StringContent(providerSessionId))).EnsureSuccessStatusCode();
            var returnPage = await client.GetAsync(
                $"/payments/stripe/success?session_id={providerSessionId}");
            returnPage.EnsureSuccessStatusCode();
            Assert.Contains(
                "Vrati se u GymLink",
                await returnPage.Content.ReadAsStringAsync());

            var active = Assert.Single((await GetMineAsync(client)).Items);
            Assert.Equal(MembershipStatus.Active, active.Status);
            Assert.True(active.IsPaid);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Gym_admin_promotes_only_an_active_tenant_member_to_trainer()
    {
        var databaseName = $"GymLink_TrainerPromotion_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);

        try
        {
            await using (var migrationContext = CreateContext(connectionString))
            {
                await migrationContext.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            var member = await LoginAsync(client, "member");
            var sarajevoAdmin = await LoginAsync(client, "desktop");
            var mostarAdmin = await LoginAsync(client, "gymadmin");
            var planId = await FindPlanAsync(client, "GymLink Sarajevo");

            Authorize(client, member);
            var request = await CreateRequestAsync(client, planId);
            Authorize(client, sarajevoAdmin);
            var approval = await client.PostAsJsonAsync(
                $"/api/tenant/membership-requests/{request.Id}/approve",
                new { concurrencyToken = request.ConcurrencyToken });
            approval.EnsureSuccessStatusCode();
            await PayPendingMembershipAsync(client, member);

            Authorize(client, mostarAdmin);
            var isolatedCandidates =
                await client.GetFromJsonAsync<PagedResult<TrainerCandidateDto>>(
                    "/api/tenant/trainer-candidates?page=1&pageSize=10");
            Assert.NotNull(isolatedCandidates);
            Assert.Empty(isolatedCandidates.Items);

            Authorize(client, sarajevoAdmin);
            var candidates =
                await client.GetFromJsonAsync<PagedResult<TrainerCandidateDto>>(
                    "/api/tenant/trainer-candidates?query=Role&page=1&pageSize=10");
            Assert.NotNull(candidates);
            var candidate = Assert.Single(candidates.Items);
            Assert.Equal(member.User.Id, candidate.UserId);
            using var lookups = JsonDocument.Parse(
                await client.GetStringAsync("/api/reference-data/lookups"));
            var trainingTypeId = lookups.RootElement
                .GetProperty("trainingTypes")[0]
                .GetProperty("id")
                .GetGuid();

            var promotion = await client.PostAsJsonAsync(
                "/api/tenant/trainers",
                new
                {
                    userId = candidate.UserId,
                    biography = "Promoted active member and certified trainer.",
                    credentials = "Certified trainer",
                    trainingTypeIds = new[] { trainingTypeId },
                    reason = "Approved by the assigned gym administrator",
                });
            Assert.Equal(HttpStatusCode.Created, promotion.StatusCode);
            var trainer = await promotion.Content.ReadFromJsonAsync<TrainerDto>();
            Assert.NotNull(trainer);
            Assert.Equal(candidate.UserId, trainer.UserId);

            Authorize(client, member);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await client.GetAsync("/api/profile")).StatusCode);
            var trainerSession = await LoginAsync(client, "member");
            Assert.Equal(RoleNames.Trainer, trainerSession.User.Role);
            Assert.Equal("GymLink Sarajevo", trainerSession.User.Tenant?.Name);

            await using var verification = CreateContext(connectionString);
            Assert.True(await verification.Memberships.IgnoreQueryFilters().AnyAsync(
                membership =>
                    membership.MemberUserId == candidate.UserId &&
                    membership.Status == MembershipStatus.Active));
            Assert.True(await verification.TrainerProfiles.IgnoreQueryFilters().AnyAsync(
                profile => profile.UserId == candidate.UserId));
            Assert.True(await verification.UserGymAssignments.IgnoreQueryFilters().AnyAsync(
                assignment =>
                    assignment.UserId == candidate.UserId &&
                    assignment.Role == RoleNames.Trainer &&
                    assignment.Status == AssignmentStatus.Active));
            Assert.True(await verification.UserGymAssignments.IgnoreQueryFilters().AnyAsync(
                assignment =>
                    assignment.UserId == candidate.UserId &&
                    assignment.Role == RoleNames.Member &&
                    assignment.Status == AssignmentStatus.Ended));
            Assert.True(await verification.SecurityAuditRecords.AnyAsync(
                audit =>
                    audit.TargetUserId == candidate.UserId &&
                    audit.Action == "trainer.promoted"));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Member_and_tenant_admin_complete_isolated_multi_gym_workflow()
    {
        var databaseName = $"GymLink_Phase4_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);

        try
        {
            await using (var migrationContext = CreateContext(connectionString))
            {
                await migrationContext.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

            var member = await LoginAsync(client, "member");
            var sarajevoAdmin = await LoginAsync(client, "desktop");
            var mostarAdmin = await LoginAsync(client, "gymadmin");
            var sarajevoPlanId = await FindPlanAsync(client, "GymLink Sarajevo");
            var mostarPlanId = await FindPlanAsync(client, "GymLink Mostar");

            Authorize(client, member);
            var sarajevoRequest = await CreateRequestAsync(client, sarajevoPlanId);
            Assert.Equal("GymLink Sarajevo", sarajevoRequest.GymName);
            var duplicate = await client.PostAsJsonAsync(
                "/api/membership-requests",
                new { membershipPlanId = sarajevoPlanId });
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal(
                "membership_request_already_pending",
                await ReadProblemCodeAsync(duplicate));

            Authorize(client, mostarAdmin);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync(
                    $"/api/tenant/membership-requests/{sarajevoRequest.Id}")).StatusCode);

            Authorize(client, sarajevoAdmin);
            var approval = await client.PostAsJsonAsync(
                $"/api/tenant/membership-requests/{sarajevoRequest.Id}/approve",
                new { concurrencyToken = sarajevoRequest.ConcurrencyToken });
            approval.EnsureSuccessStatusCode();
            var approvedRequest =
                await approval.Content.ReadFromJsonAsync<MembershipRequestDto>();
            Assert.NotNull(approvedRequest);
            Assert.Equal(MembershipRequestStatus.Approved, approvedRequest.Status);
            Assert.NotEqual(Guid.Empty, approvedRequest.GymId);

            var staleApproval = await client.PostAsJsonAsync(
                $"/api/tenant/membership-requests/{sarajevoRequest.Id}/approve",
                new { concurrencyToken = sarajevoRequest.ConcurrencyToken });
            Assert.Equal(HttpStatusCode.Conflict, staleApproval.StatusCode);
            Assert.Equal("concurrency_conflict", await ReadProblemCodeAsync(staleApproval));

            await PayPendingMembershipAsync(client, member);
            var memberships = await GetMineAsync(client);
            var sarajevoMembership = Assert.Single(memberships.Items);
            Assert.Equal(MembershipStatus.Active, sarajevoMembership.Status);
            Assert.Equal(approvedRequest.GymId, sarajevoMembership.GymId);
            Assert.Empty(sarajevoMembership.AllowedActions);
            Assert.True(sarajevoMembership.IsPaid);
            Assert.NotNull(sarajevoMembership.StartsAtUtc);
            Assert.NotNull(sarajevoMembership.EndsAtUtc);
            Assert.Equal(
                sarajevoMembership.StartsAtUtc.Value.AddDays(30),
                sarajevoMembership.EndsAtUtc);

            var currentForGym = await client.GetFromJsonAsync<PagedResult<MembershipDto>>(
                $"/api/me/memberships?gymId={sarajevoMembership.GymId}" +
                "&currentOnly=true&page=1&pageSize=10");
            Assert.NotNull(currentForGym);
            Assert.Single(currentForGym.Items);
            var covering = await client.GetFromJsonAsync<PagedResult<MembershipDto>>(
                $"/api/me/memberships?gymId={sarajevoMembership.GymId}&status=Active" +
                $"&coversFromUtc={Uri.EscapeDataString(sarajevoMembership.StartsAtUtc.Value.ToString("O"))}" +
                $"&coversToUtc={Uri.EscapeDataString(sarajevoMembership.EndsAtUtc.Value.ToString("O"))}" +
                "&page=1&pageSize=10");
            Assert.NotNull(covering);
            Assert.Single(covering.Items);

            var duplicateCurrent = await client.PostAsJsonAsync(
                "/api/membership-requests",
                new { membershipPlanId = sarajevoPlanId });
            Assert.Equal(HttpStatusCode.Conflict, duplicateCurrent.StatusCode);
            Assert.Equal("current_membership_exists", await ReadProblemCodeAsync(duplicateCurrent));

            var mostarRequest = await CreateRequestAsync(client, mostarPlanId);
            Authorize(client, mostarAdmin);
            var mostarApproval = await client.PostAsJsonAsync(
                $"/api/tenant/membership-requests/{mostarRequest.Id}/approve",
                new { concurrencyToken = mostarRequest.ConcurrencyToken });
            mostarApproval.EnsureSuccessStatusCode();

            await PayPendingMembershipAsync(client, member);
            memberships = await GetMineAsync(client);
            Assert.Equal(2, memberships.TotalCount);
            Assert.All(memberships.Items, item => Assert.Equal(MembershipStatus.Active, item.Status));

            Authorize(client, sarajevoAdmin);
            var suspend = await client.PostAsJsonAsync(
                $"/api/tenant/memberships/{sarajevoMembership.Id}/suspend",
                new
                {
                    concurrencyToken = sarajevoMembership.ConcurrencyToken,
                    reason = "Temporary policy hold",
                });
            suspend.EnsureSuccessStatusCode();
            var suspended = await suspend.Content.ReadFromJsonAsync<MembershipDto>();
            Assert.NotNull(suspended);
            Assert.Equal(MembershipStatus.Suspended, suspended.Status);
            Assert.Equal(["reactivate"], suspended.AllowedActions);

            var reactivate = await client.PostAsJsonAsync(
                $"/api/tenant/memberships/{suspended.Id}/reactivate",
                new
                {
                    concurrencyToken = suspended.ConcurrencyToken,
                    reason = "Policy issue resolved",
                });
            reactivate.EnsureSuccessStatusCode();
            var active = await reactivate.Content.ReadFromJsonAsync<MembershipDto>();
            Assert.NotNull(active);
            Assert.Equal(MembershipStatus.Active, active.Status);

            Authorize(client, member);
            var cancel = await client.PostAsJsonAsync(
                $"/api/me/memberships/{active.Id}/cancel",
                new { concurrencyToken = active.ConcurrencyToken });
            Assert.Equal(HttpStatusCode.BadRequest, cancel.StatusCode);
            Assert.Equal("paid_cancellation_not_supported", await ReadProblemCodeAsync(cancel));

            Authorize(client, mostarAdmin);
            var tenantSearch = await client.GetFromJsonAsync<PagedResult<MembershipDto>>(
                "/api/tenant/memberships?status=Active&page=1&pageSize=10");
            Assert.NotNull(tenantSearch);
            Assert.Equal(1, tenantSearch.TotalCount);
            Assert.Equal("GymLink Mostar", tenantSearch.Items[0].GymName);

            await using var verification = CreateContext(connectionString);
            var memberId = member.User.Id;
            var persisted = await verification.Memberships.IgnoreQueryFilters()
                .Where(x => x.MemberUserId == memberId)
                .OrderBy(x => x.GymId)
                .ToListAsync();
            Assert.Equal(2, persisted.Count);
            Assert.All(persisted, x => Assert.Equal(MembershipStatus.Active, x.Status));
            Assert.Equal(
                2,
                await verification.UserGymAssignments.IgnoreQueryFilters().CountAsync(
                    x => x.UserId == memberId &&
                         x.Role == RoleNames.Member &&
                         x.Status == AssignmentStatus.Active));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Approval_rolls_back_when_the_plan_is_inactive()
    {
        var databaseName = $"GymLink_Phase4Rollback_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);

        try
        {
            await using (var migrationContext = CreateContext(connectionString))
            {
                await migrationContext.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
            var member = await LoginAsync(client, "member");
            var admin = await LoginAsync(client, "desktop");
            var planId = await FindPlanAsync(client, "GymLink Sarajevo");

            Authorize(client, member);
            var request = await CreateRequestAsync(client, planId);

            await using (var context = CreateContext(connectionString))
            {
                var plan = await context.MembershipPlans.IgnoreQueryFilters()
                    .SingleAsync(x => x.Id == planId);
                plan.IsActive = false;
                await context.SaveChangesAsync();
            }

            Authorize(client, admin);
            var response = await client.PostAsJsonAsync(
                $"/api/tenant/membership-requests/{request.Id}/approve",
                new { concurrencyToken = request.ConcurrencyToken });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("membership_plan_inactive", await ReadProblemCodeAsync(response));

            await using var verification = CreateContext(connectionString);
            var persistedRequest = await verification.MembershipRequests.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == request.Id);
            Assert.Equal(MembershipRequestStatus.Pending, persistedRequest.Status);
            Assert.False(await verification.Memberships.IgnoreQueryFilters().AnyAsync(
                x => x.MembershipRequestId == request.Id));
            Assert.False(await verification.UserGymAssignments.IgnoreQueryFilters().AnyAsync(
                x => x.UserId == member.User.Id && x.Role == RoleNames.Member));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<MembershipRequestDto> CreateRequestAsync(
        HttpClient client,
        Guid planId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/membership-requests",
            new { membershipPlanId = planId });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MembershipRequestDto>()
            ?? throw new InvalidOperationException("Membership request returned no body.");
    }

    private static async Task<PagedResult<MembershipDto>> GetMineAsync(HttpClient client) =>
        await client.GetFromJsonAsync<PagedResult<MembershipDto>>(
            "/api/me/memberships?page=1&pageSize=10")
        ?? throw new InvalidOperationException("Membership search returned no body.");

    private static async Task<Guid> FindPlanAsync(HttpClient client, string gymName)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var gyms = JsonDocument.Parse(
            await client.GetStringAsync($"/api/gyms?query={Uri.EscapeDataString(gymName)}"));
        var gymId = gyms.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();
        using var plans = JsonDocument.Parse(
            await client.GetStringAsync($"/api/gyms/{gymId}/membership-plans"));
        return plans.RootElement[0].GetProperty("id").GetGuid();
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

    private static async Task PayPendingMembershipAsync(
        HttpClient client,
        AuthSessionDto member)
    {
        Authorize(client, member);
        var memberships = await GetMineAsync(client);
        var pending = memberships.Items.Single(x => x.Status == MembershipStatus.PendingPayment);
        var checkouts = await Task.WhenAll(
            client.PostAsync($"/api/payments/memberships/{pending.Id}/checkout", null),
            client.PostAsync($"/api/payments/memberships/{pending.Id}/checkout", null));
        Assert.All(checkouts, response => response.EnsureSuccessStatusCode());
        var first = await checkouts[0].Content.ReadFromJsonAsync<CheckoutSessionDto>();
        var second = await checkouts[1].Content.ReadFromJsonAsync<CheckoutSessionDto>();
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.PaymentId, second.PaymentId);
        var providerSessionId = $"cs_test_{first.PaymentId:N}";
        var webhook = await client.PostAsync(
            "/api/webhooks/stripe",
            new StringContent(providerSessionId));
        webhook.EnsureSuccessStatusCode();
        var webhookReplay = await client.PostAsync(
            "/api/webhooks/stripe",
            new StringContent(providerSessionId));
        webhookReplay.EnsureSuccessStatusCode();
        var returnResponse = await client.GetAsync(
            $"/payments/stripe/success?session_id={providerSessionId}");
        returnResponse.EnsureSuccessStatusCode();
        Assert.Contains(
            "Vrati se u GymLink",
            await returnResponse.Content.ReadAsStringAsync());
        var replay = await client.GetAsync(
            $"/payments/stripe/success?session_id={providerSessionId}");
        replay.EnsureSuccessStatusCode();
    }

    private static async Task<string> ReadProblemCodeAsync(HttpResponseMessage response)
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
            builder.ConfigureServices(services =>
                services.Replace(ServiceDescriptor.Singleton<
                    IPaymentGateway,
                    TestPaymentGateway>()));
        });

    private static GymLinkDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GymLinkDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new GymLinkDbContext(options, new TestTenantContext(null));
    }
}
