using GymLink.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace GymLink.Infrastructure.Messaging;

internal sealed class OutboxPublisherService(
    IServiceScopeFactory scopeFactory,
    RabbitMqConnectionProvider connections,
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxPublisherService> logger) : BackgroundService
{
    private static readonly Action<ILogger, Guid, Exception?> LogPublishFailure =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(700, "OutboxPublishFailed"),
            "Outbox message {MessageId} could not be published and will be retried.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(options.Value.PollIntervalSeconds));
        do
        {
            await PublishBatchAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PublishBatchAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var leaseId = Guid.NewGuid();
        List<Guid> ids;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GymLinkDbContext>();
            var messages = await db.OutboxMessages
                .Where(x =>
                    x.PublishedAtUtc == null &&
                    (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now) &&
                    (x.LeasedUntilUtc == null || x.LeasedUntilUtc <= now))
                .OrderBy(x => x.OccurredAtUtc)
                .Take(options.Value.BatchSize)
                .ToListAsync(cancellationToken);
            foreach (var message in messages)
            {
                message.LeaseId = leaseId;
                message.LeasedUntilUtc = now.AddSeconds(options.Value.LeaseSeconds);
            }

            await db.SaveChangesAsync(cancellationToken);
            ids = messages.Select(x => x.Id).ToList();
        }

        foreach (var id in ids)
        {
            await PublishOneAsync(id, leaseId, cancellationToken);
        }
    }

    private async Task PublishOneAsync(
        Guid id,
        Guid leaseId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GymLinkDbContext>();
        var message = await db.OutboxMessages.SingleOrDefaultAsync(
            x => x.Id == id && x.LeaseId == leaseId,
            cancellationToken);
        if (message is null)
        {
            return;
        }

        try
        {
            var channel = await connections.GetChannelAsync(cancellationToken);
            var properties = new BasicProperties
            {
                Persistent = true,
                MessageId = message.Id.ToString(),
                CorrelationId = message.CorrelationId,
                Type = message.MessageType,
                ContentType = "application/json",
            };
            await channel.BasicPublishAsync(
                options.Value.Exchange,
                message.RoutingKey,
                mandatory: true,
                properties,
                System.Text.Encoding.UTF8.GetBytes(message.Payload),
                cancellationToken);
            message.PublishedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            message.LeaseId = null;
            message.LeasedUntilUtc = null;
            message.LastError = null;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            LogPublishFailure(logger, message.Id, exception);
            message.PublishAttempts++;
            message.LeaseId = null;
            message.LeasedUntilUtc = null;
            message.NextAttemptAtUtc = timeProvider.GetUtcNow().UtcDateTime.AddSeconds(
                Math.Min(300, Math.Pow(2, Math.Min(message.PublishAttempts, 8))));
            message.LastError = "RabbitMQ publish failed.";
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
