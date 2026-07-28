using System.ComponentModel.DataAnnotations;

namespace GymLink.Application.Identity;

public sealed record RegisterRequest
{
    [Required, StringLength(64, MinimumLength = 3)]
    [RegularExpression("^[A-Za-z0-9._-]+$")]
    public required string Username { get; init; }

    [Required, EmailAddress, MaxLength(320)]
    public required string Email { get; init; }

    [Required, StringLength(160, MinimumLength = 2)]
    public required string DisplayName { get; init; }

    [Phone, MaxLength(32)]
    public string? PhoneNumber { get; init; }

    [Required, StringLength(100, MinimumLength = 8)]
    public required string Password { get; init; }
}

public sealed record LoginRequest
{
    [Required, MaxLength(320)]
    public required string Identifier { get; init; }

    [Required, MaxLength(100)]
    public required string Password { get; init; }
}

public sealed record RefreshSessionRequest
{
    [Required, MaxLength(512)]
    public required string RefreshToken { get; init; }
}

public sealed record LogoutRequest
{
    [Required, MaxLength(512)]
    public required string RefreshToken { get; init; }
}

public sealed record ForgotPasswordRequest
{
    [Required, EmailAddress, MaxLength(320)]
    public required string Email { get; init; }
}

public sealed record ResetPasswordRequest
{
    [Required, EmailAddress, MaxLength(320)]
    public required string Email { get; init; }

    [Required, RegularExpression("^[0-9]{6}$")]
    public required string Code { get; init; }

    [Required, StringLength(100, MinimumLength = 8)]
    public required string NewPassword { get; init; }
}

public sealed record PasswordResetAcceptedDto(
    string Message = "If the account exists, a reset code will be sent.");

public sealed record UpdateProfileRequest
{
    [Required, StringLength(160, MinimumLength = 2)]
    public required string DisplayName { get; init; }

    [Required, EmailAddress, MaxLength(320)]
    public required string Email { get; init; }

    [Phone, MaxLength(32)]
    public string? PhoneNumber { get; init; }
}

public sealed record ChangePasswordRequest
{
    [Required, MaxLength(100)]
    public required string CurrentPassword { get; init; }

    [Required, StringLength(100, MinimumLength = 8)]
    public required string NewPassword { get; init; }

    [Required, Compare(nameof(NewPassword))]
    public required string ConfirmPassword { get; init; }
}

public sealed record TenantSessionDto(Guid Id, string Name, string Role);

public sealed record UserProfileDto(
    Guid Id,
    string Username,
    string Email,
    string DisplayName,
    string? PhoneNumber,
    string Role,
    bool IsActive,
    TenantSessionDto? Tenant,
    Guid? TrainerProfileId);

public sealed record AuthSessionDto(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    UserProfileDto User);

public sealed record IssuedAccessToken(string Value, string Jti, DateTime ExpiresAtUtc);

public sealed record IdentityAccount(
    Guid Id,
    string Username,
    string Email,
    string Role);

public sealed record IdentityOperationResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static IdentityOperationResult Success { get; } = new(true, []);
}
