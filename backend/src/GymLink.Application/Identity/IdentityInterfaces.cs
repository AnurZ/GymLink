namespace GymLink.Application.Identity;

public interface IAuthenticationService
{
    Task<AuthSessionDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthSessionDto> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthSessionDto> RefreshAsync(RefreshSessionRequest request, CancellationToken cancellationToken);
    Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken);
    Task LogoutAllAsync(CancellationToken cancellationToken);
}

public interface IPasswordResetService
{
    Task RequestAsync(ForgotPasswordRequest request, CancellationToken cancellationToken);
    Task ConfirmAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
}

public interface IPasswordResetCodeService
{
    string DeriveCode(Guid challengeId);
    string CreateSalt();
    string HashCode(string code, string salt);
    bool Verify(string code, string salt, string expectedHash);
    string? HashSensitive(string? value);
}

public interface IProfileService
{
    Task<UserProfileDto> GetAsync(CancellationToken cancellationToken);
    Task<UserProfileDto> UpdateAsync(UpdateProfileRequest request, CancellationToken cancellationToken);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken);
}

public interface IIdentityAccountManager
{
    Task<IdentityAccount?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken);
    Task<IdentityAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, IdentityAccount>> FindByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, string>> GetEmailsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);
    Task<bool> CheckPasswordAsync(Guid userId, string password);
    Task<IdentityOperationResult> CreateAsync(
        Guid userId,
        string username,
        string email,
        string password,
        CancellationToken cancellationToken);
    Task<IdentityOperationResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword);
    Task<IdentityOperationResult> ResetPasswordAsync(
        Guid userId,
        string newPassword);
    Task<IdentityOperationResult> UpdateEmailAsync(
        Guid userId,
        string email,
        CancellationToken cancellationToken);
    Task<IdentityOperationResult> ReplaceRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken);
    Task<bool> IsInRoleAsync(Guid userId, string role);
    Task<IReadOnlyList<string>> GetRolesAsync(Guid userId);
    Task<(IReadOnlyList<IdentityAccount> Items, long TotalCount)> SearchAsync(
        string? query,
        string? role,
        bool? isActive,
        int skip,
        int take,
        CancellationToken cancellationToken);
    Task<int> CountInRoleAsync(string role, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(
        string role,
        CancellationToken cancellationToken);
}

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(
        IdentityAccount account,
        int tokenVersion,
        Guid sessionId,
        TenantSessionDto? tenant);
}

public interface IRefreshTokenSettings
{
    TimeSpan Lifetime { get; }
}

public interface IApplicationTransaction
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken);

    Task<T> ExecuteSerializableAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken);
}
