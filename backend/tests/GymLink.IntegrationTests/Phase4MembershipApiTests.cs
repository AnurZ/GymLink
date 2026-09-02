using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GymLink.Application.Catalog;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Application.Memberships;
using GymLink.Application.Payments;
using GymLink.Contracts.Messaging.V1;
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
            var member = await RegisterAsync(client);
            Authorize(client, member);
            var planId = await FindPlanAsync(client, "Sportska Akademija Respect");

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
    public async Task Manual_payment_routes_are_removed_and_legacy_method_cannot_be_selected()
    {
        var databaseName = $"GymLink_ManualPayment_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        try
        {
            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            var member = await RegisterAsync(client);
            Authorize(client, member);
            var planId = await FindPlanAsync(client, "Sportska Akademija Respect");

            var legacyMethod = await client.PostAsJsonAsync(
                "/api/membership-requests",
                new
                {
                    membershipPlanId = planId,
                    paymentMethod = "StripeFallback",
                });
            Assert.Equal(HttpStatusCode.BadRequest, legacyMethod.StatusCode);
            Assert.Equal(
                "unsupported_membership_payment_method",
                await ReadProblemCodeAsync(legacyMethod));

            var removedRoutes = new[]
            {
                await client.PostAsJsonAsync(
                    "/api/payments/manual/memberships/pay",
                    new { membershipPlanId = planId }),
                await client.PostAsync(
                    $"/api/payments/manual/memberships/{Guid.NewGuid()}/pay",
                    null),
                await client.PostAsync(
                    $"/api/payments/manual/reservations/{Guid.NewGuid()}/pay",
                    null),
            };
            Assert.All(
                removedRoutes,
                response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
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
            var member = await RegisterAsync(client, "Role Test Member");
            var sarajevoAdmin = await LoginAsync(client, "admin.respect");
            var mostarAdmin = await LoginAsync(client, "admin.arena");
            Authorize(client, member);
            var planId = await FindPlanAsync(client, "Sportska Akademija Respect");

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
            Assert.DoesNotContain(isolatedCandidates.Items, x => x.UserId == member.User.Id);

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
            var trainerSession = await LoginAsync(client, member.User.Username);
            Assert.Equal(RoleNames.Trainer, trainerSession.User.Role);
            Assert.Equal("Sportska Akademija Respect", trainerSession.User.Tenant?.Name);

            Guid gymId;
            await using (var verification = CreateContext(connectionString))
            {
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
                gymId = await (
                        from gym in verification.Gyms.IgnoreQueryFilters()
                        join profile in verification.TrainerProfiles.IgnoreQueryFilters()
                            on gym.TenantId equals profile.TenantId
                        where profile.Id == trainer.Id
                        select gym.Id)
                    .SingleAsync();
            }

            Authorize(client, sarajevoAdmin);
            var shortReason = await client.PostAsJsonAsync(
                $"/api/tenant/trainers/{trainer.Id}/deactivate",
                new { reason = " x " });
            Assert.Equal(HttpStatusCode.BadRequest, shortReason.StatusCode);
            using (var validation = JsonDocument.Parse(
                       await shortReason.Content.ReadAsStringAsync()))
            {
                Assert.True(validation.RootElement.GetProperty("errors")
                    .TryGetProperty("Reason", out _));
            }

            var deactivation = await client.PostAsJsonAsync(
                $"/api/tenant/trainers/{trainer.Id}/deactivate",
                new { reason = "Trainer is temporarily unavailable" });
            deactivation.EnsureSuccessStatusCode();
            var inactiveTrainer = await deactivation.Content.ReadFromJsonAsync<TrainerDto>();
            Assert.NotNull(inactiveTrainer);
            Assert.Equal(trainer.Id, inactiveTrainer.Id);
            Assert.False(inactiveTrainer.IsActive);

            Authorize(client, sarajevoAdmin);
            var hiddenTrainers = await client.GetFromJsonAsync<PagedResult<TrainerDto>>(
                $"/api/gyms/{gymId}/trainers?page=1&pageSize=50");
            Assert.NotNull(hiddenTrainers);
            Assert.DoesNotContain(hiddenTrainers.Items, item => item.Id == trainer.Id);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync($"/api/trainers/{trainer.Id}/offerings")).StatusCode);

            Authorize(client, trainerSession);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await client.GetAsync("/api/profile")).StatusCode);
            var memberSession = await LoginAsync(client, member.User.Username);
            Assert.Equal(RoleNames.Member, memberSession.User.Role);

            await using (var expireMembership = CreateContext(connectionString))
            {
                var membership = await expireMembership.Memberships.IgnoreQueryFilters()
                    .SingleAsync(item => item.MemberUserId == candidate.UserId);
                expireMembership.Entry(membership).Property(item => item.Status).CurrentValue =
                    MembershipStatus.Expired;
                expireMembership.Entry(membership).Property(item => item.EndsAtUtc).CurrentValue =
                    membership.StartsAtUtc!.Value.AddSeconds(1);
                await expireMembership.SaveChangesAsync();
            }

            Authorize(client, sarajevoAdmin);
            var reactivation = await client.PostAsJsonAsync(
                $"/api/tenant/trainers/{trainer.Id}/reactivate",
                new { reason = "Trainer has returned to work" });
            reactivation.EnsureSuccessStatusCode();
            var activeTrainer = await reactivation.Content.ReadFromJsonAsync<TrainerDto>();
            Assert.NotNull(activeTrainer);
            Assert.Equal(trainer.Id, activeTrainer.Id);
            Assert.True(activeTrainer.IsActive);
            Assert.Equal(trainer.TrainingTypeIds, activeTrainer.TrainingTypeIds);

            Authorize(client, sarajevoAdmin);
            var visibleTrainers = await client.GetFromJsonAsync<PagedResult<TrainerDto>>(
                $"/api/gyms/{gymId}/trainers?page=1&pageSize=50");
            Assert.NotNull(visibleTrainers);
            Assert.Contains(visibleTrainers.Items, item => item.Id == trainer.Id);

            Authorize(client, memberSession);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await client.GetAsync("/api/profile")).StatusCode);
            var restoredTrainerSession = await LoginAsync(client, member.User.Username);
            Assert.Equal(RoleNames.Trainer, restoredTrainerSession.User.Role);

            await using var finalVerification = CreateContext(connectionString);
            Assert.Single(await finalVerification.TrainerProfiles.IgnoreQueryFilters()
                .Where(profile => profile.UserId == candidate.UserId && profile.IsActive)
                .ToListAsync());
            Assert.Single(await finalVerification.UserGymAssignments.IgnoreQueryFilters()
                .Where(assignment =>
                    assignment.UserId == candidate.UserId &&
                    assignment.Role == RoleNames.Trainer &&
                    assignment.Status == AssignmentStatus.Active)
                .ToListAsync());
            Assert.True(await finalVerification.SecurityAuditRecords.AnyAsync(
                audit =>
                    audit.TargetUserId == candidate.UserId &&
                    audit.Action == "trainer.deactivated"));
            Assert.True(await finalVerification.SecurityAuditRecords.AnyAsync(
                audit =>
                    audit.TargetUserId == candidate.UserId &&
                    audit.Action == "trainer.reactivated"));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Central_admin_generic_role_assignment_rejects_trainer_promotion()
    {
        var databaseName = $"GymLink_GenericTrainerRole_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        try
        {
            await using (var migrationContext = CreateContext(connectionString))
            {
                await migrationContext.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            var member = await LoginAsync(client, "mobile1");
            var centralAdmin = await LoginAsync(client, "centraladmin");
            Guid tenantId;
            await using (var context = CreateContext(connectionString))
            {
                tenantId = await context.Tenants
                    .Where(tenant => tenant.Name == "Sportska Akademija Respect")
                    .Select(tenant => tenant.Id)
                    .SingleAsync();
            }

            Authorize(client, centralAdmin);
            var response = await client.PostAsJsonAsync(
                "/api/admin/users/roles/assign",
                new
                {
                    identifier = member.User.Email,
                    role = RoleNames.Trainer,
                    tenantId,
                    reason = "Attempted generic promotion",
                });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("trainer_promotion_required", await ReadProblemCodeAsync(response));

            var unchanged = await LoginAsync(client, member.User.Username);
            Assert.Equal(RoleNames.Member, unchanged.User.Role);
            await using var verification = CreateContext(connectionString);
            Assert.False(await verification.TrainerProfiles.IgnoreQueryFilters().AnyAsync(
                profile => profile.UserId == member.User.Id));
            Assert.False(await verification.UserGymAssignments.IgnoreQueryFilters().AnyAsync(
                assignment =>
                    assignment.UserId == member.User.Id &&
                    assignment.Role == RoleNames.Trainer));
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

            var member = await RegisterAsync(client);
            var sarajevoAdmin = await LoginAsync(client, "admin.respect");
            var mostarAdmin = await LoginAsync(client, "admin.arena");
            Authorize(client, member);
            var sarajevoPlanId = await FindPlanAsync(client, "Sportska Akademija Respect");
            var mostarPlanId = await FindPlanAsync(client, "Arena Sport Centar");

            Authorize(client, member);
            var sarajevoRequest = await CreateRequestAsync(client, sarajevoPlanId);
            Assert.Equal("Sportska Akademija Respect", sarajevoRequest.GymName);
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
            Assert.Contains(
                tenantSearch.Items,
                x => x.GymName == "Arena Sport Centar" &&
                     x.MemberDisplayName == member.User.DisplayName);

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
            var member = await RegisterAsync(client);
            var admin = await LoginAsync(client, "admin.respect");
            Authorize(client, member);
            var planId = await FindPlanAsync(client, "Sportska Akademija Respect");

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

    [Fact]
    public async Task Pay_in_person_waits_for_tenant_admin_and_activates_without_payment()
    {
        var databaseName = $"GymLink_Phase4_Cash_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);

        try
        {
            await using (var migrationContext = CreateContext(connectionString))
            {
                await migrationContext.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            var member = await RegisterAsync(client, "Cash Membership Member");
            var numericMember = await RegisterAsync(client, "Numeric Cash Member");
            var stripeMember = await RegisterAsync(client, "Stripe Membership Member");
            var admin = await LoginAsync(client, "admin.respect");
            Authorize(client, numericMember);
            var planId = await FindPlanAsync(client, "Sportska Akademija Respect");

            Authorize(client, numericMember);
            var numericResponse = await client.PostAsJsonAsync(
                "/api/membership-requests",
                new
                {
                    membershipPlanId = planId,
                    paymentMethod = (int)MembershipPaymentMethod.PayInPerson,
                });
            numericResponse.EnsureSuccessStatusCode();

            Authorize(client, stripeMember);
            var stripeResponse = await client.PostAsJsonAsync(
                "/api/membership-requests",
                new { membershipPlanId = planId });
            stripeResponse.EnsureSuccessStatusCode();

            Authorize(client, member);
            var unsupported = await client.PostAsJsonAsync(
                "/api/membership-requests",
                new
                {
                    membershipPlanId = planId,
                    paymentMethod = 99,
                });
            Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
            using (var problem = JsonDocument.Parse(await unsupported.Content.ReadAsStringAsync()))
            {
                Assert.Equal(
                    "unsupported_membership_payment_method",
                    problem.RootElement.GetProperty("title").GetString());
            }

            var response = await client.PostAsync(
                "/api/membership-requests",
                new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        membershipPlanId = planId,
                        paymentMethod = "PayInPerson",
                    }),
                    Encoding.UTF8,
                    "application/json"));
            response.EnsureSuccessStatusCode();
            var request = await response.Content.ReadFromJsonAsync<MembershipRequestDto>();
            Assert.NotNull(request);
            Assert.Equal(MembershipPaymentMethod.PayInPerson, request.PaymentMethod);
            Assert.Equal(MembershipRequestStatus.Pending, request.Status);
            Assert.Equal(member.User.Email, request.MemberEmail);
            Assert.Empty((await GetMineAsync(client)).Items);

            Authorize(client, admin);
            var tenantPage = await client.GetFromJsonAsync<PagedResult<MembershipRequestDto>>(
                "/api/tenant/membership-requests?paymentMethod=PayInPerson&member=Cash%20Membership%20Member&page=1&pageSize=10");
            Assert.NotNull(tenantPage);
            var tenantRequest = Assert.Single(tenantPage.Items);
            Assert.Equal(["approve", "reject", "view"], tenantRequest.AllowedActions);
            var groupedCash =
                await client.GetFromJsonAsync<PagedResult<MembershipRequestDto>>(
                    "/api/tenant/membership-requests?paymentCategory=PayInPerson&member=Cash%20Membership%20Member&page=1&pageSize=10");
            Assert.NotNull(groupedCash);
            Assert.Equal(request.Id, Assert.Single(groupedCash.Items).Id);
            var groupedStripe =
                await client.GetFromJsonAsync<PagedResult<MembershipRequestDto>>(
                    "/api/tenant/membership-requests?paymentCategory=Stripe&page=1&pageSize=100");
            Assert.NotNull(groupedStripe);
            Assert.NotEmpty(groupedStripe.Items);
            Assert.All(groupedStripe.Items, item => Assert.Contains(
                item.PaymentMethod,
                new[]
                {
                    MembershipPaymentMethod.Stripe,
                    MembershipPaymentMethod.StripeFallback,
                }));

            var approval = await client.PostAsJsonAsync(
                $"/api/tenant/membership-requests/{request.Id}/approve",
                new { concurrencyToken = tenantRequest.ConcurrencyToken });
            approval.EnsureSuccessStatusCode();
            var approvedRequest =
                await approval.Content.ReadFromJsonAsync<MembershipRequestDto>();
            var linkedMembership = Assert.IsType<MembershipRequestMembershipDto>(
                approvedRequest?.Membership);
            Assert.Equal(MembershipStatus.Active, linkedMembership.Status);
            Assert.False(linkedMembership.IsPaid);
            Assert.Contains("cancel", linkedMembership.AllowedActions);

            var activeRequests =
                await client.GetFromJsonAsync<PagedResult<MembershipRequestDto>>(
                    "/api/tenant/membership-requests?membershipStatus=Active&member=Cash%20Membership%20Member&page=1&pageSize=10");
            Assert.NotNull(activeRequests);
            Assert.Equal(request.Id, Assert.Single(activeRequests.Items).Id);

            Authorize(client, member);
            var membership = Assert.Single((await GetMineAsync(client)).Items);
            Assert.Equal(MembershipStatus.Active, membership.Status);
            Assert.False(membership.IsPaid);
            Assert.Null(membership.PaymentId);
            Assert.NotNull(membership.StartsAtUtc);
            Assert.NotNull(membership.EndsAtUtc);

            await using var verification = CreateContext(connectionString);
            Assert.False(await verification.Payments.IgnoreQueryFilters().AnyAsync(
                x => x.TargetId == membership.Id));
            Assert.Contains(
                await verification.UserGymAssignments.IgnoreQueryFilters()
                    .Where(x => x.UserId == member.User.Id)
                    .ToListAsync(),
                x => x.Role == RoleNames.Member &&
                    x.Status == AssignmentStatus.Active);
            Assert.Single(
                await verification.OutboxMessages
                    .Where(x =>
                        x.MessageType == MessageContractNames.WelcomeEmailRequestedV1 &&
                        x.Payload.Contains(member.User.Id.ToString()))
                    .ToListAsync());
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Expired_active_membership_is_not_current_and_lazy_renewal_releases_unique_index()
    {
        var databaseName = $"GymLink_MembershipExpiryLazy_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        try
        {
            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            var member = await RegisterAsync(client, "Lazy Expiry Member");
            Authorize(client, member);
            var planId = await FindPlanAsync(client, "Sportska Akademija Respect");
            (await client.PostAsJsonAsync(
                "/api/payments/memberships/checkout",
                new { membershipPlanId = planId })).EnsureSuccessStatusCode();
            await PayPendingMembershipAsync(client, member);
            var expiredId = Assert.Single((await GetMineAsync(client)).Items).Id;
            await BackdateMembershipAsync(connectionString, expiredId);

            var current = await client.GetFromJsonAsync<PagedResult<MembershipDto>>(
                "/api/me/memberships?currentOnly=true&page=1&pageSize=10");
            Assert.NotNull(current);
            Assert.Empty(current.Items);

            var renewal = await client.PostAsJsonAsync(
                "/api/payments/memberships/checkout",
                new { membershipPlanId = planId });
            renewal.EnsureSuccessStatusCode();

            await using var verification = CreateContext(connectionString);
            var persistedExpired = await verification.Memberships.IgnoreQueryFilters()
                .SingleAsync(entity => entity.Id == expiredId);
            Assert.Equal(MembershipStatus.Expired, persistedExpired.Status);
            Assert.Null(persistedExpired.StatusChangedByUserId);
            Assert.NotNull(persistedExpired.StatusChangedAtUtc);
            Assert.Single(await verification.Memberships.IgnoreQueryFilters()
                .Where(entity =>
                    entity.MemberUserId == member.User.Id &&
                    entity.GymId == persistedExpired.GymId &&
                    entity.Status == MembershipStatus.PendingPayment)
                .ToListAsync());
            Assert.Equal(
                2,
                await ExpiryNotificationCountAsync(verification, expiredId));
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Worker_expiry_is_cross_tenant_idempotent_and_safe_with_concurrent_renewal()
    {
        var databaseName = $"GymLink_MembershipExpiryWorker_{Guid.NewGuid():N}";
        var connectionString = TestSqlServer.ConnectionString(databaseName);
        try
        {
            await using (var migration = CreateContext(connectionString))
            {
                await migration.Database.MigrateAsync();
            }

            await using var factory = CreateFactory(connectionString);
            using var client = factory.CreateClient();
            var member = await RegisterAsync(client, "Worker Expiry Member");
            Authorize(client, member);
            var respectPlanId = await FindPlanAsync(client, "Sportska Akademija Respect");
            var arenaPlanId = await FindPlanAsync(client, "Arena Sport Centar");

            foreach (var planId in new[] { respectPlanId, arenaPlanId })
            {
                Authorize(client, member);
                (await client.PostAsJsonAsync(
                    "/api/payments/memberships/checkout",
                    new { membershipPlanId = planId })).EnsureSuccessStatusCode();
                await PayPendingMembershipAsync(client, member);
            }

            var memberships = (await GetMineAsync(client)).Items.ToArray();
            Assert.Equal(2, memberships.Length);
            foreach (var membership in memberships)
            {
                var admin = await LoginAsync(
                    client,
                    membership.GymName == "Sportska Akademija Respect"
                        ? "admin.respect"
                        : "admin.arena");
                Authorize(client, admin);
                var suspendedResponse = await client.PostAsJsonAsync(
                    $"/api/tenant/memberships/{membership.Id}/suspend",
                    new
                    {
                        concurrencyToken = membership.ConcurrencyToken,
                        reason = "Temporary expiry test hold",
                    });
                suspendedResponse.EnsureSuccessStatusCode();
                await BackdateMembershipAsync(connectionString, membership.Id);
            }

            Authorize(client, member);
            var current = await client.GetFromJsonAsync<PagedResult<MembershipDto>>(
                "/api/me/memberships?currentOnly=true&page=1&pageSize=10");
            Assert.NotNull(current);
            Assert.Empty(current.Items);

            using var scope = factory.Services.CreateScope();
            var expiry = scope.ServiceProvider.GetRequiredService<IMembershipExpiryService>();
            var scan = expiry.ExpireDueBatchAsync(CancellationToken.None);
            var renewal = client.PostAsJsonAsync(
                "/api/payments/memberships/checkout",
                new { membershipPlanId = respectPlanId });
            await Task.WhenAll(scan, renewal);
            var renewalResponse = await renewal;
            renewalResponse.EnsureSuccessStatusCode();
            Assert.InRange(await scan, 1, 100);
            Assert.Equal(0, await expiry.ExpireDueBatchAsync(CancellationToken.None));

            await using var verification = CreateContext(connectionString);
            var persisted = await verification.Memberships.IgnoreQueryFilters()
                .Where(entity => memberships.Select(item => item.Id).Contains(entity.Id))
                .ToListAsync();
            Assert.Equal(2, persisted.Count);
            Assert.All(persisted, entity =>
            {
                Assert.Equal(MembershipStatus.Expired, entity.Status);
                Assert.Null(entity.StatusChangedByUserId);
            });
            foreach (var membership in memberships)
            {
                Assert.Equal(
                    2,
                    await ExpiryNotificationCountAsync(verification, membership.Id));
            }
            Assert.Single(await verification.Memberships.IgnoreQueryFilters()
                .Where(entity =>
                    entity.MemberUserId == member.User.Id &&
                    entity.Status == MembershipStatus.PendingPayment)
                .ToListAsync());
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
        using var gyms = JsonDocument.Parse(
            await client.GetStringAsync($"/api/gyms?query={Uri.EscapeDataString(gymName)}"));
        var gymId = gyms.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();
        using var plans = JsonDocument.Parse(
            await client.GetStringAsync($"/api/gyms/{gymId}/membership-plans"));
        return plans.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();
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

    private static async Task<AuthSessionDto> RegisterAsync(
        HttpClient client,
        string displayName = "Workflow Test Member")
    {
        client.DefaultRequestHeaders.Authorization = null;
        var suffix = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username = $"workflow-{suffix}",
                email = $"workflow-{suffix}@gymlink.local",
                displayName,
                password = Password,
            });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthSessionDto>()
            ?? throw new InvalidOperationException("Registration returned no session.");
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

    private static async Task BackdateMembershipAsync(
        string connectionString,
        Guid membershipId)
    {
        var startsAtUtc = DateTime.UtcNow.AddDays(-31);
        var endsAtUtc = DateTime.UtcNow.AddDays(-1);
        await using var context = CreateContext(connectionString);
        Assert.Equal(
            1,
            await context.Memberships.IgnoreQueryFilters()
                .Where(entity => entity.Id == membershipId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(entity => entity.StartsAtUtc, startsAtUtc)
                    .SetProperty(entity => entity.EndsAtUtc, endsAtUtc)));
    }

    private static Task<int> ExpiryNotificationCountAsync(
        GymLinkDbContext context,
        Guid membershipId) =>
        context.OutboxMessages.CountAsync(message =>
            message.MessageType == MessageContractNames.NotificationRequestedV1 &&
            message.Payload.Contains("membership.expired") &&
            message.Payload.Contains(membershipId.ToString()));

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString) =>
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
            builder.UseSetting("RabbitMq:Enabled", "false");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var values = new Dictionary<string, string?>
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
                    ["RabbitMq:Enabled"] = "false",
                };
                configuration.AddInMemoryCollection(values);
            });
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
