using System.ComponentModel.DataAnnotations;
using GymLink.Application.Common;
using GymLink.Domain.Enums;

namespace GymLink.Application.Memberships;

public sealed record CreateMembershipRequest
{
    public required Guid MembershipPlanId { get; init; }
}

public sealed record MembershipRequestSearchRequest : PagedRequest
{
    public MembershipRequestStatus? Status { get; init; }
    public Guid? MembershipPlanId { get; init; }

    [MaxLength(160)]
    public string? Member { get; init; }

    public DateTime? RequestedFromUtc { get; init; }
    public DateTime? RequestedToUtc { get; init; }
}

public sealed record MembershipSearchRequest : PagedRequest
{
    public MembershipStatus? Status { get; init; }
    public Guid? MembershipPlanId { get; init; }

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
    [Required, StringLength(1000, MinimumLength = 2)]
    public required string Reason { get; init; }
}

public sealed record MembershipRequestDto(
    Guid Id,
    Guid MembershipPlanId,
    string MemberDisplayName,
    string GymName,
    string PlanName,
    decimal Price,
    string Currency,
    MembershipRequestStatus Status,
    DateTime RequestedAtUtc,
    DateTime? DecidedAtUtc,
    string? DecisionReason,
    IReadOnlyList<string> AllowedActions,
    string ConcurrencyToken);

public sealed record MembershipDto(
    Guid Id,
    Guid MembershipPlanId,
    Guid MembershipRequestId,
    string MemberDisplayName,
    string GymName,
    string PlanName,
    decimal Price,
    string Currency,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    MembershipStatus Status,
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
    void Record(MembershipWorkflowEventIntent intent);
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
