using System.ComponentModel.DataAnnotations;
using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Domain.Catalog;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Identity;
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
    TimeProvider timeProvider) : IGymAdministrationService
{
    public Task<PagedResult<AdminGymDto>> SearchAsync(
        AdminGymSearchRequest request,
        CancellationToken cancellationToken)
    {
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
            orderby gym.Name, gym.Id
            select new AdminGymDto(
                gym.Id,
                gym.TenantId,
                gym.Name,
                gym.Description,
                gym.Address,
                gym.CityId,
                city.Name,
                gym.Latitude,
                gym.Longitude,
                gym.PhoneNumber,
                tenant.Status,
                gym.IsPubliclyVisible,
                dbContext.UserGymAssignments.IgnoreQueryFilters().Count(assignment =>
                    assignment.TenantId == gym.TenantId &&
                    assignment.Role == RoleNames.GymAdmin &&
                    assignment.Status == AssignmentStatus.Active),
                gym.CreatedAtUtc);
        return query.ToPagedResultAsync(request, cancellationToken);
    }

    public Task<AdminGymDto> CreateAsync(
        CreateAdminGymRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async token =>
        {
            var actorId = currentUser.UserId
                ?? throw new AuthenticationFailedException(
                    "authentication_required",
                    "Authentication is required.");
            var cityExists = await dbContext.Cities.AsNoTracking()
                .AnyAsync(x => x.Id == request.CityId && x.IsActive, token);
            if (!cityExists)
            {
                throw new NotFoundException(
                    "city_not_found",
                    "The selected active city was not found.");
            }

            var name = request.Name.Trim();
            var address = request.Address.Trim();
            var duplicate = await dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(
                    x => x.CityId == request.CityId &&
                         x.Name == name &&
                         x.Address == address,
                    token);
            if (duplicate)
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
                StatusReason = "Created by CentralAdmin; awaiting setup and activation.",
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
                dbContext.SecurityAuditRecords.Add(new SecurityAuditRecord
                {
                    ActorUserId = actorId,
                    TargetTenantId = tenant.Id,
                    Action = "gym.created",
                    TargetType = nameof(Gym),
                    TargetId = gym.Id,
                    Reason = "Gym created directly by CentralAdmin.",
                    CorrelationId = Guid.NewGuid().ToString("N"),
                    OccurredAtUtc = now,
                });
                await dbContext.SaveChangesAsync(token);
            }

            return await ProjectAsync(gym.Id, token);
        }, cancellationToken);

    private async Task<AdminGymDto> ProjectAsync(Guid gymId, CancellationToken cancellationToken) =>
        await (
                from gym in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                join tenant in dbContext.Tenants.AsNoTracking() on gym.TenantId equals tenant.Id
                join city in dbContext.Cities.AsNoTracking() on gym.CityId equals city.Id
                where gym.Id == gymId
                select new AdminGymDto(
                    gym.Id,
                    gym.TenantId,
                    gym.Name,
                    gym.Description,
                    gym.Address,
                    gym.CityId,
                    city.Name,
                    gym.Latitude,
                    gym.Longitude,
                    gym.PhoneNumber,
                    tenant.Status,
                    gym.IsPubliclyVisible,
                    0,
                    gym.CreatedAtUtc))
            .SingleAsync(cancellationToken);
}
