using GymLink.Contracts.Messaging.V1;
using RabbitMQ.Client;

namespace GymLink.Infrastructure.Messaging;

internal static class RabbitMqTopology
{
    internal sealed record QueueBinding(
        string Queue,
        string Exchange,
        string RoutingKey);

    internal static IReadOnlyList<QueueBinding> Bindings(RabbitMqOptions settings) =>
    [
        new(
            settings.NotificationQueue,
            settings.Exchange,
            MessageContractNames.NotificationRequestedV1),
        new(
            settings.EmailQueue,
            settings.Exchange,
            MessageContractNames.PasswordResetRequestedV1),
        new(
            settings.NotificationDeadLetterQueue,
            settings.DeadLetterExchange,
            MessageContractNames.NotificationRequestedV1),
        new(
            settings.EmailDeadLetterQueue,
            settings.DeadLetterExchange,
            MessageContractNames.PasswordResetRequestedV1),
    ];

    public static async Task DeclareAsync(
        IChannel channel,
        RabbitMqOptions settings,
        CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            settings.Exchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
            settings.DeadLetterExchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            settings.NotificationQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            settings.EmailQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            settings.NotificationDeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            settings.EmailDeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);
        foreach (var binding in Bindings(settings))
        {
            await channel.QueueBindAsync(
                binding.Queue,
                binding.Exchange,
                binding.RoutingKey,
                cancellationToken: cancellationToken);
        }
    }
}
