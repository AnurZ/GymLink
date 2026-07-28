using Microsoft.Extensions.Options;
using GymLink.Contracts.Messaging.V1;
using RabbitMQ.Client;

namespace GymLink.Infrastructure.Messaging;

internal sealed class RabbitMqConnectionProvider(
    IOptions<RabbitMqOptions> options) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IConnection? connection;
    private IChannel? channel;

    public async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (channel is { IsOpen: true })
        {
            return channel;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (channel is { IsOpen: true })
            {
                return channel;
            }

            var settings = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = settings.Host,
                Port = settings.Port,
                VirtualHost = settings.VirtualHost,
                UserName = settings.Username,
                Password = settings.Password,
                AutomaticRecoveryEnabled = true,
                ConsumerDispatchConcurrency = 1,
            };
            connection = await factory.CreateConnectionAsync(cancellationToken);
            channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true),
                cancellationToken);
            await DeclareTopologyAsync(channel, settings, cancellationToken);
            return channel;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (channel is not null)
        {
            await channel.DisposeAsync();
        }

        if (connection is not null)
        {
            await connection.DisposeAsync();
        }

        gate.Dispose();
    }

    private static async Task DeclareTopologyAsync(
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
        await channel.QueueBindAsync(
            settings.NotificationQueue,
            settings.Exchange,
            MessageContractNames.NotificationRequestedV1,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            settings.EmailQueue,
            settings.Exchange,
            MessageContractNames.PasswordResetRequestedV1,
            cancellationToken: cancellationToken);
    }
}
