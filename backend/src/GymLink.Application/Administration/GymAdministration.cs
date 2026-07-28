using System.ComponentModel.DataAnnotations;
using GymLink.Application.Abstractions;
using GymLink.Application.Catalog;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Domain.Catalog;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Identity;
using GymLink.Domain.Memberships;
using GymLink.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Administration;

public sealed record AdminGymSearchRequest : PagedRequest
{
    [MaxLength(200)]
    public string? Query { get; init; }

    public Guid? CityId { get; init; }
    public TenantStatus? Status { get; init; }
}

public sealed record CreateAdminGymRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public required string Name { get; init; }

    [Required, StringLength(4000, MinimumLength = 10)]
    public required string Description { get; init; }

    [Required, StringLength(300, MinimumLength = 3)]
    public required string Address { get; init; }

    public required Guid CityId { get; init; }

    [Range(-90, 90)]
    public decimal Latitude { get; init; }

    [Range(-180, 180)]
    public decimal Longitude { get; init; }

    [Phone, MaxLength(32)]
    public string? PhoneNumber { get; init; }

    [MinLength(7), MaxLength(7)]
    public IReadOnlyList<WorkingHoursRequest> WorkingHours { get; init; } = [];

    [MinLength(1)]
    public IReadOnlyList<Guid> EquipmentIds { get; init; } = [];

    [MinLength(1)]
    public IReadOnlyList<Guid> TrainingTypeIds { get; init; } = [];

    [Required]
    public required CreateMembershipPlanRequest MembershipPlan { get; init; }

    public required Guid GymAdminUserId { get; init; }

    [Required, StringLength(1000, MinimumLength = 2)]
    public required string GymAdminAssignmentReason { get; init; }
}

public sealed record AdminGymDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string Description,
    string Address,
    Guid CityId,
    string CityName,
    decimal Latitude,
    decimal Longitude,
    string? PhoneNumber,
    TenantStatus Status,
    bool IsPubliclyVisible,
    int ActiveGymAdminCount,
    bool CanActivate,
    IReadOnlyList<string> MissingActivationRequirements,
    DateTime CreatedAtUtc);

public interface IGymAdministrationService
{
    Task<PagedResult<AdminGymDto>> SearchAsync(
        AdminGymSearchRequest request,
        CancellationToken cancellationToken);

    Task<AdminGymDto> CreateAsync(
        CreateAdminGymRequest request,
        CancellationToken cancellationToken);
}

internal sealed class GymAdministrationService(
    IApplicationDbContext dbContext,
    IApplicationTransaction transaction,
    ICurrentUser currentUser,
    ITenantMutationScope tenantMutationScope,
    IGymAdminAssignmentCoordinator gymAdminAssignment,
    ITenantActivationReadinessService readinessService,
    TimeProvider timeProvider) : IGymAdministrationService
{
    public async Task<PagedResult<AdminGymDto>> SearchAsync(
        AdminGymSearchRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var queryText = request.Query?.Trim();
        var query =
            from gym in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
            join tenant in dbContext.Tenants.AsNoTracking() on gym.TenantId equals tenant.Id
            join city in dbContext.Cities.AsNoTracking() on gym.CityId equals city.Id
            where (string.IsNullOrWhiteSpace(queryText) ||
                   gym.Name.Contains(queryText) ||
                   gym.Address.Contains(queryText) ||
                   city.Name.Contains(queryText)) &&
                  (!request.CityId.HasValue || gym.CityId == request.CityId) &&
                  (!request.Status.HasValue || tenant.Status == request.Status)
            select new
            {
                Gym = gym,
                Tenant = tenant,
                CityName = city.Name,
                HasGymAdmin = dbContext.UserGymAssignments.IgnoreQueryFilters().Any(assignment =>
                    assignment.TenantId == gym.TenantId &&
                    assignment.Role == RoleNames.GymAdmin &&
                    assignment.Status == AssignmentStatus.Active),
                HasHours = dbContext.GymWorkingHours.IgnoreQueryFilters().Any(hours =>
                    hours.TenantId == gym.TenantId && !hours.IsClosed),
                HasEquipment = dbContext.GymEquipment.IgnoreQueryFilters().Any(equipment =>
                    equipment.TenantId == gym.TenantId),
                HasTrainingType = dbContext.GymTrainingTypes.IgnoreQueryFilters().Any(type =>
                    type.TenantId == gym.TenantId),
                HasPlan = dbContext.MembershipPlans.IgnoreQueryFilters().Any(plan =>
                    plan.TenantId == gym.TenantId && plan.IsActive),
            };

        var totalCount = await query.LongCountAsync(cancellationToken);
        var page = await query
            .OrderBy(x => x.Gym.Name)
            .ThenBy(x => x.Gym.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var items = page.Select(x =>
        {
            var readiness = TenantActivationReadinessEvaluator.Evaluate(
                !string.IsNullOrWhiteSpace(x.Gym.Description),
                x.HasHours,
                x.HasEquipment,
                x.HasTrainingType,
                x.HasPlan,
                x.HasGymAdmin);
            return new AdminGymDto(
                x.Gym.Id,
                x.Gym.TenantId,
                x.Gym.Name,
                x.Gym.Description,
                x.Gym.Address,
                x.Gym.CityId,
                x.CityName,
                x.Gym.Latitude,
                x.Gym.Longitude,
                x.Gym.PhoneNumber,
                x.Tenant.Status,
                x.Gym.IsPubliclyVisible,
                x.HasGymAdmin ? 1 : 0,
                readiness.CanActivate,
                readiness.MissingRequirements,
                x.Gym.CreatedAtUtc);
        }).ToArray();
        return new(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<AdminGymDto> CreateAsync(
        CreateAdminGymRequest request,
        CancellationToken cancellationToken)
    {
        ValidateCatalogRequest(request);
        try
        {
            return await transaction.ExecuteSerializableAsync(async token =>
            {
                var actorId = currentUser.UserId
                    ?? throw new AuthenticationFailedException(
                        "authentication_required",
                        "Authentication is required.");
                var cityExists = await (
                        from city in dbContext.Cities.AsNoTracking()
                        join country in dbContext.Countries.AsNoTracking()
                            on city.CountryId equals country.Id
                        where city.Id == request.CityId &&
                              city.IsActive &&
                              country.IsActive &&
                              country.Code == "BIH"
                        select city.Id)
                    .AnyAsync(token);
                if (!cityExists)
                {
                    throw new NotFoundException(
                        "city_not_found",
                        "The selected active BiH city was not found.");
                }

                var equipmentIds = request.EquipmentIds.Distinct().ToArray();
                var trainingTypeIds = request.TrainingTypeIds.Distinct().ToArray();
                if (await dbContext.Equipment.CountAsync(
                        x => equipmentIds.Contains(x.Id) && x.IsActive,
                        token) != equipmentIds.Length)
                {
                    throw new NotFoundException(
                        "equipment_not_found",
                        "One or more active equipment values were not found.");
                }

                if (await dbContext.TrainingTypes.CountAsync(
                        x => trainingTypeIds.Contains(x.Id) && x.IsActive,
                        token) != trainingTypeIds.Length)
                {
                    throw new NotFoundException(
                        "training_type_not_found",
                        "One or more active training types were not found.");
                }

                var name = request.Name.Trim();
                var address = request.Address.Trim();
                if (await dbContext.Gyms.IgnoreQueryFilters().AsNoTracking().AnyAsync(
                        x => x.CityId == request.CityId &&
                             x.Name == name &&
                             x.Address == address,
                        token))
                {
                    throw new ConflictException(
                        "gym_already_exists",
                        "A gym with the same name and address already exists.");
                }

                var now = timeProvider.GetUtcNow().UtcDateTime;
                var tenant = new Tenant(Guid.NewGuid(), name)
                {
                    Status = TenantStatus.PendingActivation,
                    StatusChangedByUserId = actorId,
                    StatusChangedAtUtc = now,
                    StatusReason = "Created complete by CentralAdmin; awaiting activation.",
                };
                var gym = new Gym
                {
                    TenantId = tenant.Id,
                    Name = name,
                    Description = request.Description.Trim(),
                    Address = address,
                    CityId = request.CityId,
                    Latitude = request.Latitude,
                    Longitude = request.Longitude,
                    PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
                        ? null
                        : request.PhoneNumber.Trim(),
                    IsPubliclyVisible = false,
                };

                dbContext.Tenants.Add(tenant);
                using (tenantMutationScope.Begin(tenant.Id))
                {
                    dbContext.Gyms.Add(gym);
                    dbContext.GymWorkingHours.AddRange(request.WorkingHours.Select(hours =>
                        new GymWorkingHours
                        {
                            TenantId = tenant.Id,
                            GymId = gym.Id,
                            DayOfWeek = (DayOfWeek)hours.DayOfWeek,
                            OpensAt = hours.IsClosed ? null : hours.OpensAt,
                            ClosesAt = hours.IsClosed ? null : hours.ClosesAt,
                            IsClosed = hours.IsClosed,
                        }));
                    dbContext.GymEquipment.AddRange(equipmentIds.Select(id =>
                        new GymEquipment
                        {
                            TenantId = tenant.Id,
                            GymId = gym.Id,
                            EquipmentId = id,
                        }));
                    dbContext.GymTrainingTypes.AddRange(trainingTypeIds.Select(id =>
                        new GymTrainingType
                        {
                            TenantId = tenant.Id,
                            GymId = gym.Id,
                            TrainingTypeId = id,
                        }));
                    dbContext.MembershipPlans.Add(new MembershipPlan
                    {
                        TenantId = tenant.Id,
                        GymId = gym.Id,
                        Name = request.MembershipPlan.Name.Trim(),
                        DurationDays = request.MembershipPlan.DurationDays,
                        Price = request.MembershipPlan.Price,
                        Currency = request.MembershipPlan.Currency.Trim().ToUpperInvariant(),
                        IsActive = true,
                    });
                    AddAudit(
                        actorId,
                        tenant.Id,
                        gym.Id,
                        "gym.created",
                        "Gym and activation catalog created directly by CentralAdmin.",
                        now);
                    AddAudit(
                        actorId,
                        tenant.Id,
                        gym.Id,
                        "gym.catalog_initialized",
                        "Working hours, equipment, training types, and initial membership plan created.",
                        now);
                    await dbContext.SaveChangesAsync(token);
                }

                await gymAdminAssignment.AssignAsync(
                    request.GymAdminUserId,
                    tenant.Id,
                    request.GymAdminAssignmentReason,
                    actorId,
                    token);
                await dbContext.SaveChangesAsync(token);

                var readiness = await readinessService.GetAsync(tenant.Id, token);
                if (!readiness.CanActivate)
                {
                    throw new ConflictException(
                        "tenant_catalog_incomplete",
                        "The newly created gym is not ready for activation.");
                }

                return await ProjectAsync(gym.Id, token);
            }, cancellationToken);
        }
        catch (Exception exception) when (ContainsDbUpdateException(exception))
        {
            throw new ConflictException(
                "gym_admin_already_assigned",
                "The selected account already has an active gym assignment. Revoke it before assigning another gym.",
                exception);
        }
    }

    private async Task<AdminGymDto> ProjectAsync(
        Guid gymId,
        CancellationToken cancellationToken)
    {
        var core = await (
                from gym in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                join tenant in dbContext.Tenants.AsNoTracking() on gym.TenantId equals tenant.Id
                join city in dbContext.Cities.AsNoTracking() on gym.CityId equals city.Id
                where gym.Id == gymId
                select new { Gym = gym, Tenant = tenant, CityName = city.Name })
            .SingleAsync(cancellationToken);
        var readiness = await readinessService.GetAsync(core.Gym.TenantId, cancellationToken);
        return new(
            core.Gym.Id,
            core.Gym.TenantId,
            core.Gym.Name,
            core.Gym.Description,
            core.Gym.Address,
            core.Gym.CityId,
            core.CityName,
            core.Gym.Latitude,
            core.Gym.Longitude,
            core.Gym.PhoneNumber,
            core.Tenant.Status,
            core.Gym.IsPubliclyVisible,
            readiness.MissingRequirements.Contains(ActivationRequirementCodes.GymAdmin) ? 0 : 1,
            readiness.CanActivate,
            readiness.MissingRequirements,
            core.Gym.CreatedAtUtc);
    }

    private void AddAudit(
        Guid actorId,
        Guid tenantId,
        Guid gymId,
        string action,
        string reason,
        DateTime occurredAtUtc) =>
        dbContext.SecurityAuditRecords.Add(new SecurityAuditRecord
        {
            ActorUserId = actorId,
            TargetTenantId = tenantId,
            Action = action,
            TargetType = nameof(Gym),
            TargetId = gymId,
            Reason = reason,
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = occurredAtUtc,
        });

    private static void ValidateCatalogRequest(CreateAdminGymRequest request)
    {
        if (request.GymAdminUserId == Guid.Empty)
        {
            throw new ApplicationRuleException(
                "gym_admin_required",
                "A GymAdmin account is required.");
        }

        if (request.EquipmentIds.Count == 0 || request.EquipmentIds.Any(x => x == Guid.Empty))
        {
            throw new ApplicationRuleException(
                "equipment_required",
                "At least one equipment value is required.");
        }

        if (request.TrainingTypeIds.Count == 0 ||
            request.TrainingTypeIds.Any(x => x == Guid.Empty))
        {
            throw new ApplicationRuleException(
                "training_type_required",
                "At least one training type is required.");
        }

        if (request.WorkingHours.Count != 7 ||
            request.WorkingHours.Select(x => x.DayOfWeek).Distinct().Count() != 7 ||
            request.WorkingHours.Any(x => x.DayOfWeek is < 0 or > 6))
        {
            throw new ApplicationRuleException(
                "working_hours_incomplete",
                "Working hours must contain exactly one entry for every weekday.");
        }

        if (!request.WorkingHours.Any(x => !x.IsClosed))
        {
            throw new ApplicationRuleException(
                "working_hours_open_day_required",
                "At least one day must be open.");
        }

        if (request.WorkingHours.Any(x =>
                !x.IsClosed &&
                (!x.OpensAt.HasValue ||
                 !x.ClosesAt.HasValue ||
                 x.ClosesAt <= x.OpensAt)))
        {
            throw new ApplicationRuleException(
                "working_hours_invalid",
                "Open days require an opening time before the closing time.");
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
