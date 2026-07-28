namespace GymLink.Contracts.Messaging.V1;

public sealed record MessageEnvelope<T>(
    Guid MessageId,
    string CorrelationId,
    DateTime OccurredAtUtc,
    string MessageType,
    int ContractVersion,
    T Payload);

public sealed record NotificationRequestedV1(
    Guid RecipientUserId,
    Guid? TenantId,
    string Category,
    string Title,
    string Text,
    string? TargetType,
    Guid? TargetId);

public sealed record PasswordResetRequestedV1(
    Guid UserId,
    Guid ChallengeId);

public static class MessageContractNames
{
    public const int Version1 = 1;
    public const string NotificationRequestedV1 = "notification.requested.v1";
    public const string PasswordResetRequestedV1 = "password-reset.requested.v1";
}
