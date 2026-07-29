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
        builder.Property(x => x.ProviderSessionId).HasMaxLength(256);
        builder.Property(x => x.ProviderIntentId).HasMaxLength(256);
        builder.Property(x => x.LastProviderEventId).HasMaxLength(256);
        builder.Property(x => x.FailureCode).HasMaxLength(128);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => x.ProviderSessionId).IsUnique()
            .HasFilter("[ProviderSessionId] IS NOT NULL");
        builder.HasIndex(x => x.ProviderIntentId).IsUnique()
            .HasFilter("[ProviderIntentId] IS NOT NULL");
        builder.HasIndex(x => x.LastProviderEventId).IsUnique()
            .HasFilter("[LastProviderEventId] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.Purpose, x.TargetId })
            .IsUnique()
            .HasFilter("[Status] IN (N'Created', N'Processing', N'Succeeded')");
        builder.HasIndex(x => new { x.Status, x.ExpiresAtUtc })
            .HasFilter("[Status] = N'Processing'");
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_Payments_Amount", "[Amount] > 0");
            table.HasCheckConstraint(
                "CK_Payments_ChargedAmount",
                "[ChargedAmount] IS NULL OR [ChargedAmount] = [Amount]");
        });
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StripeEventReceiptConfiguration :
    EntityConfiguration<StripeEventReceipt>
{
    protected override void ConfigureEntity(EntityTypeBuilder<StripeEventReceipt> builder)
    {
        ConfigureTenant(builder);
        builder.Property(x => x.ProviderEventId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ProviderObjectId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(160).IsRequired();
        builder.HasIndex(x => x.ProviderEventId).IsUnique();
        builder.HasIndex(x => new { x.ProviderObjectId, x.EventType }).IsUnique();
        builder.HasIndex(x => new { x.PaymentId, x.ReceivedAtUtc });
        builder.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId)
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
