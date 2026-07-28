using System.Text.Json;
using GymLink.Application.Messaging;
using GymLink.Contracts.Messaging.V1;
using GymLink.Domain.Messaging;
using GymLink.Infrastructure.Persistence;

namespace GymLink.Infrastructure.Messaging;

internal sealed class OutboxWriter(GymLinkDbContext dbContext) : IOutboxWriter
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public void AddNotification(NotificationIntent intent)
    {
        var payload = new NotificationRequestedV1(
            intent.RecipientUserId,
            intent.TenantId,
            intent.Category,
            intent.Title,
            intent.Text,
            intent.TargetType,
            intent.TargetId);
        Add(
            MessageContractNames.NotificationRequestedV1,
            payload,
            intent.TenantId,
            intent.OccurredAtUtc,
            intent.CorrelationId);
    }

    public void AddPasswordReset(
        Guid userId,
        Guid challengeId,
        DateTime occurredAtUtc,
        string correlationId) =>
        Add(
            MessageContractNames.PasswordResetRequestedV1,
            new PasswordResetRequestedV1(userId, challengeId),
            null,
            occurredAtUtc,
            correlationId);

    private void Add<T>(
        string routingKey,
        T payload,
        Guid? tenantId,
        DateTime occurredAtUtc,
        string correlationId)
    {
        var message = new OutboxMessage
        {
            MessageType = routingKey,
            ContractVersion = MessageContractNames.Version1,
            RoutingKey = routingKey,
            CorrelationId = correlationId,
            TenantId = tenantId,
            OccurredAtUtc = occurredAtUtc,
        };
        var envelope = new MessageEnvelope<T>(
            message.Id,
            correlationId,
            occurredAtUtc,
            routingKey,
            MessageContractNames.Version1,
            payload);
        message.Payload = JsonSerializer.Serialize(envelope, SerializerOptions);
        dbContext.OutboxMessages.Add(message);
    }
}
