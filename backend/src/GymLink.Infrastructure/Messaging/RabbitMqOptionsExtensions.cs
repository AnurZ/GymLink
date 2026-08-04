using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GymLink.Infrastructure.Messaging;

public static class RabbitMqOptionsExtensions
{
    public static IServiceCollection AddGymLinkRabbitMqOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Validate(
                options => !options.Enabled || IsValidEnabledConfiguration(options),
                "Enabled RabbitMQ configuration is incomplete or contains conflicting topology names.")
            .ValidateOnStart();
        return services;
    }

    internal static bool IsValidEnabledConfiguration(RabbitMqOptions options)
    {
        string[] topologyNames =
        [
            options.Exchange,
            options.NotificationQueue,
            options.EmailQueue,
            options.DeadLetterExchange,
            options.NotificationDeadLetterQueue,
            options.EmailDeadLetterQueue,
        ];

        return !string.IsNullOrWhiteSpace(options.Host) &&
            !string.IsNullOrWhiteSpace(options.VirtualHost) &&
            !string.IsNullOrWhiteSpace(options.Username) &&
            !string.IsNullOrWhiteSpace(options.Password) &&
            options.Port is > 0 and <= 65535 &&
            options.BatchSize is > 0 and <= 100 &&
            options.PollIntervalSeconds > 0 &&
            options.LeaseSeconds > 0 &&
            topologyNames.All(name => !string.IsNullOrWhiteSpace(name)) &&
            topologyNames.Distinct(StringComparer.Ordinal).Count() == topologyNames.Length;
    }
}
