namespace Karnama.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
}

public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTimeOffset? DeletedAt { get; }
    Guid? DeletedBy { get; }
}

public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
    Guid? UpdatedBy { get; set; }
}

/// <summary>Base for entities with audit fields and soft delete. Audit fields are filled by the DbContext.</summary>
public abstract class AuditableEntity : Entity, IAuditable, ISoftDeletable
{
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }

    protected void MarkDeleted(Guid? actorId, DateTimeOffset now)
    {
        IsDeleted = true;
        DeletedAt = now;
        DeletedBy = actorId;
    }

    protected void MarkRestored()
    {
        IsDeleted = false;
        DeletedAt = null;
        DeletedBy = null;
    }
}
