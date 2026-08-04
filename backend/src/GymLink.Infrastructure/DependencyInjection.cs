using GymLink.Application.Abstractions;
using GymLink.Application.Administration;
using GymLink.Infrastructure.Persistence;
using GymLink.Infrastructure.Security;
using GymLink.Infrastructure.Identity;
using GymLink.Application.Identity;
using GymLink.Application.Messaging;
using GymLink.Infrastructure.Seeding;
using GymLink.Infrastructure.Memberships;
using GymLink.Infrastructure.Reservations;
using GymLink.Infrastructure.Messaging;
using GymLink.Infrastructure.Geocoding;
using GymLink.Infrastructure.Payments;
using GymLink.Application.Payments;
using GymLink.Application.Reservations;
using GymLink.Application.Memberships;
using GymLink.Infrastructure.Storage;
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
    public static IServiceCollection AddGymLinkPaymentWorkerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("GymLink")
            ?? throw new InvalidOperationException(
                "Environment variable ConnectionStrings__GymLink is required.");
        AddStripePayments(services, configuration);
        services.AddScoped<ITenantMutationScope, TenantMutationScope>();
        services.AddScoped<TenantAuditSaveChangesInterceptor>();
        services.AddScoped<IApplicationDbContext>(provider =>
            provider.GetRequiredService<GymLinkDbContext>());
        services.AddDbContext<GymLinkDbContext>((provider, options) =>
            options.UseSqlServer(connectionString)
                .AddInterceptors(provider.GetRequiredService<TenantAuditSaveChangesInterceptor>()));
        services.AddScoped<IApplicationTransaction, ApplicationTransaction>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IConversationPairLock, ConversationPairLock>();
        return services;
    }

    public static IServiceCollection AddGymLinkInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("GymLink")
            ?? throw new InvalidOperationException(
                "Environment variable ConnectionStrings__GymLink is required.");

        services.AddOptions<DatabaseStartupOptions>()
            .Bind(configuration.GetSection(DatabaseStartupOptions.SectionName));
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient("Nominatim", (provider, client) =>
        {
            var settings = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<GeocodingOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });
        services.AddOptions<GeocodingOptions>()
            .Bind(configuration.GetSection(GeocodingOptions.SectionName))
            .Validate(
                options => !options.Enabled ||
                    (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _) &&
                     !string.IsNullOrWhiteSpace(options.UserAgent) &&
                     options.TimeoutSeconds is > 0 and <= 60 &&
                     options.CacheHours is > 0 and <= 168 &&
                     options.MinimumIntervalMilliseconds >= 1000),
                "Enabled geocoding configuration is incomplete or invalid.")
            .ValidateOnStart();
        services.AddScoped<ILocationSearchService, NominatimLocationSearchService>();
        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.RootPath) &&
                    options.RequestPath.StartsWith('/') &&
                    !options.RequestPath.EndsWith('/') &&
                    !string.IsNullOrWhiteSpace(options.GymRootPath) &&
                    options.GymRequestPath.StartsWith('/') &&
                    !options.GymRequestPath.EndsWith('/') &&
                    !string.Equals(
                        options.RequestPath,
                        options.GymRequestPath,
                        StringComparison.OrdinalIgnoreCase),
                "Valid and distinct Trainer/Gym file storage paths are required.")
            .ValidateOnStart();
        services.AddScoped<IFileStorage, FileSystemFileStorage>();
        AddStripePayments(services, configuration);
        services.AddHttpContextAccessor();
        services.AddScoped<ClaimsRequestContext>();
        services.AddScoped<ICurrentUser>(provider =>
            provider.GetRequiredService<ClaimsRequestContext>());
        services.AddScoped<ITenantContext>(provider =>
            provider.GetRequiredService<ClaimsRequestContext>());
        services.AddScoped<IRequestMetadata>(provider =>
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
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IConversationPairLock, ConversationPairLock>();
        services.AddScoped<IPasswordResetCodeService, PasswordResetCodeService>();
        services.AddOptions<PasswordResetOptions>()
            .Bind(configuration.GetSection(PasswordResetOptions.SectionName))
            .Validate(
                options => Encoding.UTF8.GetByteCount(options.CodePepper) >= 32,
                "PasswordReset__CodePepper must contain at least 32 UTF-8 bytes.")
            .ValidateOnStart();
        services.AddGymLinkRabbitMqOptions(configuration);
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddHostedService<OutboxPublisherService>();
        services.AddScoped<IMembershipWorkflowEventRecorder, LoggingMembershipWorkflowEventRecorder>();
        services.AddScoped<IReservationWorkflowEventRecorder, LoggingReservationWorkflowEventRecorder>();
        services.AddOptions<DevelopmentSeedOptions>()
            .Bind(configuration.GetSection(DevelopmentSeedOptions.SectionName));
        services.AddScoped<DevelopmentDataSeeder>();

        return services;
    }

    private static void AddStripePayments(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<StripeOptions>()
            .Bind(configuration.GetSection(StripeOptions.SectionName))
            .Validate(
                options => !options.Enabled ||
                    (options.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal) &&
                     options.WebhookSecret.StartsWith("whsec_", StringComparison.Ordinal) &&
                     Uri.TryCreate(options.SuccessUrl, UriKind.Absolute, out _) &&
                     Uri.TryCreate(options.CancelUrl, UriKind.Absolute, out _)),
                "Enabled Stripe test-mode configuration is incomplete or invalid.")
            .ValidateOnStart();
        services.AddScoped<IPaymentGateway, StripePaymentGateway>();
    }
}
