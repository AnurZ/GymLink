using System.ComponentModel.DataAnnotations;
using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Identity;
using GymLink.Application.Messaging;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Identity;
using GymLink.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Administration;

public sealed record TenantStatusReasonRequest
{
    [Required, StringLength(1000, MinimumLength = 2)]
    public required string Reason { get; init; }
}

public sealed record TenantStatusDto(
    Guid Id,
    string Name,
    TenantStatus Status,
    string? StatusReason,
    DateTime? StatusChangedAtUtc);

public interface ITenantAdministrationService
{
    Task<TenantStatusDto> ActivateAsync(Guid id, CancellationToken cancellationToken);
    Task<TenantStatusDto> DeactivateAsync(
        Guid id,
        TenantStatusReasonRequest request,
        CancellationToken cancellationToken);
    Task<TenantStatusDto> SuspendAsync(
        Guid id,
        TenantStatusReasonRequest request,
        CancellationToken cancellationToken);
    Task<TenantStatusDto> ReactivateAsync(
        Guid id,
        TenantStatusReasonRequest request,
        CancellationToken cancellationToken);
}

internal sealed class TenantAdministrationService(
    IApplicationDbContext dbContext,
    IApplicationTransaction transaction,
    ICurrentUser currentUser,
    ITenantMutationScope tenantMutationScope,
    IOutboxWriter outbox,
    IRequestMetadata requestMetadata,
    TimeProvider timeProvider) : ITenantAdministrationService
{
    public Task<TenantStatusDto> ActivateAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, TenantStatus.PendingActivation, TenantStatus.Active, null, true, cancellationToken);

    public Task<TenantStatusDto> DeactivateAsync(
        Guid id,
        TenantStatusReasonRequest request,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(
            id,
            TenantStatus.Active,
            TenantStatus.Inactive,
            request.Reason,
            false,
            cancellationToken);

    public Task<TenantStatusDto> SuspendAsync(
        Guid id,
        TenantStatusReasonRequest request,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(
            id,
            TenantStatus.Active,
            TenantStatus.Suspended,
            request.Reason,
            false,
            cancellationToken);

    public Task<TenantStatusDto> ReactivateAsync(
        Guid id,
        TenantStatusReasonRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async token =>
        {
            var tenant = await dbContext.Tenants.SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("tenant_not_found", "The tenant was not found.");
            if (tenant.Status is not (TenantStatus.Inactive or TenantStatus.Suspended))
            {
                throw InvalidTransition(tenant.Status, TenantStatus.Active);
            }

            await EnsureCatalogReadyAsync(id, token);
            using var tenantWrite = tenantMutationScope.Begin(id);
            ApplyStatus(tenant, TenantStatus.Active, request.Reason);
            var gym = await dbContext.Gyms.IgnoreQueryFilters()
                .SingleAsync(x => x.TenantId == id, token);
            gym.IsPubliclyVisible = true;
            AddAudit(id, "tenant.reactivated", request.Reason);
            await NotifyGymAdminsAsync(tenant, token);
            await dbContext.SaveChangesAsync(token);
            return ToDto(tenant);
        }, cancellationToken);

    private Task<TenantStatusDto> ChangeStatusAsync(
        Guid id,
        TenantStatus expected,
        TenantStatus target,
        string? reason,
        bool requireCatalog,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async token =>
        {
            var tenant = await dbContext.Tenants.SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("tenant_not_found", "The tenant was not found.");
            if (tenant.Status != expected)
            {
                throw InvalidTransition(tenant.Status, target);
            }

            if (requireCatalog)
            {
                await EnsureCatalogReadyAsync(id, token);
            }

            using var tenantWrite = tenantMutationScope.Begin(id);
            ApplyStatus(tenant, target, reason);
            var gym = await dbContext.Gyms.IgnoreQueryFilters()
                .SingleAsync(x => x.TenantId == id, token);
            gym.IsPubliclyVisible = target == TenantStatus.Active;
            AddAudit(id, $"tenant.{target.ToString().ToLowerInvariant()}", reason);
            await NotifyGymAdminsAsync(tenant, token);
            await dbContext.SaveChangesAsync(token);
            return ToDto(tenant);
        }, cancellationToken);

    private async Task EnsureCatalogReadyAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var hasActiveGymAdmin = await dbContext.UserGymAssignments.IgnoreQueryFilters()
            .AnyAsync(
                x => x.TenantId == tenantId &&
                     x.Role == RoleNames.GymAdmin &&
                     x.Status == AssignmentStatus.Active,
                cancellationToken);
        if (!hasActiveGymAdmin)
        {
            throw new ConflictException(
                "tenant_admin_required",
                "An active GymAdmin must be assigned before activation.");
        }

        var gym = await dbContext.Gyms.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken)
            ?? throw new ConflictException(
                "tenant_catalog_incomplete",
                "The tenant must have a gym before activation.");
        var hasHours = await dbContext.GymWorkingHours.IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == tenantId && !x.IsClosed, cancellationToken);
        var hasEquipment = await dbContext.GymEquipment.IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == tenantId, cancellationToken);
        var hasTrainingType = await dbContext.GymTrainingTypes.IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == tenantId, cancellationToken);
        var hasPlan = await dbContext.MembershipPlans.IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == tenantId && x.IsActive, cancellationToken);
        if (string.IsNullOrWhiteSpace(gym.Description) ||
            !hasHours ||
            !hasEquipment ||
            !hasTrainingType ||
            !hasPlan)
        {
            throw new ConflictException(
                "tenant_catalog_incomplete",
                "Activation requires a description, open working hours, equipment, a training type, and an active membership plan.");
        }
    }

    private void ApplyStatus(
        Tenant tenant,
        TenantStatus status,
        string? reason)
    {
        tenant.Status = status;
        tenant.StatusChangedByUserId = RequireUser();
        tenant.StatusChangedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        tenant.StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    private void AddAudit(Guid tenantId, string action, string? reason) =>
        dbContext.SecurityAuditRecords.Add(new SecurityAuditRecord
        {
            ActorUserId = RequireUser(),
            TargetTenantId = tenantId,
            Action = action,
            TargetType = "Tenant",
            TargetId = tenantId,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        });

    private async Task NotifyGymAdminsAsync(
        Tenant tenant,
        CancellationToken cancellationToken)
    {
        var recipients = await dbContext.UserGymAssignments
            .IgnoreQueryFilters()
            .Where(x =>
                x.TenantId == tenant.Id &&
                x.Role == RoleNames.GymAdmin &&
                x.Status == AssignmentStatus.Active)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);
        foreach (var recipient in recipients)
        {
            outbox.AddNotification(new(
                recipient,
                tenant.Id,
                "tenant.status_changed",
                "Status teretane",
                $"Status teretane je promijenjen na {tenant.Status}.",
                "tenant",
                tenant.Id,
                tenant.StatusChangedAtUtc ?? timeProvider.GetUtcNow().UtcDateTime,
                requestMetadata.CorrelationId));
        }
    }

    private Guid RequireUser() =>
        currentUser.UserId
        ?? throw new AuthenticationFailedException("authentication_required", "Authentication is required.");

    private static ConflictException InvalidTransition(TenantStatus current, TenantStatus target) =>
        new(
            "tenant_status_transition_invalid",
            $"Tenant status cannot change from {current} to {target}.");

    private static TenantStatusDto ToDto(Tenant tenant) =>
        new(tenant.Id, tenant.Name, tenant.Status, tenant.StatusReason, tenant.StatusChangedAtUtc);
}
