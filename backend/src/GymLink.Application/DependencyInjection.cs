using System.Reflection;
using GymLink.Application.Catalog;
using GymLink.Application.Administration;
using GymLink.Application.Identity;
using GymLink.Application.Memberships;
using GymLink.Application.Messaging;
using GymLink.Application.ReferenceData;
using GymLink.Application.Registration;
using GymLink.Application.Reservations;
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
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IGymRegistrationService, GymRegistrationService>();
        services.AddScoped<IGymAdministrationService, GymAdministrationService>();
        services.AddScoped<ITenantAdministrationService, TenantAdministrationService>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<IMembershipRequestService, MembershipRequestService>();
        services.AddScoped<IMembershipService, MembershipService>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<INotificationService, NotificationService>();
        return services;
    }
}
