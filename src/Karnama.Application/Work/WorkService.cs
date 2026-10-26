using Karnama.Application.Abstractions;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Karnama.Domain.Work;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Work;

public sealed record WorkCardDto(
    Guid Id, Guid OrderId, long OrderNumber, string Title, Guid ClientId, string ClientName, string ServiceName, string StageName,
    int StageOrder, int StageCount, WorkStatus Status, OrderPriority Priority, Guid? AssigneeId, string? AssigneeName,
    DateTimeOffset? Deadline, int EstimatedMinutes, int RevisionCount, int Comments, int Files, double Rank, uint Version);

public sealed record CommentDto(Guid Id, string AuthorName, bool FromClient, CommentVisibility Visibility, string Body, DateTimeOffset CreatedAt, bool Mine);

public sealed record WorkDetailDto(WorkCardDto Card, OrderDto Order, IReadOnlyList<CommentDto> Comments, IReadOnlyList<Files.FileDto> Files,
    IReadOnlyList<WorkStatus> AllowedMoves, bool CanAssign, bool CanTalkToClient, int? ManagerStars = null, int? ClientStars = null, bool CanRate = false);

public sealed record BoardFilter(Guid? ClientId, Guid? AssigneeId, Guid? ServiceTypeId, bool Mine, bool Overdue);

public sealed record MoveRequest(WorkStatus Status, double? Rank, uint Version, string? Note);
public sealed record AssignRequest(Guid? UserId, uint Version);
public sealed record CommentRequest(string Body, CommentVisibility Visibility);
public sealed record AssigneeSuggestionDto(Guid UserId, string Name, int OpenItems, int OpenMinutes, bool HasStageRole);

/// <summary>Work items: the live kanban, moving cards (the domain decides who may move what), assigning, comments.</summary>
public sealed class WorkService(IAppDbContext db, ICurrentUser me, WorkAccess access, OrderService orders, IAuditWriter audit, TimeProvider clock, IChangeFeed changes, Notify.Notifier notifier)
{
    public const int BoardLimit = 500;

    public async Task<IReadOnlyList<WorkCardDto>> BoardAsync(BoardFilter f, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-14);
        var q = access.VisibleItems().AsNoTracking()
            .Where(w => w.Status != WorkStatus.Cancelled && (w.Status != WorkStatus.Done || w.DoneAt > since));
        if (f.ClientId is { } c) q = q.Where(w => w.ClientId == c);
        if (f.AssigneeId is { } a) q = q.Where(w => w.AssigneeId == a);
        if (f.ServiceTypeId is { } s) q = q.Where(w => w.ServiceTypeId == s);
        if (f.Mine) { var uid = access.Me; q = q.Where(w => w.AssigneeId == uid); }
        if (f.Overdue) { var now = clock.GetUtcNow(); q = q.Where(w => w.Deadline < now && w.Status != WorkStatus.Done); }
        var items = await q.OrderBy(w => w.Rank).Take(BoardLimit).ToListAsync(ct);
        return await CardsAsync(items, ct);
    }

    public async Task<Result<WorkDetailDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var w = await access.VisibleItems().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return Error.NotFound("work.notFound", "کار پیدا نشد.");
        var order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == w.OrderId, ct);
        var comments = await db.WorkComments.AsNoTracking().Where(c => c.WorkItemId == id).OrderBy(c => c.CreatedAt).ToListAsync(ct);
        var files = await db.Files.AsNoTracking().Where(f => f.WorkItemId == id && f.Status == Domain.Files.FileStatus.Ready).OrderBy(f => f.CreatedAt).ToListAsync(ct);
        var actor = ActorFor(w);
        var moves = actor is null ? [] : Enum.GetValues<WorkStatus>().Where(s => s is not (WorkStatus.Waiting or WorkStatus.Cancelled) && s != w.Status
            && CanMove(w, s, actor.Value)).ToList();
        return new WorkDetailDto((await CardsAsync([w], ct))[0], await orders.ToDtoAsync(order, ct),
            comments.Select(c => new CommentDto(c.Id, c.AuthorName, c.FromClient, c.Visibility, c.Body, c.CreatedAt, c.AuthorId == access.Me)).ToList(),
            files.Select(Files.FileService.ToDto).ToList(), moves, access.CanAssign, access.CanTalkToClient,
            w.ManagerStars, w.ClientStars, w.Status == WorkStatus.Done && me.Has(Permissions.WorkReview, PermissionScope.All) && w.AssigneeId != me.UserId);
    }

    private WorkActor? ActorFor(WorkItem w) =>
        access.IsManager ? WorkActor.Manager : access.CanRun && w.AssigneeId == access.Me ? WorkActor.Assignee : null;

    private static bool CanMove(WorkItem w, WorkStatus target, WorkActor actor) => w.CanMoveTo(target, actor).IsSuccess;

    /// <summary>A manager's 1–5 stars on finished work (feeds the worker score; PROMPT2 §11).</summary>
    public async Task<Result> RateAsync(Guid id, int stars, CancellationToken ct)
    {
        if (!me.Has(Permissions.WorkReview, PermissionScope.All)) return Error.Forbidden();
        var w = await db.WorkItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return Error.NotFound("work.notFound", "کار پیدا نشد.");
        // Stars feed the assignee's score: nobody rates their own work.
        if (w.AssigneeId == me.UserId) return Error.Forbidden("work.rateSelf", "به کار خودتان نمی‌توانید امتیاز بدهید.");
        var r = w.RateByManager(stars);
        if (r.IsFailure) return r;
        audit.Add("work.rated", nameof(WorkItem), w.Id.ToString(), new { stars });
        changes.Record("work", w.Id, null, w.AssigneeId, null, "rating");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return Result.Success();
    }

    public async Task<Result<WorkCardDto>> MoveAsync(Guid id, MoveRequest r, CancellationToken ct)
    {
        var w = await access.VisibleItems().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return Error.NotFound("work.notFound", "کار پیدا نشد.");
        if (ActorFor(w) is not { } actor) return Error.Forbidden("work.notYours", "این کار به شما سپرده نشده است.");
        db.SetOriginalVersion(w, r.Version);
        var from = w.Status;
        var moved = w.MoveTo(r.Status, actor, clock.GetUtcNow(), actor == WorkActor.Manager ? r.Rank : null);
        if (moved.IsFailure) return moved.Error!;
        db.MarkModified(w);

        if (from != w.Status)
        {
            if (w.Status == WorkStatus.ClientReview)
            {
                // "Send to client" = the client can now see this stage's deliverables.
                foreach (var f in await db.Files.Where(f => f.WorkItemId == w.Id && f.Purpose == Domain.Files.FilePurpose.Deliverable && !f.ClientVisible).ToListAsync(ct))
                    f.ShareWithClient(true);
            }
            if (!string.IsNullOrWhiteSpace(r.Note) && w.Status == WorkStatus.ChangesRequested)
            {
                var c = WorkComment.Create(w, me.UserId, await MyNameAsync(ct), false, CommentVisibility.Internal, r.Note, clock.GetUtcNow());
                if (c.IsSuccess) db.WorkComments.Add(c.Value!);
            }
            await AfterStatusChangeAsync(w, from, ct);
            await NotifyMoveAsync(w, ct);
            audit.Add("work.moved", nameof(WorkItem), id.ToString(), new { from, to = w.Status });
        }
        changes.Record("work", w.Id, w.ClientId, w.AssigneeId, null, "status", "rank");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return (await CardsAsync([w], ct))[0];
    }

    private async Task NotifyMoveAsync(WorkItem w, CancellationToken ct)
    {
        var link = $"/work?open={w.Id}";
        var what = $"{w.Title} — {w.StageName.Get("fa")}";
        var key = $"move:{w.Id:N}:{w.Status}:{w.RevisionCount}";
        switch (w.Status)
        {
            case WorkStatus.InReview:
                await notifier.NotifyAsync(await notifier.StaffWithAsync(Permissions.WorkReview, PermissionScope.All, ct), "work.review", "کاری برای بازبینی رسید", what, link, false, key, ct);
                break;
            case WorkStatus.ChangesRequested when w.AssigneeId is { } a:
                await notifier.NotifyAsync([a], "work.changes", "این کار نیاز به اصلاح دارد", what, link, false, key, ct);
                break;
            case WorkStatus.ClientReview when await notifier.PortalUserAsync(w.ClientId, ct) is { } portal:
                await notifier.NotifyAsync([portal], "portal.approval", "محتوای جدید منتظر تأیید شماست", w.Title, "/dashboard", true, key, ct);
                break;
        }
    }

    /// <summary>Done → the next stage unlocks; all done → the order is complete. Reopened → the order is open again.</summary>
    private async Task AfterStatusChangeAsync(WorkItem w, WorkStatus from, CancellationToken ct)
    {
        var siblings = await db.WorkItems.Where(x => x.OrderId == w.OrderId && x.Id != w.Id && x.Status != WorkStatus.Cancelled).ToListAsync(ct);
        var order = await db.Orders.FirstAsync(o => o.Id == w.OrderId, ct);
        if (w.Status == WorkStatus.Done)
        {
            var next = siblings.Where(x => x.StageOrder > w.StageOrder && x.Status == WorkStatus.Waiting).OrderBy(x => x.StageOrder).FirstOrDefault();
            if (next is not null)
            {
                next.Unlock(clock.GetUtcNow(), PlannedStart(next, siblings.Append(w), order.AcceptedAt));
                changes.Record("work", next.Id, next.ClientId, next.AssigneeId, null, "status");
            }
            if (siblings.All(x => x.Status == WorkStatus.Done))
            {
                order.Complete(clock.GetUtcNow());
                changes.Record("order", order.Id, order.ClientId, null, null, "status");
            }
        }
        else if (from == WorkStatus.Done && order.Status == OrderStatus.Completed)
        {
            order.Reopen();
            changes.Record("order", order.Id, order.ClientId, null, null, "status");
        }
    }

    /// <summary>
    /// When the plan made at accept time expected <paramref name="next"/> to start: the latest deadline of the stages
    /// before it (its window begins where theirs ends), or the accept time if none of them had a deadline.
    /// Used by <see cref="WorkItem.Unlock(DateTimeOffset, DateTimeOffset?)"/> to give back time lost waiting (e.g. for the client).
    /// </summary>
    internal static DateTimeOffset? PlannedStart(WorkItem next, IEnumerable<WorkItem> orderItems, DateTimeOffset? acceptedAt) =>
        orderItems.Where(x => x.StageOrder < next.StageOrder && x.Status != WorkStatus.Cancelled && x.Deadline != null)
            .Max(x => x.Deadline) ?? acceptedAt;

    public async Task<Result<WorkCardDto>> AssignAsync(Guid id, AssignRequest r, CancellationToken ct)
    {
        if (!access.CanAssign) return Error.Forbidden();
        var w = await db.WorkItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return Error.NotFound("work.notFound", "کار پیدا نشد.");
        if (r.UserId is { } uid)
        {
            if (!await db.Users.AnyAsync(u => u.Id == uid && u.Kind == UserKind.Staff && u.IsActive, ct))
                return Error.Validation("work.assignee", "این همکار پیدا نشد یا غیرفعال است.");
            // A kiosk tablet, a marketer or a blog writer can't see or run work: the item would be silently stuck.
            if (!(await notifier.StaffWithAsync(Permissions.WorkRun, PermissionScope.Assigned, ct)).Contains(uid))
                return Error.Validation("work.assigneeCantRun", "این همکار نمی‌تواند کار انجام دهد.");
        }
        db.SetOriginalVersion(w, r.Version);
        var previous = w.AssigneeId;
        var res = w.Assign(r.UserId);
        if (res.IsFailure) return res.Error!;
        audit.Add("work.assigned", nameof(WorkItem), id.ToString(), new { previous, to = r.UserId });
        if (r.UserId is { } assignee && assignee != me.UserId)
            await notifier.NotifyAsync([assignee], "work.assigned", "کار جدید به شما سپرده شد", $"{w.Title} — {w.StageName.Get("fa")}", $"/work?open={w.Id}", false, $"assign:{w.Id:N}:{assignee:N}:{w.Version}", ct);
        changes.Record("work", w.Id, null, w.AssigneeId, previous, "assignee");   // the portal never learns who works on what
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return (await CardsAsync([w], ct))[0];
    }

    /// <summary>Who should do this: people with the stage's role first, then the lightest open workload.</summary>
    public async Task<Result<IReadOnlyList<AssigneeSuggestionDto>>> SuggestAsync(Guid id, CancellationToken ct)
    {
        if (!access.CanAssign) return Error.Forbidden();
        var w = await db.WorkItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return Error.NotFound("work.notFound", "کار پیدا نشد.");
        // Only people who can actually run work (work.run, assigned scope or wider).
        var runners = await notifier.StaffWithAsync(Permissions.WorkRun, PermissionScope.Assigned, ct);
        var staff = await db.Users.AsNoTracking().Where(u => runners.Contains(u.Id))
            .Where(u => u.Kind == UserKind.Staff && u.IsActive && !u.IsDemo || u.Kind == UserKind.Staff && u.IsActive && me.IsDemo && u.IsDemo)
            .Select(u => new { u.Id, u.DisplayName, Roles = u.Roles.Select(r => r.RoleId).ToList() }).ToListAsync(ct);
        var load = await db.WorkItems.AsNoTracking()
            .Where(x => x.AssigneeId != null && x.Status != WorkStatus.Done && x.Status != WorkStatus.Cancelled)
            .GroupBy(x => x.AssigneeId!.Value).Select(g => new { g.Key, Count = g.Count(), Minutes = g.Sum(x => x.EstimatedMinutes) }).ToListAsync(ct);
        var byUser = load.ToDictionary(l => l.Key);
        var list = staff.Select(u => new AssigneeSuggestionDto(u.Id, u.DisplayName, byUser.GetValueOrDefault(u.Id)?.Count ?? 0,
                byUser.GetValueOrDefault(u.Id)?.Minutes ?? 0, w.RoleId is { } rid && u.Roles.Contains(rid)))
            .OrderByDescending(s => s.HasStageRole).ThenBy(s => s.OpenMinutes).ThenBy(s => s.Name).Take(20).ToList();
        return list;
    }

    public async Task<Result<CommentDto>> CommentAsync(Guid id, CommentRequest r, CancellationToken ct)
    {
        var w = await access.VisibleItems().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return Error.NotFound("work.notFound", "کار پیدا نشد.");
        if (ActorFor(w) is null) return Error.Forbidden("work.notYours", "این کار به شما سپرده نشده است.");
        if (r.Visibility == CommentVisibility.Client && !access.CanTalkToClient)
            return Error.Forbidden("comments.clientOnlyAdmins", "پیام برای مشتری را فقط ادمین می‌فرستد.");
        var c = WorkComment.Create(w, me.UserId, await MyNameAsync(ct), false, r.Visibility, r.Body, clock.GetUtcNow());
        if (c.IsFailure) return c.Error!;
        db.WorkComments.Add(c.Value!);
        changes.Record("work", w.Id, r.Visibility == CommentVisibility.Client ? w.ClientId : null, w.AssigneeId, null, "comments");
        if (r.Visibility == CommentVisibility.Client && await notifier.PortalUserAsync(w.ClientId, ct) is { } portalUser)
            await notifier.NotifyAsync([portalUser], "portal.message", $"پیام تازه درباره‌ی سفارش {w.OrderNumber}", Short(c.Value!.Body), "/dashboard", false, $"msg:{c.Value!.Id:N}", ct);
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        var x = c.Value!;
        return new CommentDto(x.Id, x.AuthorName, false, x.Visibility, x.Body, x.CreatedAt, true);
    }

    private static string Short(string s) => s.Length <= 140 ? s : s[..140] + "…";

    private async Task<string> MyNameAsync(CancellationToken ct)
    {
        var uid = access.Me;
        return await db.Users.Where(u => u.Id == uid).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "—";
    }

    private async Task<IReadOnlyList<WorkCardDto>> CardsAsync(IReadOnlyList<WorkItem> items, CancellationToken ct)
    {
        if (items.Count == 0) return [];
        var lang = me.LanguageCode;
        var ids = items.Select(w => w.Id).ToList();
        var clientIds = items.Select(w => w.ClientId).Distinct().ToList();
        var userIds = items.Where(w => w.AssigneeId != null).Select(w => w.AssigneeId!.Value).Distinct().ToList();
        var serviceIds = items.Select(w => w.ServiceTypeId).Distinct().ToList();
        // Projections only: the board is the busiest read, and whole rows carry big JSON (forms, accounts) it doesn't need.
        var clients = await db.Clients.IgnoreQueryFilters().AsNoTracking().Where(c => clientIds.Contains(c.Id)).Select(c => new { c.Id, c.BusinessName }).ToDictionaryAsync(c => c.Id, c => c.BusinessName, ct);
        var users = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.DisplayName }).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var services = await db.ServiceTypes.IgnoreQueryFilters().AsNoTracking().Where(s => serviceIds.Contains(s.Id)).Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var comments = await db.WorkComments.AsNoTracking().Where(c => ids.Contains(c.WorkItemId)).GroupBy(c => c.WorkItemId)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        var files = await db.Files.AsNoTracking().Where(f => f.WorkItemId != null && ids.Contains(f.WorkItemId!.Value) && f.Status == Domain.Files.FileStatus.Ready)
            .GroupBy(f => f.WorkItemId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        return items.Select(w => new WorkCardDto(w.Id, w.OrderId, w.OrderNumber, w.Title, w.ClientId, clients.GetValueOrDefault(w.ClientId) ?? "—",
            services.GetValueOrDefault(w.ServiceTypeId)?.Get(lang) ?? "—", w.StageName.Get(lang), w.StageOrder, w.StageCount, w.Status, w.Priority,
            w.AssigneeId, w.AssigneeId is { } a ? users.GetValueOrDefault(a) : null, w.Deadline, w.EstimatedMinutes, w.RevisionCount,
            comments.GetValueOrDefault(w.Id), files.GetValueOrDefault(w.Id), w.Rank, w.Version)).ToList();
    }
}
