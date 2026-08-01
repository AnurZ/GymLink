using GymLink.Domain.Identity;
using GymLink.Domain.ReferenceData;
using GymLink.Domain.Tenancy;
using GymLink.Domain.Trainers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymLink.Infrastructure.Persistence.Configurations;

internal sealed class TrainerProfileConfiguration : EntityConfiguration<TrainerProfile>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TrainerProfile> builder)
    {
        ConfigureTenant(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.Biography).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Credentials).HasMaxLength(2000);
        builder.Property(x => x.ImageStorageKey).HasMaxLength(500);
        builder.Property(x => x.ImageUrl).HasMaxLength(1000);
        builder.Property(x => x.ImageContentType).HasMaxLength(32);
        builder.Property(x => x.AverageRating).HasPrecision(3, 2);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_TrainerProfiles_AverageRating",
                "[AverageRating] >= 0 AND [AverageRating] <= 5");
            table.HasCheckConstraint("CK_TrainerProfiles_ReviewCount", "[ReviewCount] >= 0");
            table.HasCheckConstraint(
                "CK_TrainerProfiles_ImageMetadata",
                "([ImageStorageKey] IS NULL AND [ImageUrl] IS NULL AND " +
                "[ImageContentType] IS NULL AND [ImageFileSizeBytes] IS NULL) OR " +
                "([ImageStorageKey] IS NOT NULL AND [ImageUrl] IS NOT NULL AND " +
                "[ImageContentType] IS NOT NULL AND [ImageFileSizeBytes] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_TrainerProfiles_ImageContentType",
                "[ImageContentType] IS NULL OR [ImageContentType] IN " +
                "('image/jpeg', 'image/png', 'image/webp')");
            table.HasCheckConstraint(
                "CK_TrainerProfiles_ImageFileSize",
                $"[ImageFileSizeBytes] IS NULL OR " +
                $"([ImageFileSizeBytes] > 0 AND [ImageFileSizeBytes] <= {TrainerProfile.MaximumImageFileSizeBytes})");
        });
        builder.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TrainerTrainingTypeConfiguration : EntityConfiguration<TrainerTrainingType>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TrainerTrainingType> builder)
    {
        ConfigureTenant(builder);
        builder.HasIndex(x => new { x.TenantId, x.TrainerProfileId, x.TrainingTypeId }).IsUnique();
        builder.HasOne<TrainerProfile>().WithMany().HasForeignKey(x => x.TrainerProfileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TrainingType>().WithMany().HasForeignKey(x => x.TrainingTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TrainerServiceOfferingConfiguration : EntityConfiguration<TrainerServiceOffering>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TrainerServiceOffering> builder)
    {
        ConfigureTenant(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Price).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.HasIndex(x => new
        {
            x.TenantId,
            x.TrainerProfileId,
            x.TrainingTypeId,
            x.Name,
            x.IsActive,
        });
        builder.HasOne<TrainerProfile>().WithMany().HasForeignKey(x => x.TrainerProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TrainingType>().WithMany().HasForeignKey(x => x.TrainingTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TrainerAvailabilitySlotConfiguration : EntityConfiguration<TrainerAvailabilitySlot>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TrainerAvailabilitySlot> builder)
    {
        ConfigureTenant(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.TrainerProfileId, x.StartsAtUtc, x.EndsAtUtc });
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_TrainerAvailabilitySlots_Capacity", "[Capacity] = 1");
            table.HasCheckConstraint(
                "CK_TrainerAvailabilitySlots_TimeRange",
                "[EndsAtUtc] > [StartsAtUtc]");
        });
        builder.HasOne<TrainerProfile>().WithMany().HasForeignKey(x => x.TrainerProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TrainerAvailabilityScheduleConfiguration :
    EntityConfiguration<TrainerAvailabilitySchedule>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TrainerAvailabilitySchedule> builder)
    {
        ConfigureTenant(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.TrainerProfileId }).IsUnique();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_TrainerAvailabilitySchedules_BookingHorizonWeeks",
                "[BookingHorizonWeeks] = 8");
            table.HasCheckConstraint(
                "CK_TrainerAvailabilitySchedules_Revision",
                "[Revision] >= 0");
        });
        builder.HasOne<TrainerProfile>().WithMany().HasForeignKey(x => x.TrainerProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TrainerWeeklyShiftConfiguration : EntityConfiguration<TrainerWeeklyShift>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TrainerWeeklyShift> builder)
    {
        ConfigureTenant(builder);
        builder.Property(x => x.Period).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.StartsAtLocal).HasColumnType("time(0)");
        builder.Property(x => x.EndsAtLocal).HasColumnType("time(0)");
        builder.HasIndex(x => new
        {
            x.TenantId,
            x.TrainerAvailabilityScheduleId,
            x.TrainerProfileId,
            x.DayOfWeek,
            x.Period,
        }).IsUnique();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_TrainerWeeklyShifts_DayOfWeek",
                "[DayOfWeek] >= 0 AND [DayOfWeek] <= 6");
            table.HasCheckConstraint(
                "CK_TrainerWeeklyShifts_TimeRange",
                "[EndsAtLocal] > [StartsAtLocal]");
        });
        builder.HasOne<TrainerAvailabilitySchedule>().WithMany()
            .HasForeignKey(x => x.TrainerAvailabilityScheduleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TrainerProfile>().WithMany().HasForeignKey(x => x.TrainerProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
