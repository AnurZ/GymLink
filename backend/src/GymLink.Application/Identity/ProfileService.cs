using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Identity;

internal sealed class ProfileService(
    IApplicationDbContext dbContext,
    IIdentityAccountManager accounts,
    IApplicationTransaction transaction,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : IProfileService
{
    public async Task<UserProfileDto> GetAsync(CancellationToken cancellationToken)
    {
        var userId = RequireCurrentUser();
        var profile = await dbContext.UserProfiles.SingleAsync(x => x.Id == userId, cancellationToken);
        var account = await accounts.FindByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("account_not_found", "The account was not found.");
        TenantSessionDto? tenant = null;
        if (tenantContext.TenantId.HasValue && tenantContext.TenantRole is not null)
        {
            var tenantName = await dbContext.Tenants
                .Where(x => x.Id == tenantContext.TenantId.Value)
                .Select(x => x.Name)
                .SingleAsync(cancellationToken);
            tenant = new(tenantContext.TenantId.Value, tenantName, tenantContext.TenantRole);
        }
        var trainerProfileId = account.Role == RoleNames.Trainer
            ? await dbContext.TrainerProfiles
                .Where(x => x.UserId == userId && x.IsActive)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        return new UserProfileDto(
            userId,
            account.Username,
            account.Email,
            profile.DisplayName,
            profile.PhoneNumber,
            account.Role,
            profile.IsActive,
            tenant,
            trainerProfileId);
    }

    public Task<UserProfileDto> UpdateAsync(
        UpdateProfileRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async token =>
        {
            var userId = RequireCurrentUser();
            var result = await accounts.UpdateEmailAsync(userId, request.Email.Trim(), token);
            EnsureSucceeded(result, "profile_update_failed");
            var profile = await dbContext.UserProfiles.SingleAsync(x => x.Id == userId, token);
            profile.DisplayName = request.DisplayName.Trim();
            profile.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
                ? null
                : request.PhoneNumber.Trim();
            await dbContext.SaveChangesAsync(token);
            return await GetAsync(token);
        }, cancellationToken);

    public Task ChangePasswordAsync(
        ChangePasswordRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async token =>
        {
            var userId = RequireCurrentUser();
            var result = await accounts.ChangePasswordAsync(
                userId,
                request.CurrentPassword,
                request.NewPassword);
            EnsureSucceeded(result, "password_change_failed");

            var profile = await dbContext.UserProfiles.SingleAsync(x => x.Id == userId, token);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var sessions = await dbContext.RefreshTokenSessions
                .Where(x => x.UserId == userId && x.RevokedAtUtc == null)
                .ToListAsync(token);
            foreach (var session in sessions)
            {
                session.RevokedAtUtc = now;
                session.RevocationReason = "password_changed";
            }

            profile.TokenVersion++;
            await dbContext.SaveChangesAsync(token);
            return true;
        }, cancellationToken);

    private Guid RequireCurrentUser() =>
        currentUser.UserId
        ?? throw new AuthenticationFailedException("authentication_required", "Authentication is required.");

    private static void EnsureSucceeded(IdentityOperationResult result, string code)
    {
        if (!result.Succeeded)
        {
            throw new ApplicationRuleException(code, string.Join(" ", result.Errors));
        }
    }
}
