namespace GymLink.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public bool Enabled { get; init; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 5672;
    public string VirtualHost { get; init; } = "/";
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string Exchange { get; init; } = "gymlink.events";
    public string NotificationQueue { get; init; } = "gymlink.notifications.v1";
    public string EmailQueue { get; init; } = "gymlink.email.v1";
    public string DeadLetterExchange { get; init; } = "gymlink.dead-letter";
    public string NotificationDeadLetterQueue { get; init; } =
        "gymlink.notifications.dead-letter.v1";
    public string EmailDeadLetterQueue { get; init; } = "gymlink.email.dead-letter.v1";
    public int BatchSize { get; init; } = 50;
    public int PollIntervalSeconds { get; init; } = 2;
    public int LeaseSeconds { get; init; } = 30;
}
