using GymLink.Application.Abstractions;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Trainers;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Common;

internal static class QueryExtensions
{
    public static IQueryable<TrainerProfile> CanonicalActiveTrainers(
        this IApplicationDbContext dbContext,
        bool ignoreTenantFilter = true)
    {
        var trainers = dbContext.TrainerProfiles.AsNoTracking();
        var assignments = dbContext.UserGymAssignments.AsNoTracking();
        if (ignoreTenantFilter)
        {
            trainers = trainers.IgnoreQueryFilters();
            assignments = assignments.IgnoreQueryFilters();
        }

        return trainers.Where(trainer =>
            trainer.IsActive &&
            dbContext.UserProfiles.Any(user => user.Id == trainer.UserId && user.IsActive) &&
            dbContext.Tenants.Any(tenant =>
                tenant.Id == trainer.TenantId && tenant.Status == TenantStatus.Active) &&
            assignments.Count(assignment =>
                assignment.UserId == trainer.UserId &&
                assignment.TenantId == trainer.TenantId &&
                assignment.Role == RoleNames.Trainer &&
                assignment.Status == AssignmentStatus.Active) == 1 &&
            !assignments.Any(assignment =>
                assignment.UserId == trainer.UserId &&
                assignment.Status == AssignmentStatus.Active &&
                (assignment.TenantId != trainer.TenantId ||
                 assignment.Role != RoleNames.Trainer)));
    }

    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PagedRequest request,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<T>(items, request.Page, request.PageSize, totalCount);
    }
}
