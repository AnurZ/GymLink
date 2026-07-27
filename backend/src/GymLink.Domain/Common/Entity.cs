namespace GymLink.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
}

public abstract class AuditedEntity : Entity
{
    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

public abstract class TenantEntity : AuditedEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
}

public interface IConcurrencyTracked
{
    byte[] RowVersion { get; set; }
}
