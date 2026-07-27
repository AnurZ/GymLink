using AutoMapper;
using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Memberships;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Catalog;

public sealed class MembershipPlanService(
    IApplicationDbContext dbContext,
    ITenantContext tenantContext,
    IMapper mapper) : IMembershipPlanService
{
    public Task<PagedResult<MembershipPlanDto>> SearchAsync(
        CatalogSearchRequest request,
        CancellationToken cancellationToken)
    {
        RequireTenant();
        request.Validate();
        var query = dbContext.MembershipPlans.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var pattern = $"%{request.Query.Trim()}%";
            query = query.Where(x => EF.Functions.Like(x.Name, pattern));
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(x => x.IsActive == request.IsActive.Value);
        }

        return query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new MembershipPlanDto(
                x.Id,
                x.GymId,
                x.Name,
                x.DurationDays,
                x.Price,
                x.Currency,
                x.IsActive))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<MembershipPlanDto>> GetPublicByGymAsync(
        Guid gymId,
        CancellationToken cancellationToken)
    {
        var isVisible = await (
                from gym in dbContext.Gyms.IgnoreQueryFilters().AsNoTracking()
                join tenant in dbContext.Tenants.AsNoTracking() on gym.TenantId equals tenant.Id
                where gym.Id == gymId &&
                    gym.IsPubliclyVisible &&
                    tenant.Status == TenantStatus.Active
                select gym.Id)
            .AnyAsync(cancellationToken);
        if (!isVisible)
        {
            throw new NotFoundException("gym_not_found", "The gym was not found.");
        }

        return await dbContext.MembershipPlans.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.GymId == gymId && x.IsActive)
            .OrderBy(x => x.Price)
            .ThenBy(x => x.Name)
            .Select(x => new MembershipPlanDto(
                x.Id,
                x.GymId,
                x.Name,
                x.DurationDays,
                x.Price,
                x.Currency,
                x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<MembershipPlanDto> CreateAsync(
        CreateMembershipPlanRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var gymId = await CurrentGymIdAsync(cancellationToken);
        var name = request.Name.Trim();
        await EnsureUniqueNameAsync(gymId, name, null, cancellationToken);
        var entity = new MembershipPlan
        {
            TenantId = tenantId,
            GymId = gymId,
            Name = name,
            DurationDays = request.DurationDays,
            Price = request.Price,
            Currency = request.Currency.Trim().ToUpperInvariant(),
        };
        dbContext.MembershipPlans.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return mapper.Map<MembershipPlanDto>(entity);
    }

    public async Task<MembershipPlanDto> UpdateAsync(
        Guid id,
        UpdateMembershipPlanRequest request,
        CancellationToken cancellationToken)
    {
        RequireTenant();
        var entity = await dbContext.MembershipPlans.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("membership_plan_not_found", "The membership plan was not found.");
        var name = request.Name.Trim();
        await EnsureUniqueNameAsync(entity.GymId, name, id, cancellationToken);
        entity.Name = name;
        entity.DurationDays = request.DurationDays;
        entity.Price = request.Price;
        entity.Currency = request.Currency.Trim().ToUpperInvariant();
        entity.IsActive = request.IsActive;
        await dbContext.SaveChangesAsync(cancellationToken);
        return mapper.Map<MembershipPlanDto>(entity);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireTenant();
        var entity = await dbContext.MembershipPlans.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("membership_plan_not_found", "The membership plan was not found.");
        entity.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Guid> CurrentGymIdAsync(CancellationToken cancellationToken)
    {
        var gymId = await dbContext.Gyms.Select(x => x.Id).SingleOrDefaultAsync(cancellationToken);
        return gymId == Guid.Empty
            ? throw new NotFoundException("gym_not_found", "No gym exists for the current tenant.")
            : gymId;
    }

    private async Task EnsureUniqueNameAsync(
        Guid gymId,
        string name,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        if (await dbContext.MembershipPlans.AnyAsync(
                x => x.GymId == gymId &&
                    x.Name == name &&
                    (!excludedId.HasValue || x.Id != excludedId.Value),
                cancellationToken))
        {
            throw new ConflictException(
                "membership_plan_duplicate",
                "A membership plan with the same name already exists.");
        }
    }

    private Guid RequireTenant() =>
        tenantContext.TenantId
        ?? throw new InvalidOperationException("A selected tenant is required.");
}
