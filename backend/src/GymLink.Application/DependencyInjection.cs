using System.Reflection;
using GymLink.Application.Catalog;
using GymLink.Application.Administration;
using GymLink.Application.Identity;
using GymLink.Application.ReferenceData;
using GymLink.Application.Registration;
using Microsoft.Extensions.DependencyInjection;

namespace GymLink.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddGymLinkApplication(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddAutoMapper(configuration => { }, Assembly.GetExecutingAssembly());
        services.AddScoped<IReferenceDataService, ReferenceDataService>();
        services.AddScoped<IGymCatalogService, GymCatalogService>();
        services.AddScoped<ITrainerCatalogService, TrainerCatalogService>();
        services.AddScoped<IMembershipPlanService, MembershipPlanService>();
        services.AddScoped<ITrainerOfferingService, TrainerOfferingService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IGymRegistrationService, GymRegistrationService>();
        services.AddScoped<ITenantAdministrationService, TenantAdministrationService>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        return services;
    }
}
