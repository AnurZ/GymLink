using System.Security.Cryptography;
using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Domain.Common;
using GymLink.Domain.Enums;
using GymLink.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Identity;

internal sealed class AuthenticationService(
    IApplicationDbContext dbContext,
    IIdentityAccountManager accounts,
    IAccessTokenIssuer tokenIssuer,
    IApplicationTransaction transaction,
    ICurrentUser currentUser,
    IRefreshTokenSettings refreshTokenSettings,
    TimeProvider timeProvider) : IAuthenticationService
{
    public Task<AuthSessionDto> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async token =>
        {
            var userId = Guid.NewGuid();
            var result = await accounts.CreateAsync(
                userId,
                request.Username.Trim(),
                request.Email.Trim(),
                request.Password,
                token);
            EnsureIdentitySucceeded(result, "registration_failed");

            dbContext.UserProfiles.Add(new UserProfile(userId, request.DisplayName.Trim())
            {
                PhoneNumber = NormalizeOptional(request.PhoneNumber),
            });
            await dbContext.SaveChangesAsync(token);

            result = await accounts.ReplaceRoleAsync(userId, RoleNames.Member, token);
            EnsureIdentitySucceeded(result, "registration_failed");

            var account = await accounts.FindByIdAsync(userId, token)
                ?? throw new ConflictException("registration_failed", "The account could not be created.");
            return await CreateSessionAsync(account, null, token);
        }, cancellationToken);

    public async Task<AuthSessionDto> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdentifierAsync(request.Identifier.Trim(), cancellationToken);
        if (account is null || !await accounts.CheckPasswordAsync(account.Id, request.Password))
        {
            throw new AuthenticationFailedException();
        }

        var profile = await dbContext.UserProfiles
            .SingleOrDefaultAsync(x => x.Id == account.Id, cancellationToken);
        if (profile is null || !profile.IsActive)
        {
            throw new AuthenticationFailedException();
        }

        var tenant = await ResolveTenantAsync(account, cancellationToken);
        return await transaction.ExecuteAsync(
            token => CreateSessionAsync(account, tenant, token),
            cancellationToken);
    }

    public async Task<AuthSessionDto> RefreshAsync(
        RefreshSessionRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await transaction.ExecuteAsync(async token =>
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var hash = HashToken(request.RefreshToken);
            var session = await dbContext.RefreshTokenSessions
                .SingleOrDefaultAsync(x => x.TokenHash == hash, token)
                ?? throw new AuthenticationFailedException("invalid_refresh_token", "The refresh token is invalid.");
            var profile = await dbContext.UserProfiles
                .SingleOrDefaultAsync(x => x.Id == session.UserId, token)
                ?? throw new AuthenticationFailedException("invalid_refresh_token", "The refresh token is invalid.");

            if (session.RevokedAtUtc is not null)
            {
                await RevokeAllSessionsAsync(profile, "refresh_token_reuse", now, token);
                await dbContext.SaveChangesAsync(token);
                return new RefreshOutcome(null, true);
            }

            if (session.ExpiresAtUtc <= now || !profile.IsActive)
            {
                throw new AuthenticationFailedException("invalid_refresh_token", "The refresh token is invalid.");
            }

            var account = await accounts.FindByIdAsync(profile.Id, token)
                ?? throw new AuthenticationFailedException("invalid_refresh_token", "The refresh token is invalid.");
            var tenant = await ResolveTenantAsync(account, token);

            session.RevokedAtUtc = now;
            session.RevocationReason = "rotated";
            var replacement = await CreateSessionAsync(account, tenant, token);
            var replacementSession = await dbContext.RefreshTokenSessions
                .SingleAsync(x => x.TokenHash == HashToken(replacement.RefreshToken), token);
            session.ReplacedBySessionId = replacementSession.Id;
            await dbContext.SaveChangesAsync(token);
            return new RefreshOutcome(replacement, false);
        }, cancellationToken);
        if (outcome.ReuseDetected)
        {
            throw new AuthenticationFailedException(
                "refresh_token_reused",
                "Refresh token reuse was detected. All sessions were revoked.");
        }

        return outcome.Session!;
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireCurrentUser();
        var session = await dbContext.RefreshTokenSessions
            .SingleOrDefaultAsync(x => x.TokenHash == HashToken(request.RefreshToken), cancellationToken);
        if (session is null || session.UserId != userId)
        {
            return;
        }

        if (session.RevokedAtUtc is null)
        {
            session.RevokedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            session.RevocationReason = "logout";
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task LogoutAllAsync(CancellationToken cancellationToken)
    {
        var userId = RequireCurrentUser();
        var profile = await dbContext.UserProfiles
            .SingleAsync(x => x.Id == userId, cancellationToken);
        await RevokeAllSessionsAsync(
            profile,
            "logout_all",
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthSessionDto> CreateSessionAsync(
        IdentityAccount account,
        TenantSessionDto? tenant,
        CancellationToken cancellationToken)
    {
        var profile = await dbContext.UserProfiles
            .SingleAsync(x => x.Id == account.Id, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var session = new RefreshTokenSession
        {
            UserId = account.Id,
            TokenHash = HashToken(refreshToken),
            ExpiresAtUtc = now.Add(refreshTokenSettings.Lifetime),
        };
        var accessToken = tokenIssuer.Issue(account, profile.TokenVersion, session.Id, tenant);
        session.Jti = accessToken.Jti;
        dbContext.RefreshTokenSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthSessionDto(
            accessToken.Value,
            accessToken.ExpiresAtUtc,
            refreshToken,
            session.ExpiresAtUtc,
            ToProfile(account, profile, tenant));
    }

    private async Task<TenantSessionDto?> ResolveTenantAsync(
        IdentityAccount account,
        CancellationToken cancellationToken)
    {
        if (account.Role is not (RoleNames.GymAdmin or RoleNames.Trainer))
        {
            return null;
        }

        var assignments = await dbContext.UserGymAssignments
            .IgnoreQueryFilters()
            .Where(x =>
                x.UserId == account.Id &&
                x.Status == AssignmentStatus.Active &&
                x.Role == account.Role)
            .Join(
                dbContext.Tenants,
                assignment => assignment.TenantId,
                tenant => tenant.Id,
                (assignment, tenant) => new { assignment.TenantId, tenant.Name, tenant.Status })
            .ToListAsync(cancellationToken);

        if (assignments.Count != 1)
        {
            throw new AuthorizationDeniedException(
                "staff_assignment_required",
                "Staff access requires exactly one active gym assignment.");
        }

        var assignment = assignments[0];
        var permitted = account.Role == RoleNames.GymAdmin
            ? assignment.Status is TenantStatus.PendingActivation or TenantStatus.Active
            : assignment.Status == TenantStatus.Active;
        if (!permitted)
        {
            throw new AuthorizationDeniedException(
                "tenant_unavailable",
                "The assigned gym is not currently available for this account.");
        }

        return new TenantSessionDto(assignment.TenantId, assignment.Name, account.Role);
    }

    private async Task RevokeAllSessionsAsync(
        UserProfile profile,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var sessions = await dbContext.RefreshTokenSessions
            .Where(x => x.UserId == profile.Id && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedAtUtc = now;
            session.RevocationReason = reason;
        }

        profile.TokenVersion++;
    }

    private Guid RequireCurrentUser() =>
        currentUser.UserId
        ?? throw new AuthenticationFailedException("authentication_required", "Authentication is required.");

    private static string HashToken(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private static UserProfileDto ToProfile(
        IdentityAccount account,
        UserProfile profile,
        TenantSessionDto? tenant) =>
        new(
            profile.Id,
            account.Username,
            account.Email,
            profile.DisplayName,
            profile.PhoneNumber,
            account.Role,
            profile.IsActive,
            tenant,
            null);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureIdentitySucceeded(IdentityOperationResult result, string code)
    {
        if (!result.Succeeded)
        {
            throw new ConflictException(code, string.Join(" ", result.Errors));
        }
    }

    private sealed record RefreshOutcome(AuthSessionDto? Session, bool ReuseDetected);
}
