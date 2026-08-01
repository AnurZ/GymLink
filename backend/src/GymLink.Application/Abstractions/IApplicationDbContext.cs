using GymLink.Domain.Catalog;
using GymLink.Domain.Engagement;
using GymLink.Domain.Messaging;
using GymLink.Domain.Memberships;
using GymLink.Domain.Reservations;
using GymLink.Domain.ReferenceData;
using GymLink.Domain.Identity;
using GymLink.Domain.Payments;
using GymLink.Domain.Recommendations;
using GymLink.Domain.Tenancy;
using GymLink.Domain.Trainers;
using Microsoft.EntityFrameworkCore;

namespace GymLink.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<UserProfile> UserProfiles { get; }
    DbSet<RefreshTokenSession> RefreshTokenSessions { get; }
    DbSet<PasswordResetChallenge> PasswordResetChallenges { get; }
    DbSet<SecurityAuditRecord> SecurityAuditRecords { get; }
    DbSet<Tenant> Tenants { get; }
    DbSet<GymRegistrationRequest> GymRegistrationRequests { get; }
    DbSet<UserGymAssignment> UserGymAssignments { get; }
    DbSet<Country> Countries { get; }
    DbSet<City> Cities { get; }
    DbSet<Equipment> Equipment { get; }
    DbSet<TrainingType> TrainingTypes { get; }
    DbSet<Gym> Gyms { get; }
    DbSet<GymImage> GymImages { get; }
    DbSet<GymWorkingHours> GymWorkingHours { get; }
    DbSet<GymEquipment> GymEquipment { get; }
    DbSet<GymTrainingType> GymTrainingTypes { get; }
    DbSet<TrainerProfile> TrainerProfiles { get; }
    DbSet<TrainerTrainingType> TrainerTrainingTypes { get; }
    DbSet<TrainerServiceOffering> TrainerServiceOfferings { get; }
    DbSet<TrainerAvailabilitySlot> TrainerAvailabilitySlots { get; }
    DbSet<TrainerAvailabilitySchedule> TrainerAvailabilitySchedules { get; }
    DbSet<TrainerWeeklyShift> TrainerWeeklyShifts { get; }
    DbSet<MembershipPlan> MembershipPlans { get; }
    DbSet<MembershipRequest> MembershipRequests { get; }
    DbSet<Membership> Memberships { get; }
    DbSet<AppointmentReservation> AppointmentReservations { get; }
    DbSet<Review> Reviews { get; }
    DbSet<GymReview> GymReviews { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<ConversationParticipant> ConversationParticipants { get; }
    DbSet<Message> Messages { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<InboxMessage> InboxMessages { get; }
    DbSet<Payment> Payments { get; }
    DbSet<StripeEventReceipt> StripeEventReceipts { get; }
    DbSet<UserPreference> UserPreferences { get; }
    DbSet<ActivityHistory> ActivityHistory { get; }
    DbSet<Recommendation> Recommendations { get; }

    void ClearTrackedChanges();
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
