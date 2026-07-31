using GymLink.Application.Messaging;
using GymLink.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Infrastructure.Messaging;

internal sealed class ConversationPairLock(GymLinkDbContext dbContext) :
    IConversationPairLock
{
    public Task AcquireAsync(
        Guid tenantId,
        Guid memberUserId,
        Guid trainerUserId,
        CancellationToken cancellationToken)
    {
        var resource =
            $"GymLink.ConversationPair:{tenantId:N}:{memberUserId:N}:{trainerUserId:N}";
        return dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = {resource},
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 15000;
            IF @result < 0
                THROW 51001, 'Could not acquire the conversation pair lock.', 1;
            """, cancellationToken);
    }
}
