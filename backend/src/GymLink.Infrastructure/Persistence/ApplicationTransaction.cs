using GymLink.Application.Identity;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Infrastructure.Persistence;

internal sealed class ApplicationTransaction(GymLinkDbContext dbContext) : IApplicationTransaction
{
    public Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }
}
