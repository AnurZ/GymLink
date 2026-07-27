using GymLink.Domain.ReferenceData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymLink.Infrastructure.Persistence.Configurations;

internal sealed class CountryConfiguration : EntityConfiguration<Country>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Country> builder)
    {
        ConfigureAudit(builder);
        builder.Property(x => x.Code).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class CityConfiguration : EntityConfiguration<City>
{
    protected override void ConfigureEntity(EntityTypeBuilder<City> builder)
    {
        ConfigureAudit(builder);
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.HasIndex(x => new { x.CountryId, x.Name }).IsUnique();
        builder.HasOne<Country>().WithMany().HasForeignKey(x => x.CountryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EquipmentConfiguration : EntityConfiguration<Equipment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Equipment> builder)
    {
        ConfigureAudit(builder);
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class TrainingTypeConfiguration : EntityConfiguration<TrainingType>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TrainingType> builder)
    {
        ConfigureAudit(builder);
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.Name).IsUnique();
    }
}
