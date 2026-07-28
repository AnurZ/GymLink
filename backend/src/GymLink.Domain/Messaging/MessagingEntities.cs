using GymLink.Domain.Common;

namespace GymLink.Domain.Messaging;

public sealed class OutboxMessage : Entity, IConcurrencyTracked
{
    public string MessageType { get; set; } = string.Empty;
    public int ContractVersion { get; set; }
    public string RoutingKey { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeasedUntilUtc { get; set; }
    public int PublishAttempts { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class InboxMessage : Entity, IConcurrencyTracked
{
    public Guid MessageId { get; set; }
    public string Consumer { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int ProcessingAttempts { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
