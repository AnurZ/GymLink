using GymLink.Domain.Common;
using GymLink.Domain.Catalog;
using GymLink.Domain.Engagement;
using GymLink.Domain.Messaging;
using GymLink.Domain.Memberships;
using GymLink.Domain.Payments;
using GymLink.Domain.Reservations;
using GymLink.Domain.Tenancy;
using GymLink.Domain.Trainers;
using GymLink.Domain.Identity;
using GymLink.Infrastructure.Identity;
using GymLink.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GymLink.IntegrationTests;

public sealed class EfModelTests
{
    [Fact]
    public void Model_has_explicit_primary_keys_and_tenant_filters()
    {
        using var context = CreateContext(Guid.NewGuid());

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            Assert.NotNull(entityType.FindPrimaryKey());

            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                Assert.NotEmpty(entityType.GetDeclaredQueryFilters());
                Assert.NotNull(entityType.FindProperty(nameof(ITenantOwned.TenantId)));
                Assert.Contains(
                    entityType.GetForeignKeys(),
                    foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Tenant));
            }
        }
    }

    [Fact]
    public void Mutable_roots_use_rowversion_concurrency()
    {
        using var context = CreateContext(Guid.NewGuid());
        var trackedTypes = context.Model.GetEntityTypes()
            .Where(x => typeof(IConcurrencyTracked).IsAssignableFrom(x.ClrType));

        Assert.NotEmpty(trackedTypes);
        foreach (var entityType in trackedTypes)
        {
            var property = entityType.FindProperty(nameof(IConcurrencyTracked.RowVersion));
            Assert.NotNull(property);
            Assert.True(property.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        }
    }

    [Fact]
    public void Trainer_image_metadata_is_nullable_bounded_and_database_constrained()
    {
        using var context = CreateContext(Guid.NewGuid());
        var trainer = context.Model.FindEntityType(typeof(TrainerProfile))!;

        Assert.Equal(500, trainer.FindProperty(nameof(TrainerProfile.ImageStorageKey))!.GetMaxLength());
        Assert.Equal(1000, trainer.FindProperty(nameof(TrainerProfile.ImageUrl))!.GetMaxLength());
        Assert.Equal(32, trainer.FindProperty(nameof(TrainerProfile.ImageContentType))!.GetMaxLength());
        Assert.True(trainer.FindProperty(nameof(TrainerProfile.ImageStorageKey))!.IsNullable);
        Assert.True(trainer.FindProperty(nameof(TrainerProfile.ImageUrl))!.IsNullable);
        Assert.True(trainer.FindProperty(nameof(TrainerProfile.ImageContentType))!.IsNullable);
        Assert.True(trainer.FindProperty(nameof(TrainerProfile.ImageFileSizeBytes))!.IsNullable);

        var designTrainer = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(TrainerProfile))!;
        var constraintNames = designTrainer.GetCheckConstraints().Select(x => x.Name).ToHashSet();
        Assert.Contains("CK_TrainerProfiles_ImageMetadata", constraintNames);
        Assert.Contains("CK_TrainerProfiles_ImageContentType", constraintNames);
        Assert.Contains("CK_TrainerProfiles_ImageFileSize", constraintNames);
    }

    [Fact]
    public void Gym_image_metadata_is_legacy_compatible_bounded_and_concurrency_tracked()
    {
        using var context = CreateContext(Guid.NewGuid());
        var image = context.Model.FindEntityType(typeof(GymImage))!;

        Assert.Equal(512, image.FindProperty(nameof(GymImage.StorageKey))!.GetMaxLength());
        Assert.Equal(2048, image.FindProperty(nameof(GymImage.PublicUrl))!.GetMaxLength());
        Assert.Equal(300, image.FindProperty(nameof(GymImage.AltText))!.GetMaxLength());
        Assert.Equal(32, image.FindProperty(nameof(GymImage.ContentType))!.GetMaxLength());
        Assert.True(image.FindProperty(nameof(GymImage.ContentType))!.IsNullable);
        Assert.True(image.FindProperty(nameof(GymImage.FileSizeBytes))!.IsNullable);
        Assert.True(image.FindProperty(nameof(GymImage.RowVersion))!.IsConcurrencyToken);
        Assert.Equal(
            ValueGenerated.OnAddOrUpdate,
            image.FindProperty(nameof(GymImage.RowVersion))!.ValueGenerated);

        var designImage = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(GymImage))!;
        var constraintNames = designImage.GetCheckConstraints().Select(x => x.Name).ToHashSet();
        Assert.Contains("CK_GymImages_LocalMetadata", constraintNames);
        Assert.Contains("CK_GymImages_ContentType", constraintNames);
        Assert.Contains("CK_GymImages_FileSize", constraintNames);
        Assert.Contains("CK_GymImages_SortOrder", constraintNames);

        Assert.Contains(
            image.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["TenantId", "GymId", "SortOrder"]));
        Assert.Contains(
            image.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["GymId", "IsPrimary"]) &&
                index.GetFilter() is not null);
    }

    [Fact]
    public void Money_columns_have_explicit_precision()
    {
        using var context = CreateContext(Guid.NewGuid());

        AssertPrecision(context, typeof(Payment), nameof(Payment.Amount), 18, 2);
        AssertPrecision(context, typeof(Refund), nameof(Refund.Amount), 18, 2);
        AssertPrecision(context, typeof(Membership), nameof(Membership.Price), 18, 2);
        AssertPrecision(
            context,
            typeof(AppointmentReservation),
            nameof(AppointmentReservation.Price),
            18,
            2);
    }

    [Fact]
    public void Critical_duplicate_protections_exist()
    {
        using var context = CreateContext(Guid.NewGuid());

        Assert.Contains(
            context.Model.FindEntityType(typeof(UserGymAssignment))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["TenantId", "UserId", "Role"]));
        Assert.Contains(
            context.Model.FindEntityType(typeof(UserGymAssignment))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["UserId"]) &&
                index.GetFilter()!.Contains("GymAdmin", StringComparison.Ordinal));
        Assert.Contains(
            context.Model.FindEntityType(typeof(UserGymAssignment))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["TenantId"]) &&
                index.GetFilter()!.Contains("GymAdmin", StringComparison.Ordinal) &&
                index.GetFilter()!.Contains("Active", StringComparison.Ordinal));
        Assert.Contains(
            context.Model.FindEntityType(typeof(GymRegistrationRequest))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["ApplicantUserId"]) &&
                index.GetFilter()!.Contains("Submitted", StringComparison.Ordinal));
        Assert.Contains(
            context.Model.FindEntityType(typeof(MembershipRequest))!.GetIndexes(),
            index => index.IsUnique && index.GetFilter() is not null);
        Assert.Contains(
            context.Model.FindEntityType(typeof(Membership))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["TenantId", "MemberUserId", "GymId"]) &&
                index.GetFilter()!.Contains("PendingPayment", StringComparison.Ordinal) &&
                index.GetFilter()!.Contains("Active", StringComparison.Ordinal) &&
                index.GetFilter()!.Contains("Suspended", StringComparison.Ordinal));
        Assert.Contains(
            context.Model.FindEntityType(typeof(Review))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["ReservationId"]));
        Assert.Contains(
            context.Model.FindEntityType(typeof(GymReview))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(
                    ["TenantId", "GymId", "ReviewerUserId"]));
        Assert.Contains(
            context.Model.FindEntityType(typeof(AppointmentReservation))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["AvailabilitySlotId"]) &&
                index.GetFilter()!.Contains("IS NOT NULL", StringComparison.Ordinal) &&
                index.GetFilter()!.Contains("Pending", StringComparison.Ordinal) &&
                index.GetFilter()!.Contains("Confirmed", StringComparison.Ordinal));
        Assert.Contains(
            context.Model.FindEntityType(typeof(TrainerWeeklyShift))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(
                    [
                        "TenantId",
                        "TrainerAvailabilityScheduleId",
                        "TrainerProfileId",
                        "DayOfWeek",
                        "Period",
                    ]));
        Assert.Contains(
            context.Model.FindEntityType(typeof(TrainerAvailabilitySchedule))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["TenantId", "TrainerProfileId"]));
        Assert.Contains(
            context.Model.FindEntityType(typeof(Notification))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["SourceMessageId"]) &&
                index.GetFilter()!.Contains("IS NOT NULL", StringComparison.Ordinal));
        Assert.Contains(
            context.Model.FindEntityType(typeof(Conversation))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(
                    ["TenantId", "MemberUserId", "TrainerUserId"]));
        Assert.Contains(
            context.Model.FindEntityType(typeof(ConversationParticipant))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["ConversationId", "UserId"]));
        Assert.Contains(
            context.Model.FindEntityType(typeof(Message))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(
                    ["ConversationId", "SenderUserId", "ClientMessageId"]));
        Assert.Contains(
            context.Model.FindEntityType(typeof(PasswordResetChallenge))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["UserId"]) &&
                index.GetFilter()!.Contains("ConsumedAtUtc", StringComparison.Ordinal) &&
                index.GetFilter()!.Contains("SupersededAtUtc", StringComparison.Ordinal));
        Assert.Contains(
            context.Model.FindEntityType(typeof(InboxMessage))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["MessageId", "Consumer"]));
        Assert.Contains(
            context.Model.FindEntityType(typeof(Payment))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["TenantId", "Purpose", "TargetId"]) &&
                index.GetFilter()!.Contains("Created", StringComparison.Ordinal) &&
                index.GetFilter()!.Contains("Processing", StringComparison.Ordinal) &&
                index.GetFilter()!.Contains("Succeeded", StringComparison.Ordinal));
        Assert.Contains(
            context.Model.FindEntityType(typeof(StripeEventReceipt))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["ProviderEventId"]));
        Assert.Contains(
            context.Model.FindEntityType(typeof(StripeEventReceipt))!.GetIndexes(),
            index => index.IsUnique &&
                PropertyNames(index).SequenceEqual(["ProviderObjectId", "EventType"]));
    }

    [Fact]
    public void Phase9_chat_model_has_pair_identity_read_state_and_restrictive_history()
    {
        using var context = CreateContext(Guid.NewGuid());

        var conversation = context.Model.FindEntityType(typeof(Conversation))!;
        Assert.False(conversation.FindProperty(nameof(Conversation.MemberUserId))!.IsNullable);
        Assert.False(conversation.FindProperty(nameof(Conversation.TrainerUserId))!.IsNullable);
        Assert.True(conversation.FindProperty(nameof(Conversation.LastMessageAtUtc))!.IsNullable);
        Assert.Equal(
            64,
            conversation.FindProperty(nameof(Conversation.Type))!.GetMaxLength());
        Assert.Equal(
            2000,
            context.Model.FindEntityType(typeof(Message))!
                .FindProperty(nameof(Message.Text))!
                .GetMaxLength());
        Assert.True(
            context.Model.FindEntityType(typeof(ConversationParticipant))!
                .FindProperty(nameof(ConversationParticipant.LastReadAtUtc))!
                .IsNullable);

        Assert.All(
            conversation.GetForeignKeys()
                .Where(x =>
                    x.PrincipalEntityType.ClrType is
                    { } type &&
                    (type == typeof(UserProfile) ||
                     type == typeof(AppointmentReservation) ||
                     type == typeof(Tenant))),
            foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));

        var message = context.Model.FindEntityType(typeof(Message))!;
        Assert.True(message.FindProperty(nameof(Message.ImageStorageKey))!.IsNullable);
        Assert.Equal(
            Message.MaximumImageStorageKeyLength,
            message.FindProperty(nameof(Message.ImageStorageKey))!.GetMaxLength());
        Assert.True(message.FindProperty(nameof(Message.ImageContentType))!.IsNullable);
        Assert.Equal(
            32,
            message.FindProperty(nameof(Message.ImageContentType))!.GetMaxLength());
        Assert.True(message.FindProperty(nameof(Message.ImageFileSizeBytes))!.IsNullable);
        var designMessage = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(Message))!;
        var constraintNames = designMessage.GetCheckConstraints()
            .Select(x => x.Name)
            .ToHashSet();
        Assert.Contains("CK_Messages_ImageMetadata", constraintNames);
        Assert.Contains("CK_Messages_ImageContentType", constraintNames);
        Assert.Contains("CK_Messages_ImageFileSize", constraintNames);
        Assert.Contains(
            message.GetIndexes(),
            index => PropertyNames(index).SequenceEqual(
                ["ConversationId", "SentAtUtc", "Id"]));
        Assert.Contains(
            message.GetForeignKeys(),
            foreignKey =>
                foreignKey.PrincipalEntityType.ClrType == typeof(Conversation) &&
                foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
    }

    [Fact]
    public void Phase8_payment_model_has_reviewable_deadlines_and_restrictive_links()
    {
        using var context = CreateContext(Guid.NewGuid());

        Assert.True(
            context.Model.FindEntityType(typeof(Membership))!
                .FindProperty(nameof(Membership.StartsAtUtc))!
                .IsNullable);
        Assert.True(
            context.Model.FindEntityType(typeof(Membership))!
                .FindProperty(nameof(Membership.EndsAtUtc))!
                .IsNullable);
        Assert.True(
            context.Model.FindEntityType(typeof(AppointmentReservation))!
                .FindProperty(nameof(AppointmentReservation.PaymentDueAtUtc))!
                .IsNullable);

        var receipt = context.Model.FindEntityType(typeof(StripeEventReceipt))!;
        Assert.Contains(
            receipt.GetForeignKeys(),
            foreignKey =>
                foreignKey.PrincipalEntityType.ClrType == typeof(Payment) &&
                foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
    }

    [Fact]
    public void Durable_delivery_entities_have_bounded_retry_indexes_and_restrictive_user_links()
    {
        using var context = CreateContext(Guid.NewGuid());

        var outbox = context.Model.FindEntityType(typeof(OutboxMessage))!;
        Assert.Equal(32000, outbox.FindProperty(nameof(OutboxMessage.Payload))!.GetMaxLength());
        Assert.Contains(
            outbox.GetIndexes(),
            index =>
                PropertyNames(index).SequenceEqual(
                    ["PublishedAtUtc", "NextAttemptAtUtc", "LeasedUntilUtc"]) &&
                index.GetFilter()!.Contains("PublishedAtUtc", StringComparison.Ordinal));

        var inbox = context.Model.FindEntityType(typeof(InboxMessage))!;
        Assert.Contains(
            inbox.GetIndexes(),
            index =>
                PropertyNames(index).SequenceEqual(["CompletedAtUtc", "NextAttemptAtUtc"]) &&
                index.GetFilter()!.Contains("CompletedAtUtc", StringComparison.Ordinal));

        var challenge = context.Model.FindEntityType(typeof(PasswordResetChallenge))!;
        Assert.Contains(
            challenge.GetForeignKeys(),
            foreignKey =>
                foreignKey.PrincipalEntityType.ClrType == typeof(UserProfile) &&
                foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
    }

    [Fact]
    public void Identity_uses_explicit_tables_and_shared_profile_key()
    {
        using var context = CreateContext(Guid.NewGuid());

        Assert.Equal(
            "IdentityUsers",
            context.Model.FindEntityType(typeof(GymLinkIdentityUser))!.GetTableName());
        Assert.Equal(
            "IdentityRoles",
            context.Model.FindEntityType(typeof(IdentityRole<Guid>))!.GetTableName());
        Assert.Equal(
            "IdentityUserRoles",
            context.Model.FindEntityType(typeof(IdentityUserRole<Guid>))!.GetTableName());

        var emailIndex = context.Model.FindEntityType(typeof(GymLinkIdentityUser))!
            .GetIndexes()
            .Single(index => PropertyNames(index).SequenceEqual(["NormalizedEmail"]));
        Assert.True(emailIndex.IsUnique);

        var profile = context.Model.FindEntityType(typeof(UserProfile))!;
        Assert.Contains(
            profile.GetForeignKeys(),
            foreignKey =>
                foreignKey.PrincipalEntityType.ClrType == typeof(GymLinkIdentityUser) &&
                PropertyNames(foreignKey.PrincipalKey).SequenceEqual(["Id"]) &&
                foreignKey.Properties.Select(x => x.Name).SequenceEqual(["Id"]));
    }

    private static GymLinkDbContext CreateContext(Guid? tenantId)
    {
        var options = new DbContextOptionsBuilder<GymLinkDbContext>()
            .UseSqlServer(TestSqlServer.ConnectionString("GymLinkModelOnly"))
            .Options;
        return new GymLinkDbContext(options, new TestTenantContext(tenantId));
    }

    private static void AssertPrecision(
        GymLinkDbContext context,
        Type entityType,
        string propertyName,
        int precision,
        int scale)
    {
        var property = context.Model.FindEntityType(entityType)!.FindProperty(propertyName)!;
        Assert.Equal(precision, property.GetPrecision());
        Assert.Equal(scale, property.GetScale());
    }

    private static IEnumerable<string> PropertyNames(IReadOnlyIndex index) =>
        index.Properties.Select(x => x.Name);

    private static IEnumerable<string> PropertyNames(IReadOnlyKey key) =>
        key.Properties.Select(x => x.Name);
}
