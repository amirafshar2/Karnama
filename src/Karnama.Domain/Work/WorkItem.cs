using Karnama.Domain.Catalog;
using Karnama.Domain.Common;

namespace Karnama.Domain.Work;

/// <summary>Kanban columns. <see cref="Waiting"/> = an earlier stage isn't finished yet.</summary>
public enum WorkStatus { Waiting, Todo, InProgress, InReview, ChangesRequested, ClientReview, Done, Cancelled }

/// <summary>Who is asking for a transition; the domain decides what each may do.</summary>
public enum WorkActor { Assignee, Manager, Client }

/// <summary>One stage of one order (e.g. "edit the reel"), assigned to one person.</summary>
public sealed class WorkItem : AuditableEntity
{
    private WorkItem() { StageName = new LocalizedText(); Title = string.Empty; }

    public Guid OrderId { get; private set; }
    public long OrderNumber { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid ServiceTypeId { get; private set; }
    public Guid? StageId { get; private set; }
    public int StageOrder { get; private set; }
    public int StageCount { get; private set; }
    public LocalizedText StageName { get; private set; }
    public string Title { get; private set; }
    public Guid? RoleId { get; private set; }
    public Guid? AssigneeId { get; private set; }
    public WorkStatus Status { get; private set; }
    public OrderPriority Priority { get; private set; }
    public int EstimatedMinutes { get; private set; }
    public DateTimeOffset? Deadline { get; private set; }
    public bool NeedsAdminReview { get; private set; }
    public bool NeedsClientApproval { get; private set; }
    /// <summary>Order inside a kanban column (fractional, so a move rewrites one row).</summary>
    public double Rank { get; private set; }
    public int RevisionCount { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    /// <summary>First hand-in (reports: "on time" is the worker's first delivery, not a later fix after feedback).</summary>
    public DateTimeOffset? FirstSubmittedAt { get; private set; }
    /// <summary>Optional 1–5 stars from a manager on finished work, and from the client when approving (PROMPT2 §11).</summary>
    public byte? ManagerStars { get; private set; }
    public byte? ClientStars { get; private set; }
    public DateTimeOffset? DoneAt { get; private set; }
    public uint Version { get; private set; }

    public bool IsOpen => Status is not (WorkStatus.Done or WorkStatus.Cancelled);

    public static WorkItem ForStage(Order order, WorkflowStage stage, int stageCount, DateTimeOffset? deadline, double rank) => new()
    {
        OrderId = order.Id,
        OrderNumber = order.Number,
        ClientId = order.ClientId,
        ServiceTypeId = order.ServiceTypeId,
        StageId = stage.Id,
        StageOrder = stage.Order,
        StageCount = stageCount,
        StageName = stage.Name,
        Title = order.Title,
        RoleId = stage.RoleId,
        Status = stage.Order == 1 ? WorkStatus.Todo : WorkStatus.Waiting,
        Priority = order.Priority,
        EstimatedMinutes = stage.EstimatedMinutes,
        Deadline = deadline,
        NeedsAdminReview = stage.NeedsAdminReview,
        NeedsClientApproval = stage.NeedsClientApproval,
        Rank = rank,
    };

    public Result Assign(Guid? userId)
    {
        if (!IsOpen) return Error.Conflict("work.closed", "این کار بسته شده است.");
        AssigneeId = userId;
        return Result.Success();
    }

    public void SetDeadline(DateTimeOffset? deadline) => Deadline = deadline;

    /// <summary>
    /// Previous stage finished: this one is ready to start.
    /// Fairness rule (PROMPT2 §11: work held up by client delay must not count against the worker):
    /// this stage's deadline was planned at accept time, assuming it could start at <paramref name="plannedStart"/>
    /// (the end of the earlier stages' planned window). If it unlocks later than that — typically because the client
    /// sat on an earlier stage's approval — the whole window moves forward by the delay, so the next worker still
    /// gets the full time the plan gave this stage. Unlocking early never pulls a deadline in.
    /// </summary>
    public void Unlock(DateTimeOffset now, DateTimeOffset? plannedStart)
    {
        if (Status != WorkStatus.Waiting) return;
        Status = WorkStatus.Todo;
        if (Deadline is { } d && plannedStart is { } start && now > start)
            Deadline = d + (now - start);
    }

    /// <summary>Status only, deadlines untouched (demo seeding builds history with its own dates).</summary>
    public void Unlock() => Unlock(DateTimeOffset.MinValue, null);

    /// <summary>
    /// The one place that decides who may move a card where. Assignees follow the workflow
    /// (start → submit); managers may also review and override; clients only approve or ask for changes.
    /// </summary>
    public Result MoveTo(WorkStatus target, WorkActor actor, DateTimeOffset now, double? rank = null)
    {
        if (target == Status)
        {
            if (rank is { } r && actor == WorkActor.Manager) Rank = r;
            return Result.Success();
        }
        var check = CanMoveTo(target, actor);
        if (check.IsFailure) return check;

        if (target == WorkStatus.ChangesRequested) RevisionCount++;
        if (target == WorkStatus.InProgress && StartedAt is null) StartedAt = now;
        if (target is WorkStatus.InReview or WorkStatus.ClientReview) SubmittedAt = now;
        // Straight to Done (no review step) also counts as the hand-in.
        if (target is WorkStatus.InReview or WorkStatus.ClientReview || (target == WorkStatus.Done && actor == WorkActor.Assignee))
            FirstSubmittedAt ??= now;
        DoneAt = target == WorkStatus.Done ? now : null;
        Status = target;
        if (rank is { } nr) Rank = nr;
        return Result.Success();
    }

    /// <summary>The rules alone, without changing anything (used to show only the buttons a person may press).</summary>
    public Result CanMoveTo(WorkStatus target, WorkActor actor)
    {
        if (target == Status) return Result.Success();
        if (!IsOpen && actor != WorkActor.Manager) return Error.Conflict("work.closed", "این کار بسته شده است.");
        if (target is WorkStatus.Waiting or WorkStatus.Cancelled) return Error.Validation("work.badTarget", "به این ستون نمی‌شود منتقل کرد.");
        if (Status == WorkStatus.Waiting && actor != WorkActor.Manager)
            return Error.Conflict("work.waiting", "مرحله‌ی قبل هنوز تمام نشده است.");

        var allowed = (actor, Status, target) switch
        {
            (WorkActor.Assignee, WorkStatus.Todo or WorkStatus.ChangesRequested, WorkStatus.InProgress) => true,
            (WorkActor.Assignee, WorkStatus.InProgress, WorkStatus.Todo) => true,   // pause / put back
            (WorkActor.Assignee, WorkStatus.InProgress, WorkStatus.InReview or WorkStatus.ClientReview or WorkStatus.Done) => target == NextAfterWork(),
            (WorkActor.Client, WorkStatus.ClientReview, WorkStatus.Done or WorkStatus.ChangesRequested) => true,
            (WorkActor.Manager, _, _) => true,
            _ => false,
        };
        return allowed ? Result.Success() : Error.Forbidden("work.transition", "این جابه‌جایی برای شما مجاز نیست.");
    }

    /// <summary>Where finished work goes: admin review, else client approval, else done.</summary>
    public WorkStatus NextAfterWork() => NeedsAdminReview ? WorkStatus.InReview : NeedsClientApproval ? WorkStatus.ClientReview : WorkStatus.Done;

    /// <summary>Where an approved review goes.</summary>
    public WorkStatus NextAfterReview() => NeedsClientApproval ? WorkStatus.ClientReview : WorkStatus.Done;

    public void Cancel() { if (IsOpen) Status = WorkStatus.Cancelled; }

    public Result RateByManager(int stars)
    {
        if (Status != WorkStatus.Done) return Error.Conflict("work.notDone", "فقط کار تمام‌شده امتیاز می‌گیرد.");
        if (stars is < 1 or > 5) return Error.Validation("work.stars", "امتیاز ۱ تا ۵ ستاره است.");
        ManagerStars = (byte)stars;
        return Result.Success();
    }

    public Result RateByClient(int stars)
    {
        if (stars is < 1 or > 5) return Error.Validation("work.stars", "امتیاز ۱ تا ۵ ستاره است.");
        ClientStars = (byte)stars;
        return Result.Success();
    }

    public void Delete(Guid? actor, DateTimeOffset now) => MarkDeleted(actor, now);
}

public enum CommentVisibility { Internal, Client }

/// <summary>Comment on a work item. Internal comments never reach the client portal.</summary>
public sealed class WorkComment : Entity
{
    public const int MaxBody = 4000;
    private WorkComment() { Body = string.Empty; AuthorName = string.Empty; }

    public Guid WorkItemId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid? AuthorId { get; private set; }
    public string AuthorName { get; private set; }
    public bool FromClient { get; private set; }
    public CommentVisibility Visibility { get; private set; }
    public string Body { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<WorkComment> Create(WorkItem item, Guid? authorId, string authorName, bool fromClient, CommentVisibility visibility, string body, DateTimeOffset now)
    {
        body = body.Trim();
        if (body.Length is 0 or > MaxBody) return Error.Validation("comments.body", "متن پیام باید بین ۱ تا ۴۰۰۰ کاراکتر باشد.");
        return new WorkComment
        {
            WorkItemId = item.Id, OrderId = item.OrderId, ClientId = item.ClientId, AuthorId = authorId, AuthorName = authorName,
            FromClient = fromClient, Visibility = fromClient ? CommentVisibility.Client : visibility, Body = body, CreatedAt = now,
        };
    }
}
