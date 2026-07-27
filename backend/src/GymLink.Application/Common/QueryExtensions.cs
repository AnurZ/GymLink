using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Common;

internal static class QueryExtensions
{
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
