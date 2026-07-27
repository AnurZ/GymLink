using GymLink.Domain.Identity;
using GymLink.Domain.Payments;
using GymLink.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymLink.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : EntityConfiguration<Payment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Payment> builder)
    {
        ConfigureTenant(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.ChargedAmount).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ProviderIntentId).HasMaxLength(256);
        builder.Property(x => x.LastProviderEventId).HasMaxLength(256);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => x.ProviderIntentId).IsUnique()
            .HasFilter("[ProviderIntentId] IS NOT NULL");
        builder.HasIndex(x => x.LastProviderEventId).IsUnique()
            .HasFilter("[LastProviderEventId] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.Purpose, x.TargetId, x.Status });
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RefundConfiguration : EntityConfiguration<Refund>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Refund> builder)
    {
        ConfigureTenant(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ProviderRefundId).HasMaxLength(256);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => x.ProviderRefundId).IsUnique()
            .HasFilter("[ProviderRefundId] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.PaymentId });
        builder.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
