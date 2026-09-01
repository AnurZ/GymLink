using GymLink.Application.Abstractions;
using GymLink.Application.Identity;
using GymLink.Domain.Enums;
using GymLink.Domain.Memberships;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Memberships;

internal static class CurrentMembershipQuery
{
    public static IQueryable<Membership> WhereCurrentAt(
        this IQueryable<Membership> query,
        DateTime now) =>
        query.Where(entity =>
            entity.Status == MembershipStatus.PendingPayment ||
            ((entity.Status == MembershipStatus.Active ||
              entity.Status == MembershipStatus.Suspended) &&
             entity.EndsAtUtc.HasValue &&
             entity.EndsAtUtc > now));
}

internal sealed class MembershipExpiryService(
    IApplicationDbContext dbContext,
    IApplicationTransaction transaction,
    ITenantMutationScope tenantMutationScope,
    IMembershipWorkflowEventRecorder eventRecorder,
    TimeProvider timeProvider) : IMembershipExpiryService
{
    private const int BatchSize = 100;

    public async Task<int> ExpireDueBatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteBatchAsync(cancellationToken);
        }
        catch (Exception exception) when (IsConcurrencyRace(exception))
        {
            dbContext.ClearTrackedChanges();
            return await ExecuteBatchAsync(cancellationToken);
        }
    }

    public async Task<int> ExpireDueForAsync(
        Guid tenantId,
        Guid memberUserId,
        Guid gymId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var due = await DueMemberships(now)
            .Where(entity =>
                entity.TenantId == tenantId &&
                entity.MemberUserId == memberUserId &&
                entity.GymId == gymId)
            .ToListAsync(cancellationToken);
        return await ExpireTrackedAsync(due, now, cancellationToken);
    }

    private IQueryable<Membership> DueMemberships(DateTime now) =>
        dbContext.Memberships.IgnoreQueryFilters()
            .Where(entity =>
                (entity.Status == MembershipStatus.Active ||
                 entity.Status == MembershipStatus.Suspended) &&
                entity.EndsAtUtc.HasValue &&
                entity.EndsAtUtc <= now);

    private Task<int> ExecuteBatchAsync(CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async ct =>
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var due = await DueMemberships(now)
                .OrderBy(entity => entity.EndsAtUtc)
                .ThenBy(entity => entity.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            return await ExpireTrackedAsync(due, now, ct);
        }, cancellationToken);

    private static bool IsConcurrencyRace(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException)
            {
                return true;
            }

            if (current.GetType().FullName == "Microsoft.Data.SqlClient.SqlException" &&
                current.GetType().GetProperty("Number")?.GetValue(current) is 1205)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<int> ExpireTrackedAsync(
        List<Membership> memberships,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (memberships.Count == 0)
        {
            return 0;
        }

        var intents = new List<MembershipWorkflowEventIntent>(memberships.Count);
        foreach (var membership in memberships)
        {
            membership.Expire(now);
            intents.Add(new MembershipWorkflowEventIntent(
                "membership.expired",
                membership.TenantId,
                membership.MemberUserId,
                membership.Id,
                now));
        }

        using (tenantMutationScope.Begin(
            memberships.Select(entity => entity.TenantId).Distinct().ToArray()))
        {
            await eventRecorder.RecordManyAsync(intents, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return memberships.Count;
    }
}
