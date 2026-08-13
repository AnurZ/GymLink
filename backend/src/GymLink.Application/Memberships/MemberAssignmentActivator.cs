using GymLink.Application.Abstractions;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Memberships;

internal interface IMemberAssignmentActivator
{
    Task ActivateAsync(
        Guid tenantId,
        Guid memberUserId,
        DateTime startsAtUtc,
        string reason,
        CancellationToken cancellationToken);
}

internal sealed class MemberAssignmentActivator(IApplicationDbContext dbContext)
    : IMemberAssignmentActivator
{
    public async Task ActivateAsync(
        Guid tenantId,
        Guid memberUserId,
        DateTime startsAtUtc,
        string reason,
        CancellationToken cancellationToken)
    {
        var assignment = await dbContext.UserGymAssignments.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId &&
                     x.UserId == memberUserId &&
                     x.Role == RoleNames.Member,
                cancellationToken);
        if (assignment is null)
        {
            dbContext.UserGymAssignments.Add(new UserGymAssignment
            {
                TenantId = tenantId,
                UserId = memberUserId,
                Role = RoleNames.Member,
                Status = AssignmentStatus.Active,
                StartsAtUtc = startsAtUtc,
                Reason = reason,
            });
            return;
        }

        assignment.Status = AssignmentStatus.Active;
        assignment.StartsAtUtc = startsAtUtc;
        assignment.EndsAtUtc = null;
        assignment.ApprovedByUserId = null;
        assignment.Reason = reason;
    }
}
