using System.Linq.Expressions;
using GymLink.Application.Abstractions;
using GymLink.Domain.Catalog;
using GymLink.Domain.Common;
using GymLink.Domain.Engagement;
using GymLink.Domain.Identity;
using GymLink.Domain.Memberships;
using GymLink.Domain.Messaging;
using GymLink.Domain.Payments;
using GymLink.Domain.Recommendations;
using GymLink.Domain.ReferenceData;
using GymLink.Domain.Reservations;
using GymLink.Domain.Tenancy;
using GymLink.Domain.Trainers;
using GymLink.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GymLink.Infrastructure.Persistence;

public sealed class GymLinkDbContext(
    DbContextOptions<GymLinkDbContext> options,
    ITenantContext tenantContext)
    : IdentityDbContext<GymLinkIdentityUser, IdentityRole<Guid>, Guid>(options),
        IApplicationDbContext
{
    public Guid? CurrentTenantId => tenantContext.TenantId;
    public void ClearTrackedChanges() => ChangeTracker.Clear();

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<RefreshTokenSession> RefreshTokenSessions => Set<RefreshTokenSession>();
    public DbSet<PasswordResetChallenge> PasswordResetChallenges =>
        Set<PasswordResetChallenge>();
    public DbSet<SecurityAuditRecord> SecurityAuditRecords => Set<SecurityAuditRecord>();
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
    public DbSet<TrainerAvailabilitySchedule> TrainerAvailabilitySchedules =>
        Set<TrainerAvailabilitySchedule>();
    public DbSet<TrainerWeeklyShift> TrainerWeeklyShifts => Set<TrainerWeeklyShift>();
    public DbSet<MembershipPlan> MembershipPlans => Set<MembershipPlan>();
    public DbSet<MembershipRequest> MembershipRequests => Set<MembershipRequest>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<AppointmentReservation> AppointmentReservations => Set<AppointmentReservation>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<GymReview> GymReviews => Set<GymReview>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<ActivityHistory> ActivityHistory => Set<ActivityHistory>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<StripeEventReceipt> StripeEventReceipts => Set<StripeEventReceipt>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(GymLinkDbContext).Assembly);
        ConfigureIdentityTables(builder);

        foreach (var entityType in builder.Model.GetEntityTypes()
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
            builder.Entity(entityType.ClrType)
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

        foreach (var property in builder.Model.GetEntityTypes()
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
    }

    private static void ConfigureIdentityTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GymLinkIdentityUser>(builder =>
        {
            builder.ToTable("IdentityUsers");
            builder.Property(x => x.UserName).HasMaxLength(160);
            builder.Property(x => x.NormalizedUserName).HasMaxLength(160);
            builder.Property(x => x.Email).HasMaxLength(320);
            builder.Property(x => x.NormalizedEmail).HasMaxLength(320);
            builder.HasIndex(x => x.NormalizedEmail)
                .IsUnique()
                .HasFilter("[NormalizedEmail] IS NOT NULL");
            builder.HasOne(x => x.Profile)
                .WithOne()
                .HasForeignKey<UserProfile>(x => x.Id)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("IdentityRoles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("IdentityUserRoles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("IdentityUserClaims");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("IdentityRoleClaims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("IdentityUserLogins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("IdentityUserTokens");
    }
}
