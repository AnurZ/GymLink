using GymLink.Domain.Engagement;
using GymLink.Domain.Identity;
using GymLink.Domain.Reservations;
using GymLink.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymLink.Infrastructure.Persistence.Configurations;

internal sealed class ConversationConfiguration : EntityConfiguration<Conversation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Conversation> builder)
    {
        ConfigureTenant(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.Type).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new
        {
            x.TenantId,
            x.MemberUserId,
            x.TrainerUserId,
        })
            .IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.LastMessageAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.ReservationId });
        builder.HasOne<AppointmentReservation>().WithMany().HasForeignKey(x => x.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.MemberUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.TrainerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConversationParticipantConfiguration : EntityConfiguration<ConversationParticipant>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ConversationParticipant> builder)
    {
        ConfigureTenant(builder);
        builder.HasIndex(x => new { x.ConversationId, x.UserId }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.LeftAtUtc });
        builder.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MessageConfiguration : EntityConfiguration<Message>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Message> builder)
    {
        ConfigureTenant(builder);
        builder.Property(x => x.Text).HasMaxLength(Message.MaximumTextLength).IsRequired();
        builder.Property(x => x.ImageStorageKey)
            .HasMaxLength(Message.MaximumImageStorageKeyLength);
        builder.Property(x => x.ImageContentType).HasMaxLength(32);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_Messages_ImageMetadata",
                "([ImageStorageKey] IS NULL AND [ImageContentType] IS NULL AND " +
                "[ImageFileSizeBytes] IS NULL) OR " +
                "([ImageStorageKey] IS NOT NULL AND [ImageContentType] IS NOT NULL AND " +
                "[ImageFileSizeBytes] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_Messages_ImageContentType",
                "[ImageContentType] IS NULL OR [ImageContentType] IN " +
                "('image/jpeg', 'image/png', 'image/webp')");
            table.HasCheckConstraint(
                "CK_Messages_ImageFileSize",
                $"[ImageFileSizeBytes] IS NULL OR ([ImageFileSizeBytes] > 0 AND " +
                $"[ImageFileSizeBytes] <= {Message.MaximumImageFileSizeBytes})");
        });
        builder.HasIndex(x => new { x.ConversationId, x.SentAtUtc, x.Id });
        builder.HasIndex(x => new
        {
            x.ConversationId,
            x.SenderUserId,
            x.ClientMessageId,
        })
            .IsUnique();
        builder.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class NotificationConfiguration : EntityConfiguration<Notification>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Notification> builder)
    {
        ConfigureAudit(builder);
        ConfigureConcurrency(builder);
        builder.Property(x => x.Type).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Text).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.TargetType).HasMaxLength(100);
        builder.HasIndex(x => new { x.RecipientUserId, x.ReadAtUtc, x.CreatedAtUtc });
        builder.HasIndex(x => x.SourceMessageId)
            .IsUnique()
            .HasFilter("[SourceMessageId] IS NOT NULL");
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
