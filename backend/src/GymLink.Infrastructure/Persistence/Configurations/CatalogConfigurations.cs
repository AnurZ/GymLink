using GymLink.Domain.Catalog;
using GymLink.Domain.ReferenceData;
using GymLink.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymLink.Infrastructure.Persistence.Configurations;

internal sealed class GymConfiguration : EntityConfiguration<Gym>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Gym> builder)
    {
        ConfigureTenant(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Address).HasMaxLength(300).IsRequired();
        builder.Property(x => x.PhoneNumber).HasMaxLength(32);
        builder.Property(x => x.Latitude).HasPrecision(9, 6);
        builder.Property(x => x.Longitude).HasPrecision(9, 6);
        builder.Property(x => x.AverageRating).HasPrecision(3, 2);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_Gyms_AverageRating", "[AverageRating] >= 0 AND [AverageRating] <= 5");
            table.HasCheckConstraint("CK_Gyms_ReviewCount", "[ReviewCount] >= 0");
        });
        builder.HasIndex(x => x.TenantId).IsUnique();
        builder.HasIndex(x => new { x.CityId, x.Name });
        builder.HasOne<Tenant>().WithOne().HasForeignKey<Gym>(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<City>().WithMany().HasForeignKey(x => x.CityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GymImageConfiguration : EntityConfiguration<GymImage>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GymImage> builder)
    {
        ConfigureTenant(builder);
        builder.Property(x => x.StorageKey).HasMaxLength(512).IsRequired();
        builder.Property(x => x.PublicUrl).HasMaxLength(2048);
        builder.Property(x => x.AltText).HasMaxLength(300).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.GymId, x.SortOrder }).IsUnique();
        builder.HasIndex(x => new { x.GymId, x.IsPrimary })
            .IsUnique()
            .HasFilter("[IsPrimary] = CAST(1 AS bit)");
        builder.HasOne<Gym>().WithMany().HasForeignKey(x => x.GymId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GymWorkingHoursConfiguration : EntityConfiguration<GymWorkingHours>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GymWorkingHours> builder)
    {
        ConfigureTenant(builder);
        builder.Property(x => x.DayOfWeek).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(x => new { x.TenantId, x.GymId, x.DayOfWeek }).IsUnique();
        builder.HasOne<Gym>().WithMany().HasForeignKey(x => x.GymId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GymEquipmentConfiguration : EntityConfiguration<GymEquipment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GymEquipment> builder)
    {
        ConfigureTenant(builder);
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.HasIndex(x => new { x.TenantId, x.GymId, x.EquipmentId }).IsUnique();
        builder.HasOne<Gym>().WithMany().HasForeignKey(x => x.GymId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Equipment>().WithMany().HasForeignKey(x => x.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GymTrainingTypeConfiguration : EntityConfiguration<GymTrainingType>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GymTrainingType> builder)
    {
        ConfigureTenant(builder);
        builder.HasIndex(x => new { x.TenantId, x.GymId, x.TrainingTypeId }).IsUnique();
        builder.HasOne<Gym>().WithMany().HasForeignKey(x => x.GymId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TrainingType>().WithMany().HasForeignKey(x => x.TrainingTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
