using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Memberships;
using GymLink.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Memberships;

internal sealed class MembershipRequestService(
    IApplicationDbContext dbContext,
    IApplicationTransaction transaction,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    ITenantMutationScope tenantMutationScope,
    IMembershipWorkflowEventRecorder eventRecorder,
    TimeProvider timeProvider) : IMembershipRequestService
{
    public async Task<MembershipRequestDto> CreateAsync(
        CreateMembershipRequest request,
        CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var target = await (
                from plan in dbContext.MembershipPlans.IgnoreQueryFilters().AsNoTracking()
                join gym in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                    on new { plan.TenantId, Id = plan.GymId }
                    equals new { gym.TenantId, gym.Id }
                join tenant in dbContext.Tenants.AsNoTracking() on plan.TenantId equals tenant.Id
                where plan.Id == request.MembershipPlanId &&
                      plan.IsActive &&
                      gym.IsPubliclyVisible &&
                      tenant.Status == TenantStatus.Active
                select new { Plan = plan, GymName = gym.Name })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(
                "membership_plan_not_found",
                "The selected membership plan was not found.");

        if (await dbContext.MembershipRequests.IgnoreQueryFilters().AnyAsync(
                x => x.MemberUserId == userId &&
                     x.GymId == target.Plan.GymId &&
                     x.Status == MembershipRequestStatus.Pending,
                cancellationToken))
        {
            throw new ConflictException(
                "membership_request_already_pending",
                "A pending membership request already exists for this gym.");
        }

        if (await HasCurrentMembershipAsync(
                target.Plan.TenantId,
                userId,
                target.Plan.GymId,
                cancellationToken))
        {
            throw new ConflictException(
                "current_membership_exists",
                "A current membership already exists for this gym.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var entity = new MembershipRequest
        {
            TenantId = target.Plan.TenantId,
            MemberUserId = userId,
            GymId = target.Plan.GymId,
            MembershipPlanId = target.Plan.Id,
            RequestedAtUtc = now,
        };

        using (tenantMutationScope.Begin(target.Plan.TenantId))
        {
            dbContext.MembershipRequests.Add(entity);
            await eventRecorder.RecordAsync(new MembershipWorkflowEventIntent(
                "membership.requested",
                entity.TenantId,
                entity.MemberUserId,
                entity.Id,
                now),
                cancellationToken);
            await SaveWorkflowAsync(cancellationToken);
        }
        return await GetMineAsync(entity.Id, cancellationToken);
    }

    public Task<PagedResult<MembershipRequestDto>> SearchMineAsync(
        MembershipRequestSearchRequest request,
        CancellationToken cancellationToken) =>
        SearchAsync(
            request,
            RequireUser(),
            tenantId: null,
            ignoreQueryFilters: true,
            tenantAdmin: false,
            cancellationToken: cancellationToken);

    public Task<MembershipRequestDto> GetMineAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        GetProjectedAsync(
            id,
            RequireUser(),
            tenantId: null,
            ignoreQueryFilters: true,
            tenantAdmin: false,
            cancellationToken: cancellationToken);

    public async Task<MembershipRequestDto> CancelMineAsync(
        Guid id,
        ConcurrencyRequest request,
        CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var entity = await dbContext.MembershipRequests.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.Id == id && x.MemberUserId == userId,
                cancellationToken)
            ?? throw RequestNotFound();
        EnsureConcurrency(entity.RowVersion, request.ConcurrencyToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        entity.Cancel(userId, now);

        using (tenantMutationScope.Begin(entity.TenantId))
        {
            await eventRecorder.RecordAsync(new MembershipWorkflowEventIntent(
                "membership.request.cancelled",
                entity.TenantId,
                entity.MemberUserId,
                entity.Id,
                now),
                cancellationToken);
            await SaveWorkflowAsync(cancellationToken);
        }
        return await GetMineAsync(entity.Id, cancellationToken);
    }

    public Task<PagedResult<MembershipRequestDto>> SearchTenantAsync(
        MembershipRequestSearchRequest request,
        CancellationToken cancellationToken) =>
        SearchAsync(
            request,
            memberUserId: null,
            tenantId: RequireTenant(),
            ignoreQueryFilters: false,
            tenantAdmin: true,
            cancellationToken);

    public Task<MembershipRequestDto> GetTenantAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        GetProjectedAsync(
            id,
            memberUserId: null,
            tenantId: RequireTenant(),
            ignoreQueryFilters: false,
            tenantAdmin: true,
            cancellationToken);

    public async Task<MembershipRequestDto> ApproveAsync(
        Guid id,
        ConcurrencyRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = RequireUser();
        var tenantId = RequireTenant();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        await transaction.ExecuteAsync(async ct =>
        {
            var entity = await dbContext.MembershipRequests
                .SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct)
                ?? throw RequestNotFound();
            EnsureConcurrency(entity.RowVersion, request.ConcurrencyToken);

            var plan = await dbContext.MembershipPlans
                .SingleOrDefaultAsync(
                    x => x.Id == entity.MembershipPlanId &&
                         x.GymId == entity.GymId &&
                         x.IsActive,
                    ct)
                ?? throw new ConflictException(
                    "membership_plan_inactive",
                    "The selected membership plan is no longer active.");
            if (await HasCurrentMembershipAsync(tenantId, entity.MemberUserId, entity.GymId, ct))
            {
                throw new ConflictException(
                    "current_membership_exists",
                    "A current membership already exists for this gym.");
            }

            entity.Approve(actorId, now);
            var membership = Membership.CreatePendingPayment(
                tenantId,
                entity.MemberUserId,
                entity.GymId,
                entity.MembershipPlanId,
                entity.Id,
                plan.Name,
                plan.DurationDays,
                plan.Price,
                plan.Currency,
                actorId,
                now);
            dbContext.Memberships.Add(membership);
            await eventRecorder.RecordAsync(new MembershipWorkflowEventIntent(
                "membership.approved",
                membership.TenantId,
                membership.MemberUserId,
                membership.Id,
                now),
                ct);
            await SaveWorkflowAsync(ct);
            return membership;
        }, cancellationToken);
        return await GetTenantAsync(id, cancellationToken);
    }

    public async Task<MembershipRequestDto> RejectAsync(
        Guid id,
        ReasonedConcurrencyRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = RequireUser();
        var tenantId = RequireTenant();
        var entity = await dbContext.MembershipRequests
            .SingleOrDefaultAsync(
                x => x.Id == id && x.TenantId == tenantId,
                cancellationToken)
            ?? throw RequestNotFound();
        EnsureConcurrency(entity.RowVersion, request.ConcurrencyToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        entity.Reject(actorId, now, request.Reason);
        await eventRecorder.RecordAsync(new MembershipWorkflowEventIntent(
            "membership.rejected",
            entity.TenantId,
            entity.MemberUserId,
            entity.Id,
            now),
            cancellationToken);
        await SaveWorkflowAsync(cancellationToken);
        return await GetTenantAsync(entity.Id, cancellationToken);
    }

    private Task<PagedResult<MembershipRequestDto>> SearchAsync(
        MembershipRequestSearchRequest request,
        Guid? memberUserId,
        Guid? tenantId,
        bool ignoreQueryFilters,
        bool tenantAdmin,
        CancellationToken cancellationToken)
    {
        ValidateRange(request.RequestedFromUtc, request.RequestedToUtc);
        var source = ignoreQueryFilters
            ? dbContext.MembershipRequests.IgnoreQueryFilters().AsNoTracking()
            : dbContext.MembershipRequests.AsNoTracking();
        var query =
            from entity in source
            join member in dbContext.UserProfiles.AsNoTracking()
                on entity.MemberUserId equals member.Id
            join gym in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                on new { entity.TenantId, Id = entity.GymId }
                equals new { gym.TenantId, gym.Id }
            join plan in dbContext.MembershipPlans.IgnoreQueryFilters().AsNoTracking()
                on new { entity.TenantId, Id = entity.MembershipPlanId }
                equals new { plan.TenantId, plan.Id }
            where (!memberUserId.HasValue || entity.MemberUserId == memberUserId) &&
                  (!tenantId.HasValue || entity.TenantId == tenantId) &&
                  (!request.Status.HasValue || entity.Status == request.Status) &&
                  (!request.MembershipPlanId.HasValue ||
                   entity.MembershipPlanId == request.MembershipPlanId) &&
                  (!request.GymId.HasValue || entity.GymId == request.GymId) &&
                  (!request.RequestedFromUtc.HasValue ||
                   entity.RequestedAtUtc >= request.RequestedFromUtc) &&
                  (!request.RequestedToUtc.HasValue ||
                   entity.RequestedAtUtc <= request.RequestedToUtc) &&
                  (string.IsNullOrWhiteSpace(request.Member) ||
                   member.DisplayName.Contains(request.Member.Trim()))
            orderby entity.RequestedAtUtc descending, entity.Id
            select MapRequest(
                entity,
                member.DisplayName,
                gym.Name,
                plan.Name,
                plan.Price,
                plan.Currency,
                tenantAdmin);
        return query.ToPagedResultAsync(request, cancellationToken);
    }

    private async Task<MembershipRequestDto> GetProjectedAsync(
        Guid id,
        Guid? memberUserId,
        Guid? tenantId,
        bool ignoreQueryFilters,
        bool tenantAdmin,
        CancellationToken cancellationToken)
    {
        var source = ignoreQueryFilters
            ? dbContext.MembershipRequests.IgnoreQueryFilters().AsNoTracking()
            : dbContext.MembershipRequests.AsNoTracking();
        return await (
                from entity in source
                join member in dbContext.UserProfiles.AsNoTracking()
                    on entity.MemberUserId equals member.Id
                join gym in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                    on new { entity.TenantId, Id = entity.GymId }
                    equals new { gym.TenantId, gym.Id }
                join plan in dbContext.MembershipPlans.IgnoreQueryFilters().AsNoTracking()
                    on new { entity.TenantId, Id = entity.MembershipPlanId }
                    equals new { plan.TenantId, plan.Id }
                where entity.Id == id &&
                      (!memberUserId.HasValue || entity.MemberUserId == memberUserId) &&
                      (!tenantId.HasValue || entity.TenantId == tenantId)
                select MapRequest(
                    entity,
                    member.DisplayName,
                    gym.Name,
                    plan.Name,
                    plan.Price,
                    plan.Currency,
                    tenantAdmin))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw RequestNotFound();
    }

    private static MembershipRequestDto MapRequest(
        MembershipRequest entity,
        string memberName,
        string gymName,
        string planName,
        decimal price,
        string currency,
        bool tenantAdmin) =>
        new(
            entity.Id,
            entity.MembershipPlanId,
            entity.GymId,
            memberName,
            gymName,
            planName,
            price,
            currency,
            entity.Status,
            entity.RequestedAtUtc,
            entity.DecidedAtUtc,
            entity.DecisionReason,
            entity.Status == MembershipRequestStatus.Pending
                ? tenantAdmin ? ["approve", "reject"] : ["cancel"]
                : [],
            Convert.ToBase64String(entity.RowVersion));

    private async Task<bool> HasCurrentMembershipAsync(
        Guid tenantId,
        Guid memberUserId,
        Guid gymId,
        CancellationToken cancellationToken) =>
        await dbContext.Memberships.IgnoreQueryFilters().AnyAsync(
            x => x.TenantId == tenantId &&
                 x.MemberUserId == memberUserId &&
                 x.GymId == gymId &&
                 (x.Status == MembershipStatus.PendingPayment ||
                  x.Status == MembershipStatus.Active ||
                  x.Status == MembershipStatus.Suspended),
            cancellationToken);

    private async Task SaveWorkflowAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new ConflictException(
                "membership_conflict",
                "The membership workflow conflicts with an existing record.",
                exception);
        }
    }

    private Guid RequireUser() =>
        currentUser.UserId
        ?? throw new AuthenticationFailedException(
            "authentication_required",
            "Authentication is required.");

    private Guid RequireTenant() =>
        tenantContext.TenantId
        ?? throw new AuthorizationDeniedException(
            "tenant_required",
            "An active gym assignment is required.");

    private static void EnsureConcurrency(byte[] current, string supplied)
    {
        byte[] parsed;
        try
        {
            parsed = Convert.FromBase64String(supplied);
        }
        catch (FormatException exception)
        {
            throw new ConflictException(
                "concurrency_conflict",
                "The concurrency token is invalid. Reload the record and try again.",
                exception);
        }

        if (!current.SequenceEqual(parsed))
        {
            throw new ConflictException(
                "concurrency_conflict",
                "The record was changed by another request. Reload it and try again.");
        }
    }

    private static void ValidateRange(DateTime? fromUtc, DateTime? toUtc)
    {
        if ((fromUtc.HasValue && fromUtc.Value.Kind != DateTimeKind.Utc) ||
            (toUtc.HasValue && toUtc.Value.Kind != DateTimeKind.Utc))
        {
            throw new ApplicationRuleException(
                "utc_required",
                "Date filters must use UTC.");
        }

        if (fromUtc.HasValue && toUtc.HasValue && fromUtc > toUtc)
        {
            throw new ApplicationRuleException(
                "invalid_date_range",
                "The start date must not be after the end date.");
        }
    }

    private static NotFoundException RequestNotFound() =>
        new("membership_request_not_found", "The membership request was not found.");
}

internal sealed class MembershipService(
    IApplicationDbContext dbContext,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    ITenantMutationScope tenantMutationScope,
    IMembershipWorkflowEventRecorder eventRecorder,
    TimeProvider timeProvider) : IMembershipService
{
    public Task<PagedResult<MembershipDto>> SearchMineAsync(
        MembershipSearchRequest request,
        CancellationToken cancellationToken) =>
        SearchAsync(
            request,
            RequireUser(),
            tenantId: null,
            ignoreQueryFilters: true,
            tenantAdmin: false,
            cancellationToken: cancellationToken);

    public Task<MembershipDto> GetMineAsync(Guid id, CancellationToken cancellationToken) =>
        GetProjectedAsync(
            id,
            RequireUser(),
            tenantId: null,
            ignoreQueryFilters: true,
            tenantAdmin: false,
            cancellationToken: cancellationToken);

    public async Task<MembershipDto> CancelMineAsync(
        Guid id,
        ConcurrencyRequest request,
        CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var entity = await dbContext.Memberships.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.Id == id && x.MemberUserId == userId,
                cancellationToken)
            ?? throw MembershipNotFound();
        EnsureConcurrency(entity.RowVersion, request.ConcurrencyToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (await HasOpenCheckoutAsync(entity.Id, cancellationToken))
        {
            throw new ConflictException(
                "checkout_in_progress",
                "The open Checkout must expire before this membership can be cancelled.");
        }
        if (entity.Status == MembershipStatus.PendingPayment)
        {
            entity.CancelPendingPayment(userId, now);
        }
        else
        {
            entity.CancelByMember(userId, now);
        }
        using (tenantMutationScope.Begin(entity.TenantId))
        {
            await RecordAsync("membership.cancelled", entity, now, cancellationToken);
            await SaveAsync(cancellationToken);
        }
        return await GetMineAsync(id, cancellationToken);
    }

    public Task<PagedResult<MembershipDto>> SearchTenantAsync(
        MembershipSearchRequest request,
        CancellationToken cancellationToken) =>
        SearchAsync(
            request,
            memberUserId: null,
            tenantId: RequireTenant(),
            ignoreQueryFilters: false,
            tenantAdmin: true,
            cancellationToken);

    public Task<MembershipDto> GetTenantAsync(Guid id, CancellationToken cancellationToken) =>
        GetProjectedAsync(
            id,
            memberUserId: null,
            tenantId: RequireTenant(),
            ignoreQueryFilters: false,
            tenantAdmin: true,
            cancellationToken);

    public Task<MembershipDto> CancelAsync(
        Guid id,
        ReasonedConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            id,
            request.ConcurrencyToken,
            "membership.cancelled",
            (entity, actor, now) =>
            {
                if (entity.Status == MembershipStatus.PendingPayment)
                {
                    entity.CancelPendingPayment(actor, now);
                }
                else
                {
                    entity.CancelByStaff(actor, now, request.Reason);
                }
            },
            cancellationToken);

    private Task<bool> HasOpenCheckoutAsync(Guid membershipId, CancellationToken cancellationToken) =>
        dbContext.Payments.IgnoreQueryFilters().AnyAsync(
            x => x.Purpose == PaymentPurpose.Membership &&
                 x.TargetId == membershipId &&
                 (x.Status == PaymentStatus.Created ||
                  x.Status == PaymentStatus.Processing),
            cancellationToken);

    public Task<MembershipDto> SuspendAsync(
        Guid id,
        ReasonedConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            id,
            request.ConcurrencyToken,
            "membership.suspended",
            (entity, actor, now) => entity.Suspend(actor, now, request.Reason),
            cancellationToken);

    public Task<MembershipDto> ReactivateAsync(
        Guid id,
        ReasonedConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            id,
            request.ConcurrencyToken,
            "membership.reactivated",
            (entity, actor, now) => entity.Reactivate(actor, now, request.Reason),
            cancellationToken);

    public Task<MembershipDto> ExpireAsync(
        Guid id,
        ConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            id,
            request.ConcurrencyToken,
            "membership.expired",
            (entity, actor, now) => entity.Expire(actor, now),
            cancellationToken);

    private async Task<MembershipDto> TransitionAsync(
        Guid id,
        string concurrencyToken,
        string eventName,
        Action<Membership, Guid, DateTime> transition,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.Memberships
            .SingleOrDefaultAsync(
                x => x.Id == id && x.TenantId == RequireTenant(),
                cancellationToken)
            ?? throw MembershipNotFound();
        EnsureConcurrency(entity.RowVersion, concurrencyToken);
        if (eventName == "membership.cancelled" &&
            await HasOpenCheckoutAsync(entity.Id, cancellationToken))
        {
            throw new ConflictException(
                "checkout_in_progress",
                "The open Checkout must expire before this membership can be cancelled.");
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        transition(entity, RequireUser(), now);
        await RecordAsync(eventName, entity, now, cancellationToken);
        await SaveAsync(cancellationToken);
        return await GetTenantAsync(id, cancellationToken);
    }

    private Task<PagedResult<MembershipDto>> SearchAsync(
        MembershipSearchRequest request,
        Guid? memberUserId,
        Guid? tenantId,
        bool ignoreQueryFilters,
        bool tenantAdmin,
        CancellationToken cancellationToken)
    {
        ValidateRange(request.StartsFromUtc, request.StartsToUtc);
        ValidateRange(request.CoversFromUtc, request.CoversToUtc);
        var source = ignoreQueryFilters
            ? dbContext.Memberships.IgnoreQueryFilters().AsNoTracking()
            : dbContext.Memberships.AsNoTracking();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var query =
            from entity in source
            join member in dbContext.UserProfiles.AsNoTracking()
                on entity.MemberUserId equals member.Id
            join gym in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                on new { entity.TenantId, Id = entity.GymId }
                equals new { gym.TenantId, gym.Id }
            where (!memberUserId.HasValue || entity.MemberUserId == memberUserId) &&
                  (!tenantId.HasValue || entity.TenantId == tenantId) &&
                  (!request.Status.HasValue || entity.Status == request.Status) &&
                  (!request.MembershipPlanId.HasValue ||
                   entity.MembershipPlanId == request.MembershipPlanId) &&
                  (!request.GymId.HasValue || entity.GymId == request.GymId) &&
                  (!request.CurrentOnly ||
                   entity.Status == MembershipStatus.PendingPayment ||
                   entity.Status == MembershipStatus.Active ||
                   entity.Status == MembershipStatus.Suspended) &&
                  (!request.CoversFromUtc.HasValue ||
                   (entity.StartsAtUtc.HasValue &&
                    entity.StartsAtUtc <= request.CoversFromUtc)) &&
                  (!request.CoversToUtc.HasValue ||
                   (entity.EndsAtUtc.HasValue &&
                    entity.EndsAtUtc >= request.CoversToUtc)) &&
                  (!request.StartsFromUtc.HasValue || entity.StartsAtUtc >= request.StartsFromUtc) &&
                  (!request.StartsToUtc.HasValue || entity.StartsAtUtc <= request.StartsToUtc) &&
                  (string.IsNullOrWhiteSpace(request.Member) ||
                   member.DisplayName.Contains(request.Member.Trim()))
            orderby entity.StartsAtUtc descending, entity.Id
            select MapMembership(
                entity,
                member.DisplayName,
                gym.Name,
                tenantAdmin,
                entity.EndsAtUtc.HasValue && entity.EndsAtUtc <= now,
                dbContext.Payments.IgnoreQueryFilters()
                    .Where(x => x.Purpose == PaymentPurpose.Membership &&
                                x.TargetId == entity.Id)
                    .OrderByDescending(x => x.CreatedAtUtc)
                    .Select(x => (PaymentStatus?)x.Status)
                    .FirstOrDefault());
        return query.ToPagedResultAsync(request, cancellationToken);
    }

    private async Task<MembershipDto> GetProjectedAsync(
        Guid id,
        Guid? memberUserId,
        Guid? tenantId,
        bool ignoreQueryFilters,
        bool tenantAdmin,
        CancellationToken cancellationToken)
    {
        var source = ignoreQueryFilters
            ? dbContext.Memberships.IgnoreQueryFilters().AsNoTracking()
            : dbContext.Memberships.AsNoTracking();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return await (
                from entity in source
                join member in dbContext.UserProfiles.AsNoTracking()
                    on entity.MemberUserId equals member.Id
                join gym in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                    on new { entity.TenantId, Id = entity.GymId }
                    equals new { gym.TenantId, gym.Id }
                where entity.Id == id &&
                      (!memberUserId.HasValue || entity.MemberUserId == memberUserId) &&
                      (!tenantId.HasValue || entity.TenantId == tenantId)
                select MapMembership(
                    entity,
                    member.DisplayName,
                    gym.Name,
                    tenantAdmin,
                    entity.EndsAtUtc.HasValue && entity.EndsAtUtc <= now,
                    dbContext.Payments.IgnoreQueryFilters()
                        .Where(x => x.Purpose == PaymentPurpose.Membership &&
                                    x.TargetId == entity.Id)
                        .OrderByDescending(x => x.CreatedAtUtc)
                        .Select(x => (PaymentStatus?)x.Status)
                        .FirstOrDefault()))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw MembershipNotFound();
    }

    private static MembershipDto MapMembership(
        Membership entity,
        string memberName,
        string gymName,
        bool tenantAdmin,
        bool canExpire,
        PaymentStatus? paymentStatus) =>
        new(
            entity.Id,
            entity.MembershipPlanId,
            entity.MembershipRequestId,
            entity.GymId,
            memberName,
            gymName,
            entity.PlanName,
            entity.Price,
            entity.Currency,
            entity.StartsAtUtc,
            entity.EndsAtUtc,
            entity.Status,
            entity.PaymentId,
            paymentStatus,
            entity.PaymentId.HasValue,
            entity.StatusChangedAtUtc,
            entity.StatusReason,
            AllowedActions(
                entity.Status,
                entity.PaymentId.HasValue,
                paymentStatus,
                tenantAdmin,
                canExpire),
            Convert.ToBase64String(entity.RowVersion));

    private static IReadOnlyList<string> AllowedActions(
        MembershipStatus status,
        bool isPaid,
        PaymentStatus? paymentStatus,
        bool tenantAdmin,
        bool canExpire) =>
        status switch
        {
            MembershipStatus.PendingPayment
                when paymentStatus is PaymentStatus.Created or PaymentStatus.Processing =>
                ["pay"],
            MembershipStatus.PendingPayment => ["pay", "cancel"],
            MembershipStatus.Active when tenantAdmin && canExpire =>
                isPaid ? ["suspend", "expire"] : ["cancel", "suspend", "expire"],
            MembershipStatus.Active when tenantAdmin =>
                isPaid ? ["suspend"] : ["cancel", "suspend"],
            MembershipStatus.Active when isPaid => [],
            MembershipStatus.Active => ["cancel"],
            MembershipStatus.Suspended when tenantAdmin => ["reactivate"],
            _ => [],
        };

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new ConflictException(
                "membership_conflict",
                "The membership workflow conflicts with an existing record.",
                exception);
        }
    }

    private Task RecordAsync(
        string name,
        Membership entity,
        DateTime now,
        CancellationToken cancellationToken) =>
        eventRecorder.RecordAsync(new MembershipWorkflowEventIntent(
            name,
            entity.TenantId,
            entity.MemberUserId,
            entity.Id,
            now),
            cancellationToken);

    private Guid RequireUser() =>
        currentUser.UserId
        ?? throw new AuthenticationFailedException(
            "authentication_required",
            "Authentication is required.");

    private Guid RequireTenant() =>
        tenantContext.TenantId
        ?? throw new AuthorizationDeniedException(
            "tenant_required",
            "An active gym assignment is required.");

    private static void EnsureConcurrency(byte[] current, string supplied)
    {
        byte[] parsed;
        try
        {
            parsed = Convert.FromBase64String(supplied);
        }
        catch (FormatException exception)
        {
            throw new ConflictException(
                "concurrency_conflict",
                "The concurrency token is invalid. Reload the record and try again.",
                exception);
        }

        if (!current.SequenceEqual(parsed))
        {
            throw new ConflictException(
                "concurrency_conflict",
                "The record was changed by another request. Reload it and try again.");
        }
    }

    private static void ValidateRange(DateTime? fromUtc, DateTime? toUtc)
    {
        if ((fromUtc.HasValue && fromUtc.Value.Kind != DateTimeKind.Utc) ||
            (toUtc.HasValue && toUtc.Value.Kind != DateTimeKind.Utc))
        {
            throw new ApplicationRuleException(
                "utc_required",
                "Date filters must use UTC.");
        }

        if (fromUtc.HasValue && toUtc.HasValue && fromUtc > toUtc)
        {
            throw new ApplicationRuleException(
                "invalid_date_range",
                "The start date must not be after the end date.");
        }
    }

    private static NotFoundException MembershipNotFound() =>
        new("membership_not_found", "The membership was not found.");
}
