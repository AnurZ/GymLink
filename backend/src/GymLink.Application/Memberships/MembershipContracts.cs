using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using GymLink.Application.Common;
using GymLink.Domain.Enums;

namespace GymLink.Application.Memberships;

public enum MembershipPaymentCategory
{
    Stripe,
    PayInPerson,
}

public sealed record CreateMembershipRequest
{
    public required Guid MembershipPlanId { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<MembershipPaymentMethod>))]
    public MembershipPaymentMethod PaymentMethod { get; init; } = MembershipPaymentMethod.Stripe;
}

public sealed record MembershipRequestSearchRequest : PagedRequest
{
    public MembershipRequestStatus? Status { get; init; }
    public MembershipPaymentMethod? PaymentMethod { get; init; }
    public MembershipPaymentCategory? PaymentCategory { get; init; }
    public MembershipStatus? MembershipStatus { get; init; }
    public Guid? MembershipPlanId { get; init; }
    public Guid? GymId { get; init; }

    [MaxLength(160)]
    public string? Member { get; init; }

    public DateTime? RequestedFromUtc { get; init; }
    public DateTime? RequestedToUtc { get; init; }
}

public sealed record MembershipSearchRequest : PagedRequest
{
    public MembershipStatus? Status { get; init; }
    public Guid? MembershipPlanId { get; init; }
    public Guid? GymId { get; init; }
    public bool CurrentOnly { get; init; }
    public DateTime? CoversFromUtc { get; init; }
    public DateTime? CoversToUtc { get; init; }

    [MaxLength(160)]
    public string? Member { get; init; }

    public DateTime? StartsFromUtc { get; init; }
    public DateTime? StartsToUtc { get; init; }
}

public record ConcurrencyRequest
{
    [Required]
    public required string ConcurrencyToken { get; init; }
}

public sealed record ReasonedConcurrencyRequest : ConcurrencyRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public required string Reason { get; init; }
}

public sealed record MembershipRequestDto(
    Guid Id,
    Guid MemberUserId,
    Guid MembershipPlanId,
    Guid GymId,
    string MemberDisplayName,
    string MemberEmail,
    string GymName,
    string PlanName,
    decimal Price,
    string Currency,
    MembershipPaymentMethod PaymentMethod,
    MembershipRequestStatus Status,
    DateTime RequestedAtUtc,
    DateTime? DecidedAtUtc,
    string? DecisionReason,
    MembershipRequestMembershipDto? Membership,
    IReadOnlyList<string> AllowedActions,
    string ConcurrencyToken);

public sealed record MembershipRequestMembershipDto(
    Guid Id,
    MembershipStatus Status,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    Guid? PaymentId,
    PaymentStatus? PaymentStatus,
    bool IsPaid,
    DateTime? StatusChangedAtUtc,
    string? StatusReason,
    IReadOnlyList<string> AllowedActions,
    string ConcurrencyToken);

public sealed record MembershipDto(
    Guid Id,
    Guid MembershipPlanId,
    Guid MembershipRequestId,
    Guid GymId,
    string MemberDisplayName,
    string GymName,
    string PlanName,
    decimal Price,
    string Currency,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    MembershipStatus Status,
    Guid? PaymentId,
    PaymentStatus? PaymentStatus,
    bool IsPaid,
    DateTime? StatusChangedAtUtc,
    string? StatusReason,
    IReadOnlyList<string> AllowedActions,
    string ConcurrencyToken);

public sealed record MembershipWorkflowEventIntent(
    string Name,
    Guid TenantId,
    Guid MemberUserId,
    Guid AggregateId,
    DateTime OccurredAtUtc);

public interface IMembershipWorkflowEventRecorder
{
    Task RecordAsync(
        MembershipWorkflowEventIntent intent,
        CancellationToken cancellationToken);

    Task RecordManyAsync(
        IReadOnlyCollection<MembershipWorkflowEventIntent> intents,
        CancellationToken cancellationToken);
}

public interface IMembershipExpiryService
{
    Task<int> ExpireDueBatchAsync(CancellationToken cancellationToken);

    Task<int> ExpireDueForAsync(
        Guid tenantId,
        Guid memberUserId,
        Guid gymId,
        CancellationToken cancellationToken);
}

public interface IMembershipRequestService
{
    Task<MembershipRequestDto> CreateAsync(
        CreateMembershipRequest request,
        CancellationToken cancellationToken);
    Task<PagedResult<MembershipRequestDto>> SearchMineAsync(
        MembershipRequestSearchRequest request,
        CancellationToken cancellationToken);
    Task<MembershipRequestDto> GetMineAsync(Guid id, CancellationToken cancellationToken);
    Task<MembershipRequestDto> CancelMineAsync(
        Guid id,
        ConcurrencyRequest request,
        CancellationToken cancellationToken);
    Task<PagedResult<MembershipRequestDto>> SearchTenantAsync(
        MembershipRequestSearchRequest request,
        CancellationToken cancellationToken);
    Task<MembershipRequestDto> GetTenantAsync(Guid id, CancellationToken cancellationToken);
    Task<MembershipRequestDto> ApproveAsync(
        Guid id,
        ConcurrencyRequest request,
        CancellationToken cancellationToken);
    Task<MembershipRequestDto> RejectAsync(
        Guid id,
        ReasonedConcurrencyRequest request,
        CancellationToken cancellationToken);
}

public interface IMembershipService
{
    Task<PagedResult<MembershipDto>> SearchMineAsync(
        MembershipSearchRequest request,
        CancellationToken cancellationToken);
    Task<MembershipDto> GetMineAsync(Guid id, CancellationToken cancellationToken);
    Task<MembershipDto> CancelMineAsync(
        Guid id,
        ConcurrencyRequest request,
        CancellationToken cancellationToken);
    Task<PagedResult<MembershipDto>> SearchTenantAsync(
        MembershipSearchRequest request,
        CancellationToken cancellationToken);
    Task<MembershipDto> GetTenantAsync(Guid id, CancellationToken cancellationToken);
    Task<MembershipDto> CancelAsync(
        Guid id,
        ReasonedConcurrencyRequest request,
        CancellationToken cancellationToken);
    Task<MembershipDto> SuspendAsync(
        Guid id,
        ReasonedConcurrencyRequest request,
        CancellationToken cancellationToken);
    Task<MembershipDto> ReactivateAsync(
        Guid id,
        ReasonedConcurrencyRequest request,
        CancellationToken cancellationToken);
    Task<MembershipDto> ExpireAsync(
        Guid id,
        ConcurrencyRequest request,
        CancellationToken cancellationToken);
}
