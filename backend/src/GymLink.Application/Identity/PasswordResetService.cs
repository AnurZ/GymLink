using GymLink.Application.Abstractions;
using GymLink.Application.Common;
using GymLink.Application.Messaging;
using GymLink.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Identity;

internal sealed class PasswordResetService(
    IApplicationDbContext dbContext,
    IIdentityAccountManager accounts,
    IPasswordResetCodeService codes,
    IOutboxWriter outbox,
    IApplicationTransaction transaction,
    IRequestMetadata requestMetadata,
    TimeProvider timeProvider) : IPasswordResetService
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RequestCooldown = TimeSpan.FromMinutes(1);

    public async Task RequestAsync(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdentifierAsync(
            request.Email.Trim(),
            cancellationToken);
        if (account is null)
        {
            return;
        }

        await transaction.ExecuteAsync(async token =>
        {
            var profile = await dbContext.UserProfiles
                .SingleOrDefaultAsync(x => x.Id == account.Id && x.IsActive, token);
            if (profile is null)
            {
                return true;
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var existing = await dbContext.PasswordResetChallenges
                .Where(x =>
                    x.UserId == account.Id &&
                    x.ConsumedAtUtc == null &&
                    x.SupersededAtUtc == null)
                .SingleOrDefaultAsync(token);
            if (existing is not null &&
                existing.RequestedAtUtc.Add(RequestCooldown) > now)
            {
                return true;
            }

            existing?.Supersede(now);
            var challengeId = Guid.NewGuid();
            var salt = codes.CreateSalt();
            var code = codes.DeriveCode(challengeId);
            var challenge = new PasswordResetChallenge(
                challengeId,
                account.Id,
                codes.HashCode(code, salt),
                salt,
                now,
                now.Add(ChallengeLifetime),
                codes.HashSensitive(requestMetadata.RemoteIpAddress),
                requestMetadata.CorrelationId);
            dbContext.PasswordResetChallenges.Add(challenge);
            outbox.AddPasswordReset(
                account.Id,
                challenge.Id,
                now,
                requestMetadata.CorrelationId);
            await dbContext.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
    }

    public async Task ConfirmAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdentifierAsync(
            request.Email.Trim(),
            cancellationToken);
        if (account is null)
        {
            throw InvalidChallenge();
        }

        var outcome = await transaction.ExecuteAsync(async token =>
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var challenge = await dbContext.PasswordResetChallenges
                .Where(x =>
                    x.UserId == account.Id &&
                    x.ConsumedAtUtc == null &&
                    x.SupersededAtUtc == null)
                .SingleOrDefaultAsync(token);
            if (challenge is null || !challenge.CanConfirm(now))
            {
                return ResetOutcome.Invalid;
            }

            if (!codes.Verify(request.Code, challenge.CodeSalt, challenge.CodeHash))
            {
                challenge.RegisterFailedAttempt(now);
                await dbContext.SaveChangesAsync(token);
                return ResetOutcome.Invalid;
            }

            var result = await accounts.ResetPasswordAsync(account.Id, request.NewPassword);
            if (!result.Succeeded)
            {
                return new(false, string.Join(" ", result.Errors));
            }

            challenge.Consume(now);
            var sessions = await dbContext.RefreshTokenSessions
                .Where(x => x.UserId == account.Id && x.RevokedAtUtc == null)
                .ToListAsync(token);
            foreach (var session in sessions)
            {
                session.RevokedAtUtc = now;
                session.RevocationReason = "password_reset";
            }

            var profile = await dbContext.UserProfiles.SingleAsync(
                x => x.Id == account.Id,
                token);
            profile.TokenVersion++;
            await dbContext.SaveChangesAsync(token);
            return ResetOutcome.Success;
        }, cancellationToken);

        if (!outcome.Succeeded)
        {
            throw outcome.Error is null
                ? InvalidChallenge()
                : new ApplicationRuleException("password_reset_failed", outcome.Error);
        }
    }

    private static ApplicationRuleException InvalidChallenge() =>
        new(
            "password_reset_invalid",
            "The password reset code is invalid or expired.");

    private sealed record ResetOutcome(bool Succeeded, string? Error)
    {
        public static ResetOutcome Success { get; } = new(true, null);
        public static ResetOutcome Invalid { get; } = new(false, null);
    }
}
