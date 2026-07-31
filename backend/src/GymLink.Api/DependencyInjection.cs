using GymLink.Api.ErrorHandling;
using GymLink.Api.Hubs;
using GymLink.Application.Authorization;
using GymLink.Application.Messaging;
using GymLink.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

namespace GymLink.Api;

public static class DependencyInjection
{
    private const string CorsPolicy = "ConfiguredOrigins";

    public static IServiceCollection AddGymLinkApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddProblemDetails();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "rate_limit_exceeded",
                        Detail = "Too many requests. Try again later.",
                    },
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: cancellationToken);
            };
            options.AddPolicy(
                "PasswordResetRequest",
                context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(15),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
            options.AddPolicy(
                "PasswordResetConfirm",
                context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(15),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
            options.AddPolicy(
                "LocationSearch",
                context => RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
        });
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
        services.AddSignalR(options =>
            options.EnableDetailedErrors =
                configuration.GetValue<bool>("SignalR:EnableDetailedErrors"));
        services.AddSingleton<ChatDeliveryService>();
        services.AddSingleton<IConversationRealtimeNotifier>(
            provider => provider.GetRequiredService<ChatDeliveryService>());
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
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
