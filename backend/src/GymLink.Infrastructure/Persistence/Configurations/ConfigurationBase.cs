using GymLink.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymLink.Infrastructure.Persistence.Configurations;

internal abstract class EntityConfiguration<T> : IEntityTypeConfiguration<T>
    where T : Entity
{
    public void Configure(EntityTypeBuilder<T> builder)
    {
        builder.HasKey(x => x.Id);
        ConfigureEntity(builder);
    }

    protected abstract void ConfigureEntity(EntityTypeBuilder<T> builder);

    protected static void ConfigureAudit(EntityTypeBuilder<T> builder)
    {
        if (typeof(AuditedEntity).IsAssignableFrom(typeof(T)))
        {
            builder.Property(nameof(AuditedEntity.CreatedAtUtc)).IsRequired();
            builder.Property(nameof(AuditedEntity.CreatedByUserId));
            builder.Property(nameof(AuditedEntity.UpdatedAtUtc));
            builder.Property(nameof(AuditedEntity.UpdatedByUserId));
        }
    }

    protected static void ConfigureTenant(EntityTypeBuilder<T> builder)
    {
        ConfigureAudit(builder);
        builder.Property(nameof(ITenantOwned.TenantId)).IsRequired();
        builder.HasIndex(nameof(ITenantOwned.TenantId));
    }

    protected static void ConfigureConcurrency(EntityTypeBuilder<T> builder)
    {
        builder.Property(nameof(IConcurrencyTracked.RowVersion)).IsRowVersion();
    }
}
