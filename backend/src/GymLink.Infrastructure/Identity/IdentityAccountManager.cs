using GymLink.Application.Identity;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Infrastructure.Identity;

internal sealed class IdentityAccountManager(
    UserManager<GymLinkIdentityUser> userManager,
    GymLinkDbContext dbContext) : IIdentityAccountManager
{
    public async Task<IdentityAccount?> FindByIdentifierAsync(
        string identifier,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = identifier.Contains('@', StringComparison.Ordinal)
            ? await userManager.FindByEmailAsync(identifier)
            : await userManager.FindByNameAsync(identifier);
        return user is null ? null : await ToAccountAsync(user);
    }

    public async Task<IdentityAccount?> FindByIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : await ToAccountAsync(user);
    }

    public async Task<bool> CheckPasswordAsync(Guid userId, string password)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || await userManager.IsLockedOutAsync(user))
        {
            return false;
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            return false;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return true;
    }

    public async Task<IdentityOperationResult> CreateAsync(
        Guid userId,
        string username,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await userManager.CreateAsync(new GymLinkIdentityUser
        {
            Id = userId,
            UserName = username,
            Email = email,
        }, password);
        return Map(result);
    }

    public async Task<IdentityOperationResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new(false, ["Account not found."]);
        }

        return Map(await userManager.ChangePasswordAsync(user, currentPassword, newPassword));
    }

    public async Task<IdentityOperationResult> ResetPasswordAsync(
        Guid userId,
        string newPassword)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new(false, ["Account not found."]);
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        return Map(await userManager.ResetPasswordAsync(user, token, newPassword));
    }

    public async Task<IdentityOperationResult> UpdateEmailAsync(
        Guid userId,
        string email,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new(false, ["Account not found."]);
        }

        user.Email = email;
        return Map(await userManager.UpdateAsync(user));
    }

    public async Task<IdentityOperationResult> ReplaceRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new(false, ["Account not found."]);
        }

        var existing = await userManager.GetRolesAsync(user);
        if (existing.Count > 0)
        {
            var remove = await userManager.RemoveFromRolesAsync(user, existing);
            if (!remove.Succeeded)
            {
                return Map(remove);
            }
        }

        return Map(await userManager.AddToRoleAsync(user, role));
    }

    public async Task<bool> IsInRoleAsync(Guid userId, string role)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is not null && await userManager.IsInRoleAsync(user, role);
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? [] : [.. await userManager.GetRolesAsync(user)];
    }

    public async Task<(IReadOnlyList<IdentityAccount> Items, long TotalCount)> SearchAsync(
        string? query,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var users = userManager.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalized = query.Trim().ToUpperInvariant();
            users = users.Where(x =>
                (x.NormalizedUserName != null && x.NormalizedUserName.Contains(normalized)) ||
                (x.NormalizedEmail != null && x.NormalizedEmail.Contains(normalized)));
        }

        var total = await users.LongCountAsync(cancellationToken);
        var page = await users
            .OrderBy(x => x.UserName)
            .ThenBy(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        var results = new List<IdentityAccount>(page.Count);
        foreach (var user in page)
        {
            var account = await ToAccountAsync(user);
            if (account is not null)
            {
                results.Add(account);
            }
        }

        return (results, total);
    }

    public async Task<int> CountInRoleAsync(string role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = (await userManager.GetUsersInRoleAsync(role)).Select(x => x.Id).ToArray();
        return await dbContext.UserProfiles.CountAsync(
            x => ids.Contains(x.Id) && x.IsActive,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(
        string role,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (await userManager.GetUsersInRoleAsync(role))
            .Select(x => x.Id)
            .ToArray();
    }

    private async Task<IdentityAccount?> ToAccountAsync(GymLinkIdentityUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        if (roles.Count != 1 || user.UserName is null || user.Email is null)
        {
            return null;
        }

        return new IdentityAccount(user.Id, user.UserName, user.Email, roles[0]);
    }

    private static IdentityOperationResult Map(IdentityResult result) =>
        new(result.Succeeded, result.Errors.Select(x => x.Description).ToArray());
}
