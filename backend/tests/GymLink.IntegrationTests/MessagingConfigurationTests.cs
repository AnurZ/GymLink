using GymLink.Infrastructure.Messaging;
using GymLink.Contracts.Messaging.V1;

namespace GymLink.IntegrationTests;

public sealed class MessagingConfigurationTests
{
    [Fact]
    public void Enabled_rabbitmq_configuration_requires_distinct_durable_topology_names()
    {
        var valid = new RabbitMqOptions
        {
            Enabled = true,
            Host = "rabbitmq",
            VirtualHost = "/",
            Username = "gymlink",
            Password = "local-secret",
        };
        var duplicateDeadLetterQueue = new RabbitMqOptions
        {
            Enabled = true,
            Host = "rabbitmq",
            VirtualHost = "/",
            Username = "gymlink",
            Password = "local-secret",
            NotificationDeadLetterQueue = "gymlink.notifications.v1",
        };

        Assert.True(RabbitMqOptionsExtensions.IsValidEnabledConfiguration(valid));
        Assert.False(
            RabbitMqOptionsExtensions.IsValidEnabledConfiguration(
                duplicateDeadLetterQueue));
    }

    [Fact]
    public void Rabbitmq_defaults_define_separate_live_and_dead_letter_queues()
    {
        var options = new RabbitMqOptions();

        Assert.Equal("gymlink.events", options.Exchange);
        Assert.Equal("gymlink.notifications.v1", options.NotificationQueue);
        Assert.Equal("gymlink.email.v1", options.EmailQueue);
        Assert.Equal("gymlink.dead-letter", options.DeadLetterExchange);
        Assert.Equal(
            "gymlink.notifications.dead-letter.v1",
            options.NotificationDeadLetterQueue);
        Assert.Equal("gymlink.email.dead-letter.v1", options.EmailDeadLetterQueue);
    }

    [Fact]
    public void Live_and_dead_letter_queues_bind_the_same_versioned_routing_keys()
    {
        var options = new RabbitMqOptions();
        var bindings = RabbitMqTopology.Bindings(options);

        Assert.Collection(
            bindings,
            binding => Assert.Equal(
                (options.NotificationQueue, options.Exchange,
                    MessageContractNames.NotificationRequestedV1),
                (binding.Queue, binding.Exchange, binding.RoutingKey)),
            binding => Assert.Equal(
                (options.EmailQueue, options.Exchange,
                    MessageContractNames.PasswordResetRequestedV1),
                (binding.Queue, binding.Exchange, binding.RoutingKey)),
            binding => Assert.Equal(
                (options.EmailQueue, options.Exchange,
                    MessageContractNames.WelcomeEmailRequestedV1),
                (binding.Queue, binding.Exchange, binding.RoutingKey)),
            binding => Assert.Equal(
                (options.NotificationDeadLetterQueue, options.DeadLetterExchange,
                    MessageContractNames.NotificationRequestedV1),
                (binding.Queue, binding.Exchange, binding.RoutingKey)),
            binding => Assert.Equal(
                (options.EmailDeadLetterQueue, options.DeadLetterExchange,
                    MessageContractNames.PasswordResetRequestedV1),
                (binding.Queue, binding.Exchange, binding.RoutingKey)),
            binding => Assert.Equal(
                (options.EmailDeadLetterQueue, options.DeadLetterExchange,
                    MessageContractNames.WelcomeEmailRequestedV1),
                (binding.Queue, binding.Exchange, binding.RoutingKey)));
    }
}
