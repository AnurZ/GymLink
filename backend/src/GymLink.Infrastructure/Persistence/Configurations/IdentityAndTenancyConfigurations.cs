using GymLink.Domain.Identity;
using GymLink.Domain.ReferenceData;
using GymLink.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymLink.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : EntityConfiguration<ApplicationUser>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ApplicationUser> builder)
    {
        ConfigureAudit(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.Email).HasMaxLength(320).IsRequired();
        builder.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.PhoneNumber).HasMaxLength(32);
        builder.Property(x => x.ImageStorageKey).HasMaxLength(512);
        builder.HasIndex(x => x.NormalizedEmail).IsUnique();
    }
}

internal sealed class RefreshTokenSessionConfiguration : EntityConfiguration<RefreshTokenSession>
{
    protected override void ConfigureEntity(EntityTypeBuilder<RefreshTokenSession> builder)
    {
        ConfigureAudit(builder);
        builder.Property(x => x.TokenHash).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Jti).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.Jti).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RefreshTokenSession>().WithMany().HasForeignKey(x => x.ReplacedBySessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TenantConfiguration : EntityConfiguration<Tenant>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Tenant> builder)
    {
        ConfigureAudit(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.StatusReason).HasMaxLength(1000);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StatusChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GymRegistrationRequestConfiguration : EntityConfiguration<GymRegistrationRequest>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GymRegistrationRequest> builder)
    {
        ConfigureAudit(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.ProposedGymName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ProposedAddress).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.DecisionReason).HasMaxLength(1000);
        builder.HasIndex(x => new { x.ApplicantUserId, x.Status });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApplicantUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.DecidedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<City>().WithMany().HasForeignKey(x => x.CityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.CreatedTenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserGymAssignmentConfiguration : EntityConfiguration<UserGymAssignment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<UserGymAssignment> builder)
    {
        ConfigureTenant(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.Role).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.UserId, x.Role }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
