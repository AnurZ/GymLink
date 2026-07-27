using GymLink.Api.ErrorHandling;
using GymLink.Application.Authorization;
using GymLink.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using System.Text.Json.Serialization;

namespace GymLink.Api;

public static class DependencyInjection
{
    private const string CorsPolicy = "ConfiguredOrigins";

    public static IServiceCollection AddGymLinkApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddProblemDetails();
        services.AddControllers()
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.UnmappedMemberHandling =
                    JsonUnmappedMemberHandling.Disallow)
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var details = new ValidationProblemDetails(context.ModelState)
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Title = "validation_failed",
                        Detail = "One or more validation errors occurred.",
                    };
                    details.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                    return new BadRequestObjectResult(details);
                };
            });
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options =>
            options.AddPolicy(
                CorsPolicy,
                policy =>
                {
                    if (origins.Length > 0)
                    {
                        policy.WithOrigins(origins)
                            .AllowAnyHeader()
                            .AllowAnyMethod();
                    }
                }));
        services.AddAuthorizationBuilder()
            .AddPolicy(
                PolicyNames.CentralAdminOnly,
                policy => policy.RequireAuthenticatedUser().RequireRole(RoleNames.CentralAdmin))
            .AddPolicy(
                PolicyNames.TenantGymAdmin,
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireRole(RoleNames.GymAdmin)
                    .RequireClaim("tenant_id")
                    .RequireClaim("tenant_role", RoleNames.GymAdmin))
            .AddPolicy(
                PolicyNames.TenantTrainer,
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireRole(RoleNames.Trainer)
                    .RequireClaim("tenant_id")
                    .RequireClaim("tenant_role", RoleNames.Trainer))
            .AddPolicy(
                PolicyNames.TenantStaff,
                policy => policy
                    .RequireAuthenticatedUser()
                    .RequireRole(RoleNames.GymAdmin, RoleNames.Trainer)
                    .RequireClaim("tenant_id"))
            .AddPolicy(
                PolicyNames.MemberSelf,
                policy => policy.RequireAuthenticatedUser().RequireRole(RoleNames.Member));
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "GymLink API",
                Version = "v1",
                Description = "GymLink API.",
            });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
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

        app.UseCors(CorsPolicy);
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
