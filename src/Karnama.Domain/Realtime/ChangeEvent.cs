namespace Karnama.Domain.Realtime;

/// <summary>
/// One row per change that clients show live (work cards, orders). <see cref="Seq"/> is the change cursor: after a
/// reconnect a client asks for everything after the last cursor it saw (docs/PROMPT.md §8).
/// </summary>
public sealed class ChangeEvent
{
    private ChangeEvent() { Kind = string.Empty; Fields = string.Empty; }

    public ChangeEvent(string kind, Guid entityId, Guid? clientId, Guid? assigneeId, Guid? previousAssigneeId, string fields, DateTimeOffset at)
    {
        Kind = kind;
        EntityId = entityId;
        ClientId = clientId;
        AssigneeId = assigneeId;
        PreviousAssigneeId = previousAssigneeId;
        Fields = fields;
        At = at;
    }

    public long Seq { get; private set; }
    public string Kind { get; private set; }
    public Guid EntityId { get; private set; }
    public Guid? ClientId { get; private set; }
    public Guid? AssigneeId { get; private set; }
    public Guid? PreviousAssigneeId { get; private set; }
    public string Fields { get; private set; }
    public DateTimeOffset At { get; private set; }
}
