namespace GymLink.Application.Abstractions;

public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
}

public interface IRequestMetadata
{
    string CorrelationId { get; }
    string? RemoteIpAddress { get; }
}
