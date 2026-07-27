using System.Linq.Expressions;
using GymLink.Application.Abstractions;
using GymLink.Domain.Catalog;
using GymLink.Domain.Common;
using GymLink.Domain.Engagement;
using GymLink.Domain.Identity;
using GymLink.Domain.Memberships;
using GymLink.Domain.Payments;
using GymLink.Domain.Recommendations;
using GymLink.Domain.ReferenceData;
using GymLink.Domain.Reservations;
using GymLink.Domain.Tenancy;
using GymLink.Domain.Trainers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GymLink.Infrastructure.Persistence;

public sealed class GymLinkDbContext(
    DbContextOptions<GymLinkDbContext> options,
    ITenantContext tenantContext)
    : DbContext(options)
{
    public Guid? CurrentTenantId => tenantContext.TenantId;

    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<RefreshTokenSession> RefreshTokenSessions => Set<RefreshTokenSession>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<GymRegistrationRequest> GymRegistrationRequests => Set<GymRegistrationRequest>();
    public DbSet<UserGymAssignment> UserGymAssignments => Set<UserGymAssignment>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<TrainingType> TrainingTypes => Set<TrainingType>();
    public DbSet<Gym> Gyms => Set<Gym>();
    public DbSet<GymImage> GymImages => Set<GymImage>();
    public DbSet<GymWorkingHours> GymWorkingHours => Set<GymWorkingHours>();
    public DbSet<GymEquipment> GymEquipment => Set<GymEquipment>();
    public DbSet<GymTrainingType> GymTrainingTypes => Set<GymTrainingType>();
    public DbSet<TrainerProfile> TrainerProfiles => Set<TrainerProfile>();
    public DbSet<TrainerTrainingType> TrainerTrainingTypes => Set<TrainerTrainingType>();
    public DbSet<TrainerServiceOffering> TrainerServiceOfferings => Set<TrainerServiceOffering>();
    public DbSet<TrainerAvailabilitySlot> TrainerAvailabilitySlots => Set<TrainerAvailabilitySlot>();
    public DbSet<MembershipPlan> MembershipPlans => Set<MembershipPlan>();
    public DbSet<MembershipRequest> MembershipRequests => Set<MembershipRequest>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<AppointmentReservation> AppointmentReservations => Set<AppointmentReservation>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<ActivityHistory> ActivityHistory => Set<ActivityHistory>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Refund> Refunds => Set<Refund>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GymLinkDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(x => typeof(ITenantOwned).IsAssignableFrom(x.ClrType)))
        {
            var parameter = Expression.Parameter(entityType.ClrType, "entity");
            var tenantId = Expression.Convert(
                Expression.Property(parameter, nameof(ITenantOwned.TenantId)),
                typeof(Guid?));
            var currentTenantId = Expression.Property(
                Expression.Constant(this),
                nameof(CurrentTenantId));
            var hasTenant = Expression.Property(currentTenantId, nameof(Nullable<Guid>.HasValue));
            var matchesTenant = Expression.Equal(tenantId, currentTenantId);
            var body = Expression.AndAlso(hasTenant, matchesTenant);
            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(Expression.Lambda(body, parameter));
        }

        var utcConverter = new ValueConverter<DateTime, DateTime>(
            value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        var nullableUtcConverter = new ValueConverter<DateTime?, DateTime?>(
            value => value,
            value => value.HasValue
                ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
                : value);

        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(entityType => entityType.GetProperties()))
        {
            if (property.ClrType == typeof(DateTime))
            {
                property.SetValueConverter(utcConverter);
            }
            else if (property.ClrType == typeof(DateTime?))
            {
                property.SetValueConverter(nullableUtcConverter);
            }
        }

        base.OnModelCreating(modelBuilder);
    }
}
