using GymLink.Domain.Messaging;
using GymLink.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymLink.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : EntityConfiguration<OutboxMessage>
{
    protected override void ConfigureEntity(EntityTypeBuilder<OutboxMessage> builder)
    {
        ConfigureConcurrency(builder);
        builder.Property(x => x.MessageType).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RoutingKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Payload).HasMaxLength(32000).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(2000);
        builder.HasIndex(x => new
        {
            x.PublishedAtUtc,
            x.NextAttemptAtUtc,
            x.LeasedUntilUtc,
        })
            .HasFilter("[PublishedAtUtc] IS NULL");
        builder.HasIndex(x => new { x.CorrelationId, x.OccurredAtUtc });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_OutboxMessages_ContractVersion",
                "[ContractVersion] > 0");
            table.HasCheckConstraint(
                "CK_OutboxMessages_PublishAttempts",
                "[PublishAttempts] >= 0");
        });
    }
}

internal sealed class InboxMessageConfiguration : EntityConfiguration<InboxMessage>
{
    protected override void ConfigureEntity(EntityTypeBuilder<InboxMessage> builder)
    {
        ConfigureConcurrency(builder);
        builder.Property(x => x.Consumer).HasMaxLength(200).IsRequired();
        builder.Property(x => x.MessageType).HasMaxLength(200).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(2000);
        builder.HasIndex(x => new { x.MessageId, x.Consumer }).IsUnique();
        builder.HasIndex(x => new { x.CompletedAtUtc, x.NextAttemptAtUtc })
            .HasFilter("[CompletedAtUtc] IS NULL");
        builder.ToTable(table =>
            table.HasCheckConstraint(
                "CK_InboxMessages_ProcessingAttempts",
                "[ProcessingAttempts] >= 0"));
    }
}
