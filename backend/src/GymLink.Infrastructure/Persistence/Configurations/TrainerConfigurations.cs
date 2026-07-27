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
        builder.Property(x => x.AverageRating).HasPrecision(3, 2);
        builder.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId)
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
        builder.HasOne<TrainerProfile>().WithMany().HasForeignKey(x => x.TrainerProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
