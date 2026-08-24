using System.ComponentModel.DataAnnotations;
using GymLink.Application.Common;
using GymLink.Domain.Enums;

namespace GymLink.Application.Registration;

public sealed record SubmitGymRegistrationRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public required string GymName { get; init; }

    [Required, StringLength(4000, MinimumLength = 10)]
    public required string Description { get; init; }

    [Required, StringLength(300, MinimumLength = 3)]
    public required string Address { get; init; }

    public required Guid CityId { get; init; }

    [Range(-90, 90)]
    public decimal Latitude { get; init; }

    [Range(-180, 180)]
    public decimal Longitude { get; init; }

    [Phone, MaxLength(32)]
    public string? PhoneNumber { get; init; }
}

public sealed record RegistrationSearchRequest : PagedRequest
{
    public GymRegistrationStatus? Status { get; init; }
}

public sealed record RegistrationDecisionRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public required string Reason { get; init; }
}

public sealed record GymRegistrationDto(
    Guid Id,
    Guid ApplicantUserId,
    string GymName,
    string Description,
    string Address,
    Guid CityId,
    string CityName,
    decimal Latitude,
    decimal Longitude,
    string? PhoneNumber,
    GymRegistrationStatus Status,
    DateTime? SubmittedAtUtc,
    DateTime? DecidedAtUtc,
    string? DecisionReason,
    Guid? CreatedTenantId);

public interface IGymRegistrationService
{
    Task<GymRegistrationDto> SubmitAsync(
        SubmitGymRegistrationRequest request,
        CancellationToken cancellationToken);
    Task<PagedResult<GymRegistrationDto>> ListMineAsync(
        RegistrationSearchRequest request,
        CancellationToken cancellationToken);
    Task<PagedResult<GymRegistrationDto>> SearchAsync(
        RegistrationSearchRequest request,
        CancellationToken cancellationToken);
    Task<GymRegistrationDto> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<GymRegistrationDto> ApproveAsync(
        Guid id,
        RegistrationDecisionRequest request,
        CancellationToken cancellationToken);
    Task<GymRegistrationDto> RejectAsync(
        Guid id,
        RegistrationDecisionRequest request,
        CancellationToken cancellationToken);
}
