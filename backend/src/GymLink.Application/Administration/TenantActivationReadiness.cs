using GymLink.Application.Abstractions;
using GymLink.Application.Identity;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Administration;

public static class ActivationRequirementCodes
{
    public const string GymAdmin = "gym_admin";
    public const string Description = "description";
    public const string WorkingHours = "working_hours";
    public const string Equipment = "equipment";
    public const string TrainingType = "training_type";
    public const string MembershipPlan = "membership_plan";
}

public sealed record TenantActivationReadiness(
    bool CanActivate,
    IReadOnlyList<string> MissingRequirements);

internal static class TenantActivationReadinessEvaluator
{
    public static TenantActivationReadiness Evaluate(
        bool hasDescription,
        bool hasWorkingHours,
        bool hasEquipment,
        bool hasTrainingType,
        bool hasMembershipPlan,
        bool hasGymAdmin)
    {
        var missing = new List<string>();
        AddMissing(hasDescription, ActivationRequirementCodes.Description, missing);
        AddMissing(hasWorkingHours, ActivationRequirementCodes.WorkingHours, missing);
        AddMissing(hasEquipment, ActivationRequirementCodes.Equipment, missing);
        AddMissing(hasTrainingType, ActivationRequirementCodes.TrainingType, missing);
        AddMissing(hasMembershipPlan, ActivationRequirementCodes.MembershipPlan, missing);
        AddMissing(hasGymAdmin, ActivationRequirementCodes.GymAdmin, missing);
        return new(missing.Count == 0, missing);
    }

    private static void AddMissing(bool present, string code, List<string> missing)
    {
        if (!present)
        {
            missing.Add(code);
        }
    }
}

internal interface ITenantActivationReadinessService
{
    Task<TenantActivationReadiness> GetAsync(
        Guid tenantId,
        CancellationToken cancellationToken);
}

internal sealed class TenantActivationReadinessService(IApplicationDbContext dbContext)
    : ITenantActivationReadinessService
{
    public async Task<TenantActivationReadiness> GetAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.Gyms.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(gym => gym.TenantId == tenantId)
            .Select(gym => new
            {
                HasDescription = gym.Description != string.Empty,
                HasGymAdmin = dbContext.UserGymAssignments.IgnoreQueryFilters().Any(assignment =>
                    assignment.TenantId == tenantId &&
                    assignment.Role == RoleNames.GymAdmin &&
                    assignment.Status == AssignmentStatus.Active),
                HasHours = dbContext.GymWorkingHours.IgnoreQueryFilters().Any(hours =>
                    hours.TenantId == tenantId && !hours.IsClosed),
                HasEquipment = dbContext.GymEquipment.IgnoreQueryFilters().Any(equipment =>
                    equipment.TenantId == tenantId),
                HasTrainingType = dbContext.GymTrainingTypes.IgnoreQueryFilters().Any(type =>
                    type.TenantId == tenantId),
                HasPlan = dbContext.MembershipPlans.IgnoreQueryFilters().Any(plan =>
                    plan.TenantId == tenantId && plan.IsActive),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (state is null)
        {
            return TenantActivationReadinessEvaluator.Evaluate(
                false,
                false,
                false,
                false,
                false,
                false);
        }

        return TenantActivationReadinessEvaluator.Evaluate(
            state.HasDescription,
            state.HasHours,
            state.HasEquipment,
            state.HasTrainingType,
            state.HasPlan,
            state.HasGymAdmin);
    }
}
