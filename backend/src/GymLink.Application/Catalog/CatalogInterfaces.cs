using GymLink.Application.Common;

namespace GymLink.Application.Catalog;

public interface IGymCatalogService
{
    Task<PagedResult<GymListItemDto>> SearchPublicAsync(GymSearchRequest request, CancellationToken cancellationToken);
    Task<GymDetailsDto> GetPublicDetailsAsync(Guid id, CancellationToken cancellationToken);
    Task<GymDetailsDto> GetCurrentTenantGymAsync(CancellationToken cancellationToken);
    Task<GymDetailsDto> UpdateCurrentTenantGymAsync(UpdateGymRequest request, CancellationToken cancellationToken);
}

public interface ITrainerCatalogService
{
    Task<PagedResult<TrainerDto>> SearchAsync(TrainerSearchRequest request, CancellationToken cancellationToken);
    Task<PagedResult<TrainerCandidateDto>> SearchCandidatesAsync(
        TrainerCandidateSearchRequest request,
        CancellationToken cancellationToken);
    Task<PagedResult<TrainerDto>> GetPublicByGymAsync(
        Guid gymId,
        PagedRequest request,
        CancellationToken cancellationToken);
    Task<TrainerDto> CreateAsync(CreateTrainerRequest request, CancellationToken cancellationToken);
    Task<TrainerDto> UpdateAsync(Guid id, UpdateTrainerRequest request, CancellationToken cancellationToken);
    Task DeactivateAsync(Guid id, CancellationToken cancellationToken);
}

public interface IMembershipPlanService
{
    Task<PagedResult<MembershipPlanDto>> SearchAsync(CatalogSearchRequest request, CancellationToken cancellationToken);
    Task<PagedResult<MembershipPlanDto>> GetPublicByGymAsync(
        Guid gymId,
        PagedRequest request,
        CancellationToken cancellationToken);
    Task<MembershipPlanDto> CreateAsync(CreateMembershipPlanRequest request, CancellationToken cancellationToken);
    Task<MembershipPlanDto> UpdateAsync(Guid id, UpdateMembershipPlanRequest request, CancellationToken cancellationToken);
    Task DeactivateAsync(Guid id, CancellationToken cancellationToken);
}

public interface ITrainerOfferingService
{
    Task<PagedResult<TrainerOfferingDto>> SearchAsync(CatalogSearchRequest request, CancellationToken cancellationToken);
    Task<PagedResult<TrainerOfferingDto>> GetPublicByTrainerAsync(
        Guid trainerId,
        PagedRequest request,
        CancellationToken cancellationToken);
    Task<TrainerOfferingDto> CreateAsync(CreateTrainerOfferingRequest request, CancellationToken cancellationToken);
    Task<TrainerOfferingDto> UpdateAsync(Guid id, UpdateTrainerOfferingRequest request, CancellationToken cancellationToken);
    Task DeactivateAsync(Guid id, CancellationToken cancellationToken);
}
