using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Application.Messaging;
using GymLink.Domain.Catalog;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Identity;
using GymLink.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Registration;

internal sealed class GymRegistrationService(
    IApplicationDbContext dbContext,
    IIdentityAccountManager accounts,
    IApplicationTransaction transaction,
    ICurrentUser currentUser,
    ITenantMutationScope tenantMutationScope,
    IOutboxWriter outbox,
    IRequestMetadata requestMetadata,
    TimeProvider timeProvider) : IGymRegistrationService
{
    public async Task<GymRegistrationDto> SubmitAsync(
        SubmitGymRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        if (!await accounts.IsInRoleAsync(userId, RoleNames.Member))
        {
            throw new AuthorizationDeniedException(
                "member_role_required",
                "Only Member accounts can submit a gym registration.");
        }

        if (!await dbContext.Cities.AnyAsync(
                x => x.Id == request.CityId && x.IsActive,
                cancellationToken))
        {
            throw new NotFoundException("city_not_found", "The selected city was not found.");
        }

        if (await dbContext.GymRegistrationRequests.AnyAsync(
                x => x.ApplicantUserId == userId &&
                     x.Status == GymRegistrationStatus.Submitted,
                cancellationToken))
        {
            throw new ConflictException(
                "registration_already_open",
                "The account already has an open gym registration request.");
        }

        var entity = new GymRegistrationRequest
        {
            ApplicantUserId = userId,
            ProposedGymName = request.GymName.Trim(),
            ProposedDescription = request.Description.Trim(),
            ProposedAddress = request.Address.Trim(),
            CityId = request.CityId,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            PhoneNumber = NormalizeOptional(request.PhoneNumber),
            Status = GymRegistrationStatus.Submitted,
            SubmittedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        };
        dbContext.GymRegistrationRequests.Add(entity);
        var applicantName = await dbContext.UserProfiles
            .Where(x => x.Id == userId)
            .Select(x => x.DisplayName)
            .SingleAsync(cancellationToken);
        var centralAdmins = await accounts.GetUserIdsInRoleAsync(
            RoleNames.CentralAdmin,
            cancellationToken);
        foreach (var recipient in centralAdmins)
        {
            outbox.AddNotification(new(
                recipient,
                null,
                "registration.submitted",
                "Nova registracija teretane",
                $"{applicantName} je podnio zahtjev za registraciju teretane {entity.ProposedGymName}.",
                "gymRegistration",
                entity.Id,
                entity.SubmittedAtUtc
                    ?? throw new InvalidOperationException(
                        "A submitted registration must have a submission timestamp."),
                requestMetadata.CorrelationId));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetProjectedAsync(entity.Id, cancellationToken);
    }

    public Task<PagedResult<GymRegistrationDto>> ListMineAsync(
        RegistrationSearchRequest request,
        CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var query =
                from registration in dbContext.GymRegistrationRequests.AsNoTracking()
                join city in dbContext.Cities.AsNoTracking() on registration.CityId equals city.Id
                where registration.ApplicantUserId == userId
                select new { Registration = registration, CityName = city.Name };
        if (request.Status.HasValue)
        {
            query = query.Where(x => x.Registration.Status == request.Status.Value);
        }

        return query
            .OrderByDescending(x => x.Registration.SubmittedAtUtc)
            .ThenBy(x => x.Registration.Id)
            .Select(x => new GymRegistrationDto(
                    x.Registration.Id,
                    x.Registration.ApplicantUserId,
                    x.Registration.ProposedGymName,
                    x.Registration.ProposedDescription,
                    x.Registration.ProposedAddress,
                    x.Registration.CityId,
                    x.CityName,
                    x.Registration.Latitude,
                    x.Registration.Longitude,
                    x.Registration.PhoneNumber,
                    x.Registration.Status,
                    x.Registration.SubmittedAtUtc,
                    x.Registration.DecidedAtUtc,
                    x.Registration.DecisionReason,
                    x.Registration.CreatedTenantId))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public Task<PagedResult<GymRegistrationDto>> SearchAsync(
        RegistrationSearchRequest request,
        CancellationToken cancellationToken)
    {
        var query =
            from registration in dbContext.GymRegistrationRequests.AsNoTracking()
            join city in dbContext.Cities.AsNoTracking() on registration.CityId equals city.Id
            select new { Registration = registration, CityName = city.Name };
        if (request.Status.HasValue)
        {
            query = query.Where(x => x.Registration.Status == request.Status.Value);
        }

        return query.OrderByDescending(x => x.Registration.SubmittedAtUtc)
            .ThenBy(x => x.Registration.Id)
            .Select(x => new GymRegistrationDto(
                x.Registration.Id,
                x.Registration.ApplicantUserId,
                x.Registration.ProposedGymName,
                x.Registration.ProposedDescription,
                x.Registration.ProposedAddress,
                x.Registration.CityId,
                x.CityName,
                x.Registration.Latitude,
                x.Registration.Longitude,
                x.Registration.PhoneNumber,
                x.Registration.Status,
                x.Registration.SubmittedAtUtc,
                x.Registration.DecidedAtUtc,
                x.Registration.DecisionReason,
                x.Registration.CreatedTenantId))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public Task<GymRegistrationDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        GetProjectedAsync(id, cancellationToken);

    public Task<GymRegistrationDto> ApproveAsync(
        Guid id,
        RegistrationDecisionRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async token =>
        {
            var actorId = RequireUser();
            var entity = await GetSubmittedEntityAsync(id, token);
            var applicant = await accounts.FindByIdAsync(entity.ApplicantUserId, token)
                ?? throw new NotFoundException("applicant_not_found", "The applicant account was not found.");
            if (applicant.Role != RoleNames.Member)
            {
                throw new ConflictException(
                    "applicant_role_changed",
                    "The applicant must still have the Member role.");
            }

            var tenant = new Tenant(Guid.NewGuid(), entity.ProposedGymName)
            {
                Status = TenantStatus.PendingActivation,
                StatusChangedByUserId = actorId,
                StatusChangedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
                StatusReason = request.Reason.Trim(),
            };
            dbContext.Tenants.Add(tenant);
            using var tenantWrite = tenantMutationScope.Begin(tenant.Id);
            dbContext.Gyms.Add(new Gym
            {
                TenantId = tenant.Id,
                Name = entity.ProposedGymName,
                Description = entity.ProposedDescription,
                Address = entity.ProposedAddress,
                CityId = entity.CityId,
                Latitude = entity.Latitude,
                Longitude = entity.Longitude,
                PhoneNumber = entity.PhoneNumber,
                IsPubliclyVisible = false,
            });
            dbContext.UserGymAssignments.Add(new UserGymAssignment
            {
                TenantId = tenant.Id,
                UserId = entity.ApplicantUserId,
                Role = RoleNames.GymAdmin,
                Status = AssignmentStatus.Active,
                StartsAtUtc = timeProvider.GetUtcNow().UtcDateTime,
                ApprovedByUserId = actorId,
                Reason = "Gym registration approved.",
            });

            var roleResult = await accounts.ReplaceRoleAsync(
                entity.ApplicantUserId,
                RoleNames.GymAdmin,
                token);
            EnsureSucceeded(roleResult);
            await RevokeUserSessionsAsync(entity.ApplicantUserId, "role_changed", token);

            entity.Status = GymRegistrationStatus.Approved;
            entity.DecidedByUserId = actorId;
            entity.DecidedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            entity.DecisionReason = request.Reason.Trim();
            entity.CreatedTenantId = tenant.Id;
            AddAudit(actorId, entity.ApplicantUserId, tenant.Id, "registration.approved", entity.Id, request.Reason);
            AddDecisionNotification(entity, "registration.approved");
            await dbContext.SaveChangesAsync(token);
            return await GetProjectedAsync(entity.Id, token);
        }, cancellationToken);

    public Task<GymRegistrationDto> RejectAsync(
        Guid id,
        RegistrationDecisionRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async token =>
        {
            var actorId = RequireUser();
            var entity = await GetSubmittedEntityAsync(id, token);
            entity.Status = GymRegistrationStatus.Rejected;
            entity.DecidedByUserId = actorId;
            entity.DecidedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            entity.DecisionReason = request.Reason.Trim();
            AddAudit(actorId, entity.ApplicantUserId, null, "registration.rejected", entity.Id, request.Reason);
            AddDecisionNotification(entity, "registration.rejected");
            await dbContext.SaveChangesAsync(token);
            return await GetProjectedAsync(entity.Id, token);
        }, cancellationToken);

    private async Task<GymRegistrationDto> GetProjectedAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await (
                from request in dbContext.GymRegistrationRequests.AsNoTracking()
                join city in dbContext.Cities.AsNoTracking() on request.CityId equals city.Id
                where request.Id == id
                select new GymRegistrationDto(
                    request.Id,
                    request.ApplicantUserId,
                    request.ProposedGymName,
                    request.ProposedDescription,
                    request.ProposedAddress,
                    request.CityId,
                    city.Name,
                    request.Latitude,
                    request.Longitude,
                    request.PhoneNumber,
                    request.Status,
                    request.SubmittedAtUtc,
                    request.DecidedAtUtc,
                    request.DecisionReason,
                    request.CreatedTenantId))
            .SingleOrDefaultAsync(cancellationToken);
        return result
            ?? throw new NotFoundException(
                "registration_not_found",
                "The registration request was not found.");
    }

    private async Task<GymRegistrationRequest> GetSubmittedEntityAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.GymRegistrationRequests
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("registration_not_found", "The registration request was not found.");
        if (entity.Status != GymRegistrationStatus.Submitted)
        {
            throw new ConflictException(
                "registration_already_decided",
                "The registration request has already been decided.");
        }

        return entity;
    }

    private async Task RevokeUserSessionsAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var profile = await dbContext.UserProfiles.SingleAsync(x => x.Id == userId, cancellationToken);
        var sessions = await dbContext.RefreshTokenSessions
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedAtUtc = now;
            session.RevocationReason = reason;
        }

        profile.TokenVersion++;
    }

    private void AddAudit(
        Guid actorId,
        Guid targetUserId,
        Guid? tenantId,
        string action,
        Guid targetId,
        string reason) =>
        dbContext.SecurityAuditRecords.Add(new SecurityAuditRecord
        {
            ActorUserId = actorId,
            TargetUserId = targetUserId,
            TargetTenantId = tenantId,
            Action = action,
            TargetType = nameof(GymRegistrationRequest),
            TargetId = targetId,
            Reason = reason.Trim(),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        });

    private void AddDecisionNotification(
        GymRegistrationRequest entity,
        string category) =>
        outbox.AddNotification(new(
            entity.ApplicantUserId,
            entity.CreatedTenantId,
            category,
            "Registracija teretane",
            category == "registration.approved"
                ? $"Registracija teretane {entity.ProposedGymName} je odobrena. Razlog: {entity.DecisionReason}"
                : $"Registracija teretane {entity.ProposedGymName} je odbijena. Razlog: {entity.DecisionReason}",
            "gymRegistration",
            entity.Id,
            entity.DecidedAtUtc ?? timeProvider.GetUtcNow().UtcDateTime,
            requestMetadata.CorrelationId));

    private Guid RequireUser() =>
        currentUser.UserId
        ?? throw new AuthenticationFailedException("authentication_required", "Authentication is required.");

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureSucceeded(IdentityOperationResult result)
    {
        if (!result.Succeeded)
        {
            throw new ConflictException("role_change_failed", string.Join(" ", result.Errors));
        }
    }
}
