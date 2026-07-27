using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GymLink.Application.Identity;
using GymLink.Domain.Common;
using GymLink.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace GymLink.IntegrationTests;

public sealed class JwtAccessTokenIssuerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(RoleNames.CentralAdmin)]
    [InlineData(RoleNames.Member)]
    public void Global_roles_have_no_tenant_claims(string role)
    {
        var token = Issue(role, null);

        Assert.Equal(role, token.Claims.Single(x => x.Type == ClaimTypes.Role).Value);
        Assert.DoesNotContain(token.Claims, x => x.Type is "tenant_id" or "tenant_role");
        AssertCoreClaims(token);
    }

    [Theory]
    [InlineData(RoleNames.GymAdmin)]
    [InlineData(RoleNames.Trainer)]
    public void Staff_roles_have_exactly_one_tenant_context(string role)
    {
        var tenantId = Guid.NewGuid();
        var token = Issue(role, new TenantSessionDto(tenantId, "Test Gym", role));

        Assert.Equal(
            tenantId.ToString(),
            token.Claims.Single(x => x.Type == "tenant_id").Value);
        Assert.Equal(role, token.Claims.Single(x => x.Type == "tenant_role").Value);
        AssertCoreClaims(token);
    }

    private static JwtSecurityToken Issue(string role, TenantSessionDto? tenant)
    {
        var issuer = new JwtAccessTokenIssuer(
            Options.Create(new JwtOptions
            {
                Issuer = "GymLink.Tests",
                Audience = "GymLink.Tests.Client",
                SigningKey = "unit-test-signing-key-that-is-over-32-bytes",
                AccessTokenMinutes = 15,
                RefreshTokenDays = 30,
            }),
            new FixedTimeProvider(Now));
        var issued = issuer.Issue(
            new IdentityAccount(Guid.NewGuid(), "testuser", "test@gymlink.local", role),
            4,
            Guid.NewGuid(),
            tenant);
        Assert.Equal(Now.UtcDateTime.AddMinutes(15), issued.ExpiresAtUtc);
        return new JwtSecurityTokenHandler().ReadJwtToken(issued.Value);
    }

    private static void AssertCoreClaims(JwtSecurityToken token)
    {
        Assert.Single(token.Claims, x => x.Type == JwtRegisteredClaimNames.Sub);
        Assert.Single(token.Claims, x => x.Type == JwtRegisteredClaimNames.Jti);
        Assert.Single(token.Claims, x => x.Type == "sid");
        Assert.Equal("4", token.Claims.Single(x => x.Type == "token_version").Value);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
