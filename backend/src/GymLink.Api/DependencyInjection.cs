using GymLink.Api.ErrorHandling;
using GymLink.Api.Security;
using GymLink.Application.Authorization;
using GymLink.Domain.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.OpenApi;

namespace GymLink.Api;

public static class DependencyInjection
{
    private const string FailClosedScheme = "Phase3Pending";

    public static IServiceCollection AddGymLinkApi(this IServiceCollection services)
    {
        services.AddControllers();
        services.AddAuthentication(FailClosedScheme)
            .AddScheme<AuthenticationSchemeOptions, FailClosedAuthenticationHandler>(
                FailClosedScheme,
                _ => { });
        services.AddAuthorizationBuilder()
            .AddPolicy(
                PolicyNames.CentralAdminOnly,
                policy => policy.RequireAuthenticatedUser().RequireRole(RoleNames.CentralAdmin))
            .AddPolicy(
                PolicyNames.TenantGymAdmin,
                policy => policy.RequireAuthenticatedUser().RequireRole(RoleNames.GymAdmin));
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "GymLink API",
                Version = "v1",
                Description = "GymLink catalog API. Protected writes remain fail-closed until Phase 3 JWT authorization.",
            });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
            });
        });
        return services;
    }

    public static WebApplication UseGymLinkApi(this WebApplication app)
    {
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
