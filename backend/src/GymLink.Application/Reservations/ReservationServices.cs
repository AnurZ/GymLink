using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Identity;
using GymLink.Domain.Reservations;
using GymLink.Domain.Trainers;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Reservations;

internal sealed class AvailabilityService(
    IApplicationDbContext dbContext,
    IApplicationTransaction transaction,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IReservationWorkflowEventRecorder eventRecorder,
    TimeProvider timeProvider) : IAvailabilityService
{
    public async Task<PagedResult<AvailabilityDto>> SearchTenantAsync(
        AvailabilitySearchRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        ValidateRange(request.FromUtc, request.ToUtc);
        var query = dbContext.TrainerAvailabilitySlots.AsNoTracking();
        if (tenantContext.TenantRole == RoleNames.Trainer)
        {
            var ownTrainerId = (await ResolveWritableTrainerAsync(Guid.Empty, cancellationToken)).Id;
            query = query.Where(x => x.TrainerProfileId == ownTrainerId);
        }
        if (request.TrainerProfileId.HasValue)
        {
            query = query.Where(x => x.TrainerProfileId == request.TrainerProfileId);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.Status == request.Status);
        }

        query = ApplyRange(query, request.FromUtc, request.ToUtc);
        return await query.OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id)
            .Select(x => ToDto(x))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<PagedResult<AvailabilityDto>> SearchPublicAsync(
        Guid trainerId,
        PublicAvailabilitySearchRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        ValidateRange(request.FromUtc, request.ToUtc);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var trainer = await dbContext.TrainerProfiles.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == trainerId && x.IsActive, cancellationToken)
            ?? throw new NotFoundException("trainer_not_found", "The trainer was not found.");
        var visible = await dbContext.Tenants.AsNoTracking()
                .AnyAsync(x => x.Id == trainer.TenantId && x.Status == TenantStatus.Active, cancellationToken) &&
            await dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.TenantId == trainer.TenantId && x.IsPubliclyVisible, cancellationToken);
        if (!visible)
        {
            throw new NotFoundException("trainer_not_found", "The trainer was not found.");
        }

        int? offeringDuration = null;
        if (request.TrainerServiceOfferingId.HasValue)
        {
            offeringDuration = await dbContext.TrainerServiceOfferings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(
                    x => x.Id == request.TrainerServiceOfferingId &&
                         x.TrainerProfileId == trainerId &&
                         x.IsActive)
                .Select(x => (int?)x.DurationMinutes)
                .SingleOrDefaultAsync(cancellationToken);
            if (!offeringDuration.HasValue)
            {
                throw new NotFoundException("offering_not_found", "The trainer offering was not found.");
            }
        }

        var query = dbContext.TrainerAvailabilitySlots.IgnoreQueryFilters().AsNoTracking()
            .Where(x =>
                x.TenantId == trainer.TenantId &&
                x.TrainerProfileId == trainerId &&
                x.Status == AvailabilitySlotStatus.Available &&
                x.StartsAtUtc > now);
        if (offeringDuration.HasValue)
        {
            query = query.Where(
                x => x.EndsAtUtc >= x.StartsAtUtc.AddMinutes(offeringDuration.Value));
        }
        query = ApplyRange(query, request.FromUtc, request.ToUtc);
        return await query.OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id)
            .Select(x => ToDto(x))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<AvailabilityDto> CreateAsync(
        CreateAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = RequireUser();
        var tenantId = RequireTenant();
        var trainer = await ResolveWritableTrainerAsync(
            request.TrainerProfileId,
            cancellationToken);
        ValidateFutureRange(request.StartsAtUtc, request.EndsAtUtc);
        ValidateManagedStatus(request.Status);
        TrainerAvailabilitySlot slot;
        try
        {
            slot = await transaction.ExecuteSerializableAsync(async ct =>
            {
                await EnsureNoOverlapAsync(
                    tenantId,
                    trainer.Id,
                    request.StartsAtUtc,
                    request.EndsAtUtc,
                    null,
                    ct);
                var entity = new TrainerAvailabilitySlot(
                    tenantId,
                    trainer.Id,
                    request.StartsAtUtc,
                    request.EndsAtUtc);
                if (request.Status == AvailabilitySlotStatus.Unavailable)
                {
                    entity.Update(request.StartsAtUtc, request.EndsAtUtc, request.Status);
                }

                dbContext.TrainerAvailabilitySlots.Add(entity);
                AddOverrideAudit(actorId, trainer, entity.Id, "availability.created");
                await dbContext.SaveChangesAsync(ct);
                return entity;
            }, cancellationToken);
        }
        catch (InvalidOperationException exception) when (ContainsDbUpdateException(exception))
        {
            throw new ConflictException(
                "availability_overlap",
                "The availability interval overlaps another trainer slot.",
                exception);
        }
        eventRecorder.Record(new(
            "availability.changed",
            tenantId,
            actorId,
            slot.Id,
            timeProvider.GetUtcNow().UtcDateTime));
        return ToDto(slot);
    }

    public async Task<AvailabilityDto> UpdateAsync(
        Guid id,
        UpdateAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = RequireUser();
        ValidateFutureRange(request.StartsAtUtc, request.EndsAtUtc);
        ValidateManagedStatus(request.Status);
        TrainerAvailabilitySlot slot;
        try
        {
            slot = await transaction.ExecuteSerializableAsync(async ct =>
            {
                var entity = await dbContext.TrainerAvailabilitySlots
                    .SingleOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw SlotNotFound();
                var writableTrainer = await ResolveWritableTrainerAsync(
                    entity.TrainerProfileId,
                    ct);
                EnsureToken(entity.RowVersion, request.ConcurrencyToken);
                await EnsureNoOverlapAsync(
                    RequireTenant(),
                    entity.TrainerProfileId,
                    request.StartsAtUtc,
                    request.EndsAtUtc,
                    entity.Id,
                    ct);
                entity.Update(request.StartsAtUtc, request.EndsAtUtc, request.Status);
                AddOverrideAudit(actorId, writableTrainer, entity.Id, "availability.updated");
                await dbContext.SaveChangesAsync(ct);
                return entity;
            }, cancellationToken);
        }
        catch (InvalidOperationException exception) when (ContainsDbUpdateException(exception))
        {
            throw new ConflictException(
                "availability_overlap",
                "The availability interval overlaps another trainer slot.",
                exception);
        }
        eventRecorder.Record(new(
            "availability.changed",
            slot.TenantId,
            actorId,
            slot.Id,
            timeProvider.GetUtcNow().UtcDateTime));
        return ToDto(slot);
    }

    public async Task<AvailabilityDto> CancelAsync(
        Guid id,
        ReservationConcurrencyRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = RequireUser();
        var slot = await dbContext.TrainerAvailabilitySlots
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw SlotNotFound();
        var trainer = await ResolveWritableTrainerAsync(slot.TrainerProfileId, cancellationToken);
        EnsureToken(slot.RowVersion, request.ConcurrencyToken);
        slot.Cancel();
        AddOverrideAudit(actorId, trainer, slot.Id, "availability.cancelled");
        await SaveAsync(cancellationToken);
        eventRecorder.Record(new(
            "availability.changed",
            slot.TenantId,
            actorId,
            slot.Id,
            timeProvider.GetUtcNow().UtcDateTime));
        return ToDto(slot);
    }

    private async Task<TrainerProfile> ResolveWritableTrainerAsync(
        Guid requestedTrainerId,
        CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var actorId = RequireUser();
        if (tenantContext.TenantRole == RoleNames.Trainer)
        {
            var own = await dbContext.TrainerProfiles
                .SingleOrDefaultAsync(x => x.UserId == actorId && x.IsActive, cancellationToken)
                ?? throw new AuthorizationDeniedException(
                    "trainer_profile_required",
                    "An active trainer profile is required.");
            if (requestedTrainerId != Guid.Empty && requestedTrainerId != own.Id)
            {
                throw new AuthorizationDeniedException(
                    "trainer_ownership_required",
                    "Trainers may manage only their own availability.");
            }

            return own;
        }

        if (tenantContext.TenantRole != RoleNames.GymAdmin)
        {
            throw new AuthorizationDeniedException();
        }

        return await dbContext.TrainerProfiles.SingleOrDefaultAsync(
                x => x.Id == requestedTrainerId &&
                     x.TenantId == tenantId &&
                     x.IsActive,
                cancellationToken)
            ?? throw new NotFoundException("trainer_not_found", "The trainer was not found.");
    }

    private async Task EnsureNoOverlapAsync(
        Guid tenantId,
        Guid trainerId,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        if (await dbContext.TrainerAvailabilitySlots.AnyAsync(
                x => x.TenantId == tenantId &&
                     x.TrainerProfileId == trainerId &&
                     x.Status != AvailabilitySlotStatus.Cancelled &&
                     (!excludedId.HasValue || x.Id != excludedId) &&
                     x.StartsAtUtc < endsAtUtc &&
                     startsAtUtc < x.EndsAtUtc,
                cancellationToken))
        {
            throw new ConflictException(
                "availability_overlap",
                "The availability interval overlaps another trainer slot.");
        }
    }

    private void AddOverrideAudit(
        Guid actorId,
        TrainerProfile trainer,
        Guid slotId,
        string action)
    {
        if (tenantContext.TenantRole != RoleNames.GymAdmin)
        {
            return;
        }

        dbContext.SecurityAuditRecords.Add(new SecurityAuditRecord
        {
            ActorUserId = actorId,
            TargetUserId = trainer.UserId,
            TargetTenantId = trainer.TenantId,
            Action = action,
            TargetType = nameof(TrainerAvailabilitySlot),
            TargetId = slotId,
            Reason = "GymAdmin availability override.",
            OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        });
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConflictException(
                "concurrency_conflict",
                "The availability slot changed. Reload it and try again.",
                exception);
        }
    }

    private void ValidateFutureRange(DateTime start, DateTime end)
    {
        ValidateRange(start, end);
        if (start <= timeProvider.GetUtcNow().UtcDateTime)
        {
            throw new ApplicationRuleException(
                "availability_must_be_future",
                "Availability must start in the future.");
        }
    }

    private static void ValidateManagedStatus(AvailabilitySlotStatus status)
    {
        if (status is not AvailabilitySlotStatus.Available and
            not AvailabilitySlotStatus.Unavailable)
        {
            throw new ApplicationRuleException(
                "availability_status_invalid",
                "Only Available or Unavailable can be selected by staff.");
        }
    }

    private static IQueryable<TrainerAvailabilitySlot> ApplyRange(
        IQueryable<TrainerAvailabilitySlot> query,
        DateTime? from,
        DateTime? to)
    {
        if (from.HasValue)
        {
            query = query.Where(x => x.EndsAtUtc > from);
        }

        if (to.HasValue)
        {
            query = query.Where(x => x.StartsAtUtc < to);
        }

        return query;
    }

    private static void ValidateRange(DateTime? from, DateTime? to)
    {
        if (from.HasValue && from.Value.Kind != DateTimeKind.Utc ||
            to.HasValue && to.Value.Kind != DateTimeKind.Utc)
        {
            throw new ApplicationRuleException("utc_required", "Date filters must use UTC.");
        }

        if (from.HasValue && to.HasValue && to <= from)
        {
            throw new ApplicationRuleException("invalid_time_range", "The end must follow the start.");
        }
    }

    private static AvailabilityDto ToDto(TrainerAvailabilitySlot slot) =>
        new(
            slot.Id,
            slot.TrainerProfileId,
            slot.StartsAtUtc,
            slot.EndsAtUtc,
            slot.Status,
            Convert.ToBase64String(slot.RowVersion));

    private Guid RequireUser() =>
        currentUser.UserId ??
        throw new AuthorizationDeniedException("current_user_required", "A current user is required.");

    private Guid RequireTenant() =>
        tenantContext.TenantId ??
        throw new AuthorizationDeniedException("tenant_required", "A selected tenant is required.");

    private static NotFoundException SlotNotFound() =>
        new("availability_not_found", "The availability slot was not found.");

    private static void EnsureToken(byte[] rowVersion, string token)
    {
        byte[] supplied;
        try
        {
            supplied = Convert.FromBase64String(token);
        }
        catch (FormatException)
        {
            throw new ApplicationRuleException(
                "concurrency_token_invalid",
                "The concurrency token is invalid.");
        }

        if (!rowVersion.SequenceEqual(supplied))
        {
            throw new ConflictException(
                "concurrency_conflict",
                "The record changed. Reload it and try again.");
        }
    }

    private static bool ContainsDbUpdateException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is DbUpdateException)
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class ReservationService(
    IApplicationDbContext dbContext,
    IApplicationTransaction transaction,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    ITenantMutationScope tenantMutationScope,
    IReservationWorkflowEventRecorder eventRecorder,
    TimeProvider timeProvider) : IReservationService
{
    public async Task<ReservationDto> CreateAsync(
        CreateReservationRequest request,
        CancellationToken cancellationToken)
    {
        var memberId = RequireUser();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        AppointmentReservation reservation;
        try
        {
            reservation = await transaction.ExecuteSerializableAsync(async ct =>
            {
                var target = await (
                        from offering in dbContext.TrainerServiceOfferings.IgnoreQueryFilters()
                        join trainer in dbContext.TrainerProfiles.IgnoreQueryFilters()
                            on new { offering.TenantId, Id = offering.TrainerProfileId }
                            equals new { trainer.TenantId, trainer.Id }
                        join slot in dbContext.TrainerAvailabilitySlots.IgnoreQueryFilters()
                            on new { offering.TenantId, Id = request.AvailabilitySlotId }
                            equals new { slot.TenantId, slot.Id }
                        join gym in dbContext.Gyms.IgnoreQueryFilters()
                            on offering.TenantId equals gym.TenantId
                        join tenant in dbContext.Tenants on offering.TenantId equals tenant.Id
                        where offering.Id == request.TrainerServiceOfferingId &&
                              offering.IsActive &&
                              trainer.IsActive &&
                              slot.TrainerProfileId == trainer.Id &&
                              slot.Status == AvailabilitySlotStatus.Available &&
                              gym.IsPubliclyVisible &&
                              tenant.Status == TenantStatus.Active
                        select new { Offering = offering, Trainer = trainer, Slot = slot, Gym = gym })
                    .SingleOrDefaultAsync(ct)
                    ?? throw new NotFoundException(
                        "bookable_slot_not_found",
                        "The selected offering and availability slot are not bookable.");
                var endsAtUtc = target.Slot.StartsAtUtc.AddMinutes(target.Offering.DurationMinutes);
                if (target.Slot.StartsAtUtc <= now || endsAtUtc > target.Slot.EndsAtUtc)
                {
                    throw new ConflictException(
                        "slot_duration_mismatch",
                        "The offering does not fit within the selected availability slot.");
                }

                var membership = await dbContext.Memberships.IgnoreQueryFilters()
                    .SingleOrDefaultAsync(
                        x => x.TenantId == target.Offering.TenantId &&
                             x.GymId == target.Gym.Id &&
                             x.MemberUserId == memberId &&
                             x.Status == MembershipStatus.Active &&
                             x.StartsAtUtc <= target.Slot.StartsAtUtc &&
                             x.EndsAtUtc >= endsAtUtc,
                        ct)
                    ?? throw new ConflictException(
                        "covering_membership_required",
                        "An active membership covering the appointment is required.");
                var activeStatuses = new[] { ReservationStatus.Pending, ReservationStatus.Confirmed };
                var overlaps = await dbContext.AppointmentReservations.IgnoreQueryFilters()
                    .AnyAsync(
                        x => activeStatuses.Contains(x.Status) &&
                             x.StartsAtUtc < endsAtUtc &&
                             target.Slot.StartsAtUtc < x.EndsAtUtc &&
                             (x.TrainerProfileId == target.Trainer.Id ||
                              x.MemberUserId == memberId),
                        ct);
                if (overlaps)
                {
                    throw new ConflictException(
                        "reservation_overlap",
                        "The trainer or Member already has an overlapping reservation.");
                }

                target.Slot.Reserve();
                var entity = new AppointmentReservation(
                    target.Offering.TenantId,
                    memberId,
                    target.Trainer.Id,
                    target.Offering.Id,
                    target.Slot.Id,
                    membership.Id,
                    target.Slot.StartsAtUtc,
                    target.Offering.DurationMinutes,
                    target.Offering.Price,
                    target.Offering.Currency);
                using (tenantMutationScope.Begin(target.Offering.TenantId))
                {
                    dbContext.AppointmentReservations.Add(entity);
                    await dbContext.SaveChangesAsync(ct);
                }

                return entity;
            }, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new ConflictException(
                "reservation_conflict",
                "The selected time became unavailable. Reload availability and try again.",
                exception);
        }
        catch (InvalidOperationException exception) when (ContainsDbUpdateException(exception))
        {
            throw new ConflictException(
                "reservation_conflict",
                "The selected time became unavailable. Reload availability and try again.",
                exception);
        }

        eventRecorder.Record(new(
            "reservation.created",
            reservation.TenantId,
            memberId,
            reservation.Id,
            now));
        return await GetMineAsync(reservation.Id, cancellationToken);
    }

    public Task<PagedResult<ReservationDto>> SearchMineAsync(
        ReservationSearchRequest request,
        CancellationToken cancellationToken) =>
        SearchAsync(request, null, RequireUser(), null, null, true, cancellationToken);

    public Task<ReservationDto> GetMineAsync(Guid id, CancellationToken cancellationToken) =>
        GetAsync(id, RequireUser(), null, null, true, cancellationToken);

    public async Task<ReservationDto> CancelMineAsync(
        Guid id,
        ReservationConcurrencyRequest request,
        CancellationToken cancellationToken)
    {
        var actor = RequireUser();
        var entity = await dbContext.AppointmentReservations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == id && x.MemberUserId == actor, cancellationToken)
            ?? throw ReservationNotFound();
        EnsureToken(entity.RowVersion, request.ConcurrencyToken);
        var slot = await dbContext.TrainerAvailabilitySlots.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == entity.AvailabilitySlotId, cancellationToken);
        entity.CancelByMember(actor, timeProvider.GetUtcNow().UtcDateTime);
        slot.Release();
        using (tenantMutationScope.Begin(entity.TenantId))
        {
            await SaveAsync(cancellationToken);
        }

        RecordStatus(entity, actor);
        return await GetMineAsync(id, cancellationToken);
    }

    public async Task<PagedResult<ReservationDto>> SearchTrainerAsync(
        ReservationSearchRequest request,
        CancellationToken cancellationToken)
    {
        var trainer = await RequireOwnTrainerAsync(cancellationToken);
        return await SearchAsync(request, null, null, trainer.Id, RequireTenant(), false, cancellationToken);
    }

    public async Task<ReservationDto> GetTrainerAsync(Guid id, CancellationToken cancellationToken)
    {
        var trainer = await RequireOwnTrainerAsync(cancellationToken);
        return await GetAsync(id, null, trainer.Id, RequireTenant(), false, cancellationToken);
    }

    public Task<PagedResult<ReservationDto>> SearchTenantAsync(
        ReservationSearchRequest request,
        CancellationToken cancellationToken) =>
        SearchAsync(request, null, null, request.TrainerProfileId, RequireTenant(), false, cancellationToken);

    public Task<ReservationDto> GetTenantAsync(Guid id, CancellationToken cancellationToken) =>
        GetAsync(id, null, null, RequireTenant(), false, cancellationToken);

    public Task<ReservationDto> ConfirmAsync(
        Guid id,
        ReservationConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        TransitionAsync(id, request.ConcurrencyToken, null, ReservationAction.Confirm, cancellationToken);

    public Task<ReservationDto> CancelStaffAsync(
        Guid id,
        StaffCancellationRequest request,
        CancellationToken cancellationToken) =>
        TransitionAsync(id, request.ConcurrencyToken, request.Reason, ReservationAction.Cancel, cancellationToken);

    public Task<ReservationDto> CompleteAsync(
        Guid id,
        ReservationConcurrencyRequest request,
        CancellationToken cancellationToken) =>
        TransitionAsync(id, request.ConcurrencyToken, null, ReservationAction.Complete, cancellationToken);

    private async Task<ReservationDto> TransitionAsync(
        Guid id,
        string concurrencyToken,
        string? reason,
        ReservationAction action,
        CancellationToken cancellationToken)
    {
        var actor = RequireUser();
        var tenantId = RequireTenant();
        var entity = await dbContext.AppointmentReservations
            .SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, cancellationToken)
            ?? throw ReservationNotFound();
        await EnsureStaffOwnershipAsync(entity, action, cancellationToken);
        EnsureToken(entity.RowVersion, concurrencyToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (action == ReservationAction.Confirm)
        {
            var stillEligible = await dbContext.Memberships.AnyAsync(
                    x => x.Id == entity.MembershipId &&
                         x.Status == MembershipStatus.Active &&
                         x.StartsAtUtc <= entity.StartsAtUtc &&
                         x.EndsAtUtc >= entity.EndsAtUtc,
                    cancellationToken) &&
                await dbContext.TrainerAvailabilitySlots.AnyAsync(
                    x => x.Id == entity.AvailabilitySlotId &&
                         x.Status == AvailabilitySlotStatus.Reserved,
                    cancellationToken);
            if (!stillEligible)
            {
                throw new ConflictException(
                    "reservation_prerequisite_invalid",
                    "The reservation prerequisites are no longer valid.");
            }

            entity.Confirm(actor, now);
        }
        else if (action == ReservationAction.Complete)
        {
            entity.Complete(actor, now);
        }
        else
        {
            entity.CancelByStaff(actor, now, reason ?? string.Empty);
            var slot = await dbContext.TrainerAvailabilitySlots.SingleAsync(
                x => x.Id == entity.AvailabilitySlotId,
                cancellationToken);
            slot.Release();
        }

        await SaveAsync(cancellationToken);
        RecordStatus(entity, actor);
        return tenantContext.TenantRole == RoleNames.Trainer
            ? await GetTrainerAsync(id, cancellationToken)
            : await GetTenantAsync(id, cancellationToken);
    }

    private async Task EnsureStaffOwnershipAsync(
        AppointmentReservation entity,
        ReservationAction action,
        CancellationToken cancellationToken)
    {
        if (tenantContext.TenantRole == RoleNames.GymAdmin)
        {
            if (action == ReservationAction.Complete)
            {
                throw new AuthorizationDeniedException(
                    "trainer_completion_required",
                    "Only the assigned Trainer may complete a reservation.");
            }

            return;
        }

        var trainer = await RequireOwnTrainerAsync(cancellationToken);
        if (trainer.Id != entity.TrainerProfileId)
        {
            throw ReservationNotFound();
        }
    }

    private async Task<TrainerProfile> RequireOwnTrainerAsync(CancellationToken cancellationToken)
    {
        var user = RequireUser();
        return await dbContext.TrainerProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == user && x.IsActive, cancellationToken)
            ?? throw new AuthorizationDeniedException(
                "trainer_profile_required",
                "An active trainer profile is required.");
    }

    private Task<PagedResult<ReservationDto>> SearchAsync(
        ReservationSearchRequest request,
        Guid? reservationId,
        Guid? memberId,
        Guid? trainerId,
        Guid? tenantId,
        bool ignoreFilters,
        CancellationToken cancellationToken)
    {
        request.Validate();
        ValidateRange(request.FromUtc, request.ToUtc);
        var reservations = ignoreFilters
            ? dbContext.AppointmentReservations.IgnoreQueryFilters().AsNoTracking()
            : dbContext.AppointmentReservations.AsNoTracking();
        var trainers = ignoreFilters
            ? dbContext.TrainerProfiles.IgnoreQueryFilters().AsNoTracking()
            : dbContext.TrainerProfiles.AsNoTracking();
        var memberships = ignoreFilters
            ? dbContext.Memberships.IgnoreQueryFilters().AsNoTracking()
            : dbContext.Memberships.AsNoTracking();
        var gyms = ignoreFilters
            ? dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
            : dbContext.Gyms.AsNoTracking();
        var offerings = ignoreFilters
            ? dbContext.TrainerServiceOfferings.IgnoreQueryFilters().AsNoTracking()
            : dbContext.TrainerServiceOfferings.AsNoTracking();
        var reviews = ignoreFilters
            ? dbContext.Reviews.IgnoreQueryFilters().AsNoTracking()
            : dbContext.Reviews.AsNoTracking();
        var query =
            from reservation in reservations
            join trainer in trainers on reservation.TrainerProfileId equals trainer.Id
            join trainerUser in dbContext.UserProfiles.AsNoTracking() on trainer.UserId equals trainerUser.Id
            join member in dbContext.UserProfiles.AsNoTracking() on reservation.MemberUserId equals member.Id
            join membership in memberships on reservation.MembershipId equals membership.Id
            join gym in gyms on membership.GymId equals gym.Id
            join offering in offerings on reservation.TrainerServiceOfferingId equals offering.Id
            select new
            {
                Reservation = reservation,
                TrainerName = trainerUser.DisplayName,
                MemberName = member.DisplayName,
                GymName = gym.Name,
                OfferingName = offering.Name,
                HasReview = reviews.Any(x => x.ReservationId == reservation.Id),
            };
        if (reservationId.HasValue)
        {
            query = query.Where(x => x.Reservation.Id == reservationId);
        }

        if (memberId.HasValue)
        {
            query = query.Where(x => x.Reservation.MemberUserId == memberId);
        }

        if (trainerId.HasValue)
        {
            query = query.Where(x => x.Reservation.TrainerProfileId == trainerId);
        }

        if (tenantId.HasValue)
        {
            query = query.Where(x => x.Reservation.TenantId == tenantId);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.Reservation.Status == request.Status);
        }

        if (request.FromUtc.HasValue)
        {
            query = query.Where(x => x.Reservation.EndsAtUtc > request.FromUtc);
        }

        if (request.ToUtc.HasValue)
        {
            query = query.Where(x => x.Reservation.StartsAtUtc < request.ToUtc);
        }

        return query.OrderByDescending(x => x.Reservation.StartsAtUtc)
            .ThenBy(x => x.Reservation.Id)
            .Select(x => new ReservationDto(
                x.Reservation.Id,
                x.Reservation.TrainerProfileId,
                x.TrainerName,
                x.MemberName,
                x.GymName,
                x.OfferingName,
                x.Reservation.StartsAtUtc,
                x.Reservation.EndsAtUtc,
                x.Reservation.DurationMinutes,
                x.Reservation.Price,
                x.Reservation.Currency,
                x.Reservation.Status,
                x.Reservation.CancellationReason,
                x.Reservation.Status == ReservationStatus.Completed && !x.HasReview,
                Convert.ToBase64String(x.Reservation.RowVersion)))
            .ToPagedResultAsync(request, cancellationToken);
    }

    private async Task<ReservationDto> GetAsync(
        Guid id,
        Guid? memberId,
        Guid? trainerId,
        Guid? tenantId,
        bool ignoreFilters,
        CancellationToken cancellationToken)
    {
        var result = await SearchAsync(
            new ReservationSearchRequest { Page = 1, PageSize = 100 },
            id,
            memberId,
            trainerId,
            tenantId,
            ignoreFilters,
            cancellationToken);
        return result.Items.SingleOrDefault(x => x.Id == id)
            ?? throw ReservationNotFound();
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConflictException(
                "concurrency_conflict",
                "The reservation changed. Reload it and try again.",
                exception);
        }
    }

    private void RecordStatus(AppointmentReservation entity, Guid actor) =>
        eventRecorder.Record(new(
            "reservation.status_changed",
            entity.TenantId,
            actor,
            entity.Id,
            timeProvider.GetUtcNow().UtcDateTime));

    private Guid RequireUser() =>
        currentUser.UserId ??
        throw new AuthorizationDeniedException("current_user_required", "A current user is required.");

    private Guid RequireTenant() =>
        tenantContext.TenantId ??
        throw new AuthorizationDeniedException("tenant_required", "A selected tenant is required.");

    private static NotFoundException ReservationNotFound() =>
        new("reservation_not_found", "The reservation was not found.");

    private static void ValidateRange(DateTime? from, DateTime? to)
    {
        if (from.HasValue && from.Value.Kind != DateTimeKind.Utc ||
            to.HasValue && to.Value.Kind != DateTimeKind.Utc)
        {
            throw new ApplicationRuleException("utc_required", "Date filters must use UTC.");
        }

        if (from.HasValue && to.HasValue && to <= from)
        {
            throw new ApplicationRuleException("invalid_time_range", "The end must follow the start.");
        }
    }

    private static void EnsureToken(byte[] rowVersion, string token)
    {
        byte[] supplied;
        try
        {
            supplied = Convert.FromBase64String(token);
        }
        catch (FormatException)
        {
            throw new ApplicationRuleException(
                "concurrency_token_invalid",
                "The concurrency token is invalid.");
        }

        if (!rowVersion.SequenceEqual(supplied))
        {
            throw new ConflictException("concurrency_conflict", "The record changed. Reload it and try again.");
        }
    }

    private static bool ContainsDbUpdateException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is DbUpdateException)
            {
                return true;
            }
        }

        return false;
    }

    private enum ReservationAction
    {
        Confirm,
        Cancel,
        Complete,
    }
}

internal sealed class ReviewService(
    IApplicationDbContext dbContext,
    IApplicationTransaction transaction,
    ICurrentUser currentUser,
    ITenantMutationScope tenantMutationScope,
    IReservationWorkflowEventRecorder eventRecorder,
    TimeProvider timeProvider) : IReviewService
{
    public async Task<ReviewDto> CreateTrainerReviewAsync(
        Guid reservationId,
        CreateReviewRequest request,
        CancellationToken cancellationToken)
    {
        var memberId = RequireUser();
        var review = await transaction.ExecuteAsync(async ct =>
        {
            var reservation = await dbContext.AppointmentReservations.IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    x => x.Id == reservationId &&
                         x.MemberUserId == memberId &&
                         x.Status == ReservationStatus.Completed,
                    ct)
                ?? throw new NotFoundException(
                    "review_eligible_reservation_not_found",
                    "A completed reservation eligible for review was not found.");
            if (await dbContext.Reviews.IgnoreQueryFilters()
                .AnyAsync(x => x.ReservationId == reservationId, ct))
            {
                throw new ConflictException("review_exists", "This reservation already has a review.");
            }

            var trainer = await dbContext.TrainerProfiles.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == reservation.TrainerProfileId, ct);
            var entity = new Review(
                reservation.TenantId,
                reservation.Id,
                memberId,
                trainer.Id,
                request.Rating,
                request.Comment);
            trainer.AddReview(request.Rating);
            using (tenantMutationScope.Begin(reservation.TenantId))
            {
                dbContext.Reviews.Add(entity);
                await dbContext.SaveChangesAsync(ct);
            }

            return entity;
        }, cancellationToken);
        Record("review.trainer_created", review.TenantId, review.Id);
        return ToDto(review);
    }

    public async Task<PagedResult<ReviewDto>> SearchTrainerReviewsAsync(
        Guid trainerId,
        ReviewSearchRequest request,
        CancellationToken cancellationToken)
    {
        await EnsurePublicTrainerAsync(trainerId, cancellationToken);
        return await dbContext.Reviews.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TrainerProfileId == trainerId)
            .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Select(x => ToDto(x))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<ReviewDto> CreateGymReviewAsync(
        Guid gymId,
        CreateReviewRequest request,
        CancellationToken cancellationToken)
    {
        var memberId = RequireUser();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var review = await transaction.ExecuteAsync(async ct =>
        {
            var gym = await dbContext.Gyms.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.Id == gymId && x.IsPubliclyVisible, ct)
                ?? throw new NotFoundException("gym_not_found", "The gym was not found.");
            var tenantActive = await dbContext.Tenants.AsNoTracking()
                .AnyAsync(x => x.Id == gym.TenantId && x.Status == TenantStatus.Active, ct);
            var membershipActive = await dbContext.Memberships.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(
                    x => x.GymId == gymId &&
                         x.MemberUserId == memberId &&
                         x.Status == MembershipStatus.Active &&
                         x.StartsAtUtc <= now &&
                         x.EndsAtUtc > now,
                    ct);
            if (!tenantActive || !membershipActive)
            {
                throw new ConflictException(
                    "active_membership_required",
                    "An active membership in the gym is required to create a review.");
            }

            if (await dbContext.GymReviews.IgnoreQueryFilters()
                .AnyAsync(x => x.GymId == gymId && x.ReviewerUserId == memberId, ct))
            {
                throw new ConflictException("gym_review_exists", "You have already reviewed this gym.");
            }

            var entity = new GymReview(
                gym.TenantId,
                gym.Id,
                memberId,
                request.Rating,
                request.Comment);
            gym.AddReview(request.Rating);
            using (tenantMutationScope.Begin(gym.TenantId))
            {
                dbContext.GymReviews.Add(entity);
                await dbContext.SaveChangesAsync(ct);
            }

            return entity;
        }, cancellationToken);
        Record("review.gym_created", review.TenantId, review.Id);
        return ToDto(review);
    }

    public async Task<PagedResult<ReviewDto>> SearchGymReviewsAsync(
        Guid gymId,
        ReviewSearchRequest request,
        CancellationToken cancellationToken)
    {
        var gym = await dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == gymId && x.IsPubliclyVisible, cancellationToken)
            ?? throw new NotFoundException("gym_not_found", "The gym was not found.");
        if (!await dbContext.Tenants.AsNoTracking()
            .AnyAsync(x => x.Id == gym.TenantId && x.Status == TenantStatus.Active, cancellationToken))
        {
            throw new NotFoundException("gym_not_found", "The gym was not found.");
        }

        return await dbContext.GymReviews.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.GymId == gymId)
            .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Select(x => ToDto(x))
            .ToPagedResultAsync(request, cancellationToken);
    }

    private async Task EnsurePublicTrainerAsync(Guid trainerId, CancellationToken cancellationToken)
    {
        var trainer = await dbContext.TrainerProfiles.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == trainerId && x.IsActive, cancellationToken)
            ?? throw new NotFoundException("trainer_not_found", "The trainer was not found.");
        var visible = await dbContext.Tenants.AsNoTracking()
                .AnyAsync(x => x.Id == trainer.TenantId && x.Status == TenantStatus.Active, cancellationToken) &&
            await dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.TenantId == trainer.TenantId && x.IsPubliclyVisible, cancellationToken);
        if (!visible)
        {
            throw new NotFoundException("trainer_not_found", "The trainer was not found.");
        }
    }

    private void Record(string name, Guid tenantId, Guid targetId) =>
        eventRecorder.Record(new(
            name,
            tenantId,
            RequireUser(),
            targetId,
            timeProvider.GetUtcNow().UtcDateTime));

    private Guid RequireUser() =>
        currentUser.UserId ??
        throw new AuthorizationDeniedException("current_user_required", "A current user is required.");

    private static ReviewDto ToDto(Review review) =>
        new(review.Id, review.Rating, review.Comment, review.CreatedAtUtc);

    private static ReviewDto ToDto(GymReview review) =>
        new(review.Id, review.Rating, review.Comment, review.CreatedAtUtc);
}
