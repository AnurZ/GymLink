using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GymLink.Application.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GymLink.Infrastructure.Security;

internal sealed class JwtAccessTokenIssuer(
    IOptions<JwtOptions> options,
    TimeProvider timeProvider) : IAccessTokenIssuer
{
    public IssuedAccessToken Issue(
        IdentityAccount account,
        int tokenVersion,
        Guid sessionId,
        TenantSessionDto? tenant)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(settings.AccessTokenMinutes);
        var jti = Guid.NewGuid().ToString("N");
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, account.Username),
            new(JwtRegisteredClaimNames.Email, account.Email),
            new(ClaimTypes.Role, account.Role),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(
                JwtRegisteredClaimNames.Iat,
                EpochTime.GetIntDate(now).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
            new("sid", sessionId.ToString()),
            new("token_version", tokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        if (tenant is not null)
        {
            claims.Add(new("tenant_id", tenant.Id.ToString()));
            claims.Add(new("tenant_role", tenant.Role));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            settings.Issuer,
            settings.Audience,
            claims,
            now,
            expires,
            credentials);
        return new IssuedAccessToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            jti,
            expires);
    }
}

internal sealed class RefreshTokenSettings(IOptions<JwtOptions> options) : IRefreshTokenSettings
{
    public TimeSpan Lifetime => TimeSpan.FromDays(options.Value.RefreshTokenDays);
}
