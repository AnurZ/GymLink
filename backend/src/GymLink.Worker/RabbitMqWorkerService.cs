using System.Text;
using System.Text.Json;
using GymLink.Application.Identity;
using GymLink.Contracts.Messaging.V1;
using GymLink.Domain.Engagement;
using GymLink.Domain.Messaging;
using GymLink.Infrastructure.Identity;
using GymLink.Infrastructure.Messaging;
using GymLink.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace GymLink.Worker;

internal sealed class RabbitMqWorkerService(
    IServiceScopeFactory scopeFactory,
    IEmailSender emailSender,
    IPasswordResetCodeService codes,
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<RabbitMqWorkerService> logger) : BackgroundService
{
    private const int MaximumAttempts = 5;
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly Action<ILogger, string, Exception?> LogConsumerFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(800, "RabbitMqConsumerFailure"),
            "RabbitMQ consumer failed for message type {MessageType}.");

    private IConnection? connection;
    private IChannel? notificationChannel;
    private IChannel? emailChannel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var factory = new ConnectionFactory
        {
            HostName = options.Value.Host,
            Port = options.Value.Port,
            VirtualHost = options.Value.VirtualHost,
            UserName = options.Value.Username,
            Password = options.Value.Password,
            AutomaticRecoveryEnabled = true,
            ConsumerDispatchConcurrency = 1,
        };
        connection = await factory.CreateConnectionAsync(stoppingToken);
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);
        notificationChannel = await connection.CreateChannelAsync(
            channelOptions,
            stoppingToken);
        emailChannel = await connection.CreateChannelAsync(
            channelOptions,
            stoppingToken);
        await RabbitMqTopology.DeclareAsync(
            notificationChannel,
            options.Value,
            stoppingToken);
        await notificationChannel.BasicQosAsync(0, 8, false, stoppingToken);
        await emailChannel.BasicQosAsync(0, 2, false, stoppingToken);

        var notificationConsumer = new AsyncEventingBasicConsumer(notificationChannel);
        notificationConsumer.ReceivedAsync += (_, delivery) =>
            HandleAsync(
                notificationChannel,
                delivery,
                "notification",
                ProcessNotificationAsync,
                stoppingToken);
        await notificationChannel.BasicConsumeAsync(
            options.Value.NotificationQueue,
            autoAck: false,
            notificationConsumer,
            stoppingToken);

        var emailConsumer = new AsyncEventingBasicConsumer(emailChannel);
        emailConsumer.ReceivedAsync += (_, delivery) =>
            HandleAsync(
                emailChannel,
                delivery,
                "reset-email",
                ProcessResetEmailAsync,
                stoppingToken);
        await emailChannel.BasicConsumeAsync(
            options.Value.EmailQueue,
            autoAck: false,
            emailConsumer,
            stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (notificationChannel is not null)
        {
            await notificationChannel.DisposeAsync();
        }

        if (emailChannel is not null)
        {
            await emailChannel.DisposeAsync();
        }

        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
    }

    private async Task HandleAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        string consumer,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task<Guid>> handler,
        CancellationToken cancellationToken)
    {
        var messageId = TryReadMessageId(delivery.Body);
        try
        {
            messageId = await handler(delivery.Body, cancellationToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            LogConsumerFailure(logger, delivery.BasicProperties.Type ?? "unknown", exception);
            var attempts = messageId == Guid.Empty
                ? MaximumAttempts
                : await RecordFailureAsync(
                    messageId,
                    consumer,
                    cancellationToken);
            if (attempts >= MaximumAttempts)
            {
                await channel.BasicPublishAsync(
                    options.Value.DeadLetterExchange,
                    delivery.RoutingKey,
                    mandatory: true,
                    new BasicProperties
                    {
                        Persistent = true,
                        MessageId = delivery.BasicProperties.MessageId,
                        CorrelationId = delivery.BasicProperties.CorrelationId,
                        Type = delivery.BasicProperties.Type,
                        ContentType = delivery.BasicProperties.ContentType,
                    },
                    delivery.Body,
                    cancellationToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
            }
            else
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempts)),
                    cancellationToken);
                await channel.BasicNackAsync(
                    delivery.DeliveryTag,
                    multiple: false,
                    requeue: true,
                    cancellationToken);
            }
        }
    }

    private async Task<Guid> ProcessNotificationAsync(
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        var envelope = Deserialize<NotificationRequestedV1>(body);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GymLinkDbContext>();
        if (await IsCompletedAsync(db, envelope.MessageId, "notification", cancellationToken))
        {
            return envelope.MessageId;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (!await db.Notifications.AnyAsync(
                x => x.SourceMessageId == envelope.MessageId,
                cancellationToken))
        {
            DateTime? readAtUtc = null;
            if (envelope.Payload.Category == "chat" &&
                envelope.Payload.TargetType == "conversation" &&
                envelope.Payload.TargetId.HasValue)
            {
                var lastReadAtUtc = await db.ConversationParticipants
                    .IgnoreQueryFilters()
                    .Where(x =>
                        x.ConversationId == envelope.Payload.TargetId.Value &&
                        x.UserId == envelope.Payload.RecipientUserId)
                    .Select(x => x.LastReadAtUtc)
                    .SingleOrDefaultAsync(cancellationToken);
                if (lastReadAtUtc >= envelope.OccurredAtUtc)
                {
                    readAtUtc = lastReadAtUtc;
                }
            }

            db.Notifications.Add(new Notification
            {
                RecipientUserId = envelope.Payload.RecipientUserId,
                TenantId = envelope.Payload.TenantId,
                Type = envelope.Payload.Category,
                Title = envelope.Payload.Title,
                Text = envelope.Payload.Text,
                TargetType = envelope.Payload.TargetType,
                TargetId = envelope.Payload.TargetId,
                CorrelationId = envelope.CorrelationId,
                SourceMessageId = envelope.MessageId,
                CreatedAtUtc = now,
                ReadAtUtc = readAtUtc,
            });
        }

        await CompleteInboxAsync(
            db,
            envelope.MessageId,
            "notification",
            envelope.MessageType,
            now,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return envelope.MessageId;
    }

    private async Task<Guid> ProcessResetEmailAsync(
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        var envelope = Deserialize<PasswordResetRequestedV1>(body);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GymLinkDbContext>();
        if (await IsCompletedAsync(db, envelope.MessageId, "reset-email", cancellationToken))
        {
            return envelope.MessageId;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var challenge = await db.PasswordResetChallenges.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == envelope.Payload.ChallengeId &&
                     x.UserId == envelope.Payload.UserId,
                cancellationToken);
        var user = await db.Set<GymLinkIdentityUser>().AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == envelope.Payload.UserId,
                cancellationToken);
        if (challenge is null ||
            user?.Email is null ||
            !challenge.CanConfirm(now))
        {
            await CompleteInboxAsync(
                db,
                envelope.MessageId,
                "reset-email",
                envelope.MessageType,
                now,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return envelope.MessageId;
        }

        await emailSender.SendResetCodeAsync(
            user.Email,
            codes.DeriveCode(challenge.Id),
            envelope.MessageId,
            cancellationToken);
        await CompleteInboxAsync(
            db,
            envelope.MessageId,
            "reset-email",
            envelope.MessageType,
            now,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return envelope.MessageId;
    }

    private async Task<int> RecordFailureAsync(
        Guid messageId,
        string messageType,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GymLinkDbContext>();
        var inbox = await db.InboxMessages.SingleOrDefaultAsync(
            x => x.MessageId == messageId && x.Consumer == messageType,
            cancellationToken);
        if (inbox is null)
        {
            inbox = new InboxMessage
            {
                MessageId = messageId,
                Consumer = messageType,
                MessageType = messageType,
                ReceivedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            };
            db.InboxMessages.Add(inbox);
        }

        inbox.ProcessingAttempts++;
        inbox.LastError = "Message processing failed.";
        inbox.NextAttemptAtUtc = timeProvider.GetUtcNow().UtcDateTime.AddSeconds(
            Math.Pow(2, inbox.ProcessingAttempts));
        await db.SaveChangesAsync(cancellationToken);
        return inbox.ProcessingAttempts;
    }

    private static async Task<bool> IsCompletedAsync(
        GymLinkDbContext db,
        Guid messageId,
        string consumer,
        CancellationToken cancellationToken) =>
        await db.InboxMessages.AsNoTracking().AnyAsync(
            x =>
                x.MessageId == messageId &&
                x.Consumer == consumer &&
                x.CompletedAtUtc != null,
            cancellationToken);

    private static async Task CompleteInboxAsync(
        GymLinkDbContext db,
        Guid messageId,
        string consumer,
        string messageType,
        DateTime completedAtUtc,
        CancellationToken cancellationToken)
    {
        var inbox = await db.InboxMessages.SingleOrDefaultAsync(
            x => x.MessageId == messageId && x.Consumer == consumer,
            cancellationToken);
        if (inbox is null)
        {
            inbox = new InboxMessage
            {
                MessageId = messageId,
                Consumer = consumer,
                MessageType = messageType,
                ReceivedAtUtc = completedAtUtc,
            };
            db.InboxMessages.Add(inbox);
        }

        inbox.ProcessingAttempts++;
        inbox.CompletedAtUtc = completedAtUtc;
        inbox.NextAttemptAtUtc = null;
        inbox.LastError = null;
    }

    private static Guid TryReadMessageId(ReadOnlyMemory<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("messageId", out var property) &&
                property.TryGetGuid(out var messageId)
                ? messageId
                : Guid.Empty;
        }
        catch (JsonException)
        {
            return Guid.Empty;
        }
    }

    private static MessageEnvelope<T> Deserialize<T>(ReadOnlyMemory<byte> body) =>
        JsonSerializer.Deserialize<MessageEnvelope<T>>(body.Span, SerializerOptions)
        ?? throw new InvalidOperationException("The message payload is invalid.");

}
