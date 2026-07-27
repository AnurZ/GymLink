using GymLink.Application.Abstractions;
using GymLink.Infrastructure.Persistence;
using GymLink.Infrastructure.Security;
using GymLink.Infrastructure.Identity;
using GymLink.Application.Identity;
using GymLink.Infrastructure.Seeding;
using GymLink.Infrastructure.Memberships;
using GymLink.Application.Memberships;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.IdentityModel.Tokens.Jwt;

namespace GymLink.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGymLinkInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("GymLink")
            ?? throw new InvalidOperationException(
                "Environment variable ConnectionStrings__GymLink is required.");

        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<ClaimsRequestContext>();
        services.AddScoped<ICurrentUser>(provider =>
            provider.GetRequiredService<ClaimsRequestContext>());
        services.AddScoped<ITenantContext>(provider =>
            provider.GetRequiredService<ClaimsRequestContext>());
        services.AddScoped<ITenantMutationScope, TenantMutationScope>();
        services.AddScoped<TenantAuditSaveChangesInterceptor>();
        services.AddScoped<IApplicationDbContext>(provider =>
            provider.GetRequiredService<GymLinkDbContext>());
        services.AddDbContext<GymLinkDbContext>((provider, options) =>
            options.UseSqlServer(connectionString)
                .AddInterceptors(provider.GetRequiredService<TenantAuditSaveChangesInterceptor>()));
        services.AddIdentityCore<GymLinkIdentityUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<GymLinkDbContext>()
            .AddDefaultTokenProviders();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Issuer),
                "Jwt__Issuer is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Audience),
                "Jwt__Audience is required.")
            .Validate(
                options => Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
                "Jwt__SigningKey must contain at least 32 UTF-8 bytes.")
            .Validate(
                options => options.AccessTokenMinutes > 0 && options.RefreshTokenDays > 0,
                "JWT lifetimes must be positive.")
            .ValidateOnStart();

        var jwtSettings = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("JWT configuration is required.");
        if (Encoding.UTF8.GetByteCount(jwtSettings.SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Environment variable Jwt__SigningKey must contain at least 32 UTF-8 bytes.");
        }

        services.AddScoped<JwtTokenValidationEvents>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtSettings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.UniqueName,
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                };
                options.EventsType = typeof(JwtTokenValidationEvents);
            });
        services.AddScoped<IIdentityAccountManager, IdentityAccountManager>();
        services.AddScoped<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddScoped<IRefreshTokenSettings, RefreshTokenSettings>();
        services.AddScoped<IApplicationTransaction, ApplicationTransaction>();
        services.AddScoped<IMembershipWorkflowEventRecorder, LoggingMembershipWorkflowEventRecorder>();
        services.AddOptions<DevelopmentSeedOptions>()
            .Bind(configuration.GetSection(DevelopmentSeedOptions.SectionName));
        services.AddScoped<DevelopmentDataSeeder>();

        return services;
    }
}
