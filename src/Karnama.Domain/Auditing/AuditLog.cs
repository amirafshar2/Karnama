namespace Karnama.Domain.Auditing;

/// <summary>Append-only audit trail. UPDATE and DELETE are blocked by a database trigger.</summary>
public sealed class AuditLog
{
    private AuditLog() { Action = string.Empty; EntityType = string.Empty; }

    public long Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid? ActorId { get; private set; }
    public bool IsDemo { get; private set; }
    public string Action { get; private set; }
    public string EntityType { get; private set; }
    public string? EntityId { get; private set; }
    public string? DataJson { get; private set; }
    public string? TraceId { get; private set; }

    public static AuditLog Record(DateTimeOffset now, Guid? actorId, bool isDemo, string action, string entityType, string? entityId, string? dataJson, string? traceId) => new()
    {
        OccurredAt = now,
        ActorId = actorId,
        IsDemo = isDemo,
        Action = action,
        EntityType = entityType,
        EntityId = entityId,
        DataJson = dataJson,
        TraceId = traceId,
    };
}

public static class AuditActions
{
    public const string Login = "auth.login";
    public const string DemoLogin = "auth.login.demo";
    public const string LoginFailed = "auth.login.failed";
    public const string RefreshReuse = "auth.refresh.reuse";
    public const string TwoFactorEnabled = "auth.2fa.enabled";
    public const string Setup = "setup.completed";
    public const string RoleCreated = "roles.created";
    public const string RoleUpdated = "roles.updated";
    public const string RoleDeleted = "roles.deleted";
    public const string RoleRestored = "roles.restored";
}
