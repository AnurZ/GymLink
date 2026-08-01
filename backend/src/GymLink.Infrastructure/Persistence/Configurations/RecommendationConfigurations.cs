using GymLink.Domain.Identity;
using GymLink.Domain.Recommendations;
using GymLink.Domain.ReferenceData;
using GymLink.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymLink.Infrastructure.Persistence.Configurations;

internal sealed class UserPreferenceConfiguration : EntityConfiguration<UserPreference>
{
    protected override void ConfigureEntity(EntityTypeBuilder<UserPreference> builder)
    {
        ConfigureAudit(builder);
        builder.Property(x => x.Weight).HasPrecision(8, 4);
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_UserPreferences_Weight",
            "[Weight] > 0 AND [Weight] <= 1"));
        builder.HasIndex(x => new
        {
            x.UserId,
            x.PreferredCityId,
            x.PreferredTrainingTypeId,
        }).IsUnique();
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<City>().WithMany().HasForeignKey(x => x.PreferredCityId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TrainingType>().WithMany().HasForeignKey(x => x.PreferredTrainingTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ActivityHistoryConfiguration : EntityConfiguration<ActivityHistory>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ActivityHistory> builder)
    {
        builder.Property(x => x.TargetType).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.EventType).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.TargetTenantId, x.TargetType, x.TargetId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.UserId, x.EventType, x.SourceId })
            .IsUnique()
            .HasFilter("[SourceId] IS NOT NULL");
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TargetTenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RecommendationConfiguration : EntityConfiguration<Recommendation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Recommendation> builder)
    {
        builder.Property(x => x.TargetType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Score).HasPrecision(12, 6);
        builder.Property(x => x.AlgorithmVersion).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_Recommendations_Score",
            "[Score] >= 0 AND [Score] <= 1"));
        builder.HasIndex(x => new { x.UserId, x.GeneratedAtUtc });
        builder.HasIndex(x => new { x.TargetTenantId, x.TargetType, x.TargetId });
        builder.HasIndex(x => new { x.UserId, x.TargetType, x.TargetId }).IsUnique();
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TargetTenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
