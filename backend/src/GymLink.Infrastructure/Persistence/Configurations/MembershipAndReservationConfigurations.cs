using GymLink.Domain.Catalog;
using GymLink.Domain.Identity;
using GymLink.Domain.Memberships;
using GymLink.Domain.Payments;
using GymLink.Domain.Reservations;
using GymLink.Domain.Tenancy;
using GymLink.Domain.Trainers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymLink.Infrastructure.Persistence.Configurations;

internal sealed class MembershipPlanConfiguration : EntityConfiguration<MembershipPlan>
{
    protected override void ConfigureEntity(EntityTypeBuilder<MembershipPlan> builder)
    {
        ConfigureTenant(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Price).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.GymId, x.Name, x.IsActive });
        builder.HasOne<Gym>().WithMany().HasForeignKey(x => x.GymId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MembershipRequestConfiguration : EntityConfiguration<MembershipRequest>
{
    protected override void ConfigureEntity(EntityTypeBuilder<MembershipRequest> builder)
    {
        ConfigureTenant(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.DecisionReason).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.MemberUserId, x.GymId })
            .IsUnique()
            .HasFilter("[Status] = N'Pending'");
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.MemberUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.DecidedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Gym>().WithMany().HasForeignKey(x => x.GymId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MembershipPlan>().WithMany().HasForeignKey(x => x.MembershipPlanId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MembershipConfiguration : EntityConfiguration<Membership>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Membership> builder)
    {
        ConfigureTenant(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.PlanName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Price).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.StatusReason).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.MemberUserId, x.GymId })
            .IsUnique()
            .HasFilter("[Status] IN (N'PendingPayment', N'Active', N'Suspended')");
        builder.HasIndex(x => x.MembershipRequestId).IsUnique();
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.MemberUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.StatusChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Gym>().WithMany().HasForeignKey(x => x.GymId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MembershipPlan>().WithMany().HasForeignKey(x => x.MembershipPlanId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MembershipRequest>().WithOne().HasForeignKey<Membership>(x => x.MembershipRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AppointmentReservationConfiguration : EntityConfiguration<AppointmentReservation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AppointmentReservation> builder)
    {
        ConfigureTenant(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.Price).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.CancellationReason).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.TrainerProfileId, x.StartsAtUtc, x.EndsAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.MemberUserId, x.StartsAtUtc, x.EndsAtUtc });
        builder.HasIndex(x => x.AvailabilitySlotId)
            .IsUnique()
            .HasFilter(
                "[AvailabilitySlotId] IS NOT NULL AND [Status] IN (N'Pending', N'Confirmed')");
        builder.ToTable(table =>
            table.HasCheckConstraint(
                "CK_AppointmentReservations_TimeRange",
                "[EndsAtUtc] > [StartsAtUtc]"));
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.MemberUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.CancelledByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.ConfirmedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.CompletedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TrainerProfile>().WithMany().HasForeignKey(x => x.TrainerProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TrainerServiceOffering>().WithMany().HasForeignKey(x => x.TrainerServiceOfferingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TrainerAvailabilitySlot>().WithMany().HasForeignKey(x => x.AvailabilitySlotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Membership>().WithMany().HasForeignKey(x => x.MembershipId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReviewConfiguration : EntityConfiguration<Review>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Review> builder)
    {
        ConfigureTenant(builder);
        builder.Property(x => x.Comment).HasMaxLength(2000);
        builder.ToTable(table =>
            table.HasCheckConstraint("CK_Reviews_Rating", "[Rating] >= 1 AND [Rating] <= 5"));
        builder.HasIndex(x => x.ReservationId).IsUnique();
        builder.HasOne<AppointmentReservation>().WithOne().HasForeignKey<Review>(x => x.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.ReviewerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TrainerProfile>().WithMany().HasForeignKey(x => x.TrainerProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GymReviewConfiguration : EntityConfiguration<GymReview>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GymReview> builder)
    {
        ConfigureTenant(builder);
        builder.Property(x => x.Comment).HasMaxLength(2000);
        builder.HasIndex(x => new { x.TenantId, x.GymId, x.ReviewerUserId }).IsUnique();
        builder.ToTable(table =>
            table.HasCheckConstraint("CK_GymReviews_Rating", "[Rating] >= 1 AND [Rating] <= 5"));
        builder.HasOne<Gym>().WithMany().HasForeignKey(x => x.GymId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.ReviewerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
