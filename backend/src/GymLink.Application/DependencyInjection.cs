using System.Reflection;
using GymLink.Application.Catalog;
using GymLink.Application.Administration;
using GymLink.Application.Identity;
using GymLink.Application.Memberships;
using GymLink.Application.Messaging;
using GymLink.Application.Payments;
using GymLink.Application.ReferenceData;
using GymLink.Application.Registration;
using GymLink.Application.Reservations;
using GymLink.Application.TrainerImages;
using GymLink.Application.GymImages;
using GymLink.Application.Recommendations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddScoped<ITrainerImageService, TrainerImageService>();
        services.AddScoped<IGymImageService, GymImageService>();
        services.AddScoped<IGymRegistrationService, GymRegistrationService>();
        services.AddScoped<IGymAdministrationService, GymAdministrationService>();
        services.AddScoped<ITenantActivationReadinessService, TenantActivationReadinessService>();
        services.AddScoped<ITenantAdministrationService, TenantAdministrationService>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<IGymAdminAssignmentCoordinator, GymAdminAssignmentCoordinator>();
        services.AddScoped<IMembershipRequestService, MembershipRequestService>();
        services.AddScoped<IMembershipService, MembershipService>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IConversationProvisioner, ConversationProvisioner>();
        services.TryAddSingleton<
            IConversationRealtimeNotifier,
            NullConversationRealtimeNotifier>();
        services.AddScoped<ChatService>();
        services.AddScoped<IChatService>(provider => provider.GetRequiredService<ChatService>());
        services.AddScoped<IChatActorService>(
            provider => provider.GetRequiredService<ChatService>());
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentReconciliationService, PaymentReconciliationService>();
        services.AddScoped<IRecommendationActivityRecorder, RecommendationActivityRecorder>();
        services.AddScoped<IRecommendationService, RecommendationService>();
        return services;
    }
}
