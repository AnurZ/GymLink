using GymLink.Application.Identity;
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Infrastructure.Persistence;

internal sealed class ApplicationTransaction(GymLinkDbContext dbContext) : IApplicationTransaction
{
    public Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) =>
        ExecuteAsync(operation, IsolationLevel.ReadCommitted, cancellationToken);

    public Task<T> ExecuteSerializableAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) =>
        ExecuteAsync(operation, IsolationLevel.Serializable, cancellationToken);

    private Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await dbContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }
}
