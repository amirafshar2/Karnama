using System.Text.Json;
using Karnama.Application.Abstractions;
using Karnama.Application.Catalog;
using Karnama.Application.Work;
using Karnama.Domain.Clients;
using Karnama.Domain.Common;
using Karnama.Domain.Files;
using Karnama.Domain.Work;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Portal;

public sealed record QuotaUseDto(Guid ServiceTypeId, string ServiceName, int Used, int Total);
public sealed record PortalPackageDto(string Name, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int DaysLeft, int TotalDays, IReadOnlyList<QuotaUseDto> Quotas);
public sealed record ApprovalDto(Guid WorkItemId, Guid OrderId, long OrderNumber, string Title, string StageName, DateTimeOffset? SubmittedAt, IReadOnlyList<Files.FileDto> Files);
public sealed record PortalOrderDto(Guid Id, long Number, string Title, string ServiceName, OrderStatus Status, int Percent, string StepLabel, DateTimeOffset CreatedAt);
public sealed record PortalHomeDto(string ClientName, PortalPackageDto? Package, IReadOnlyList<ApprovalDto> Approvals, IReadOnlyList<PortalOrderDto> Orders);
public sealed record PortalMessageDto(Guid Id, string Author, bool FromMe, string Body, DateTimeOffset At);
public sealed record ApprovalRequest(bool Approve, string? Note);
public sealed record PortalMessageRequest(string Body);

/// <summary>
/// The client's own panel (docs/PROMPT.md §6.9): package use, orders with progress, approving content, ordering a service.
/// A client never sees workers, working hours, internal notes, internal files or another client.
/// </summary>
public sealed class PortalService(IAppDbContext db, ICurrentUser me, WorkAccess access, TimeProvider clock, IAuditWriter audit, IChangeFeed changes, Notify.Notifier notifier)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<Guid?> ClientAsync(CancellationToken ct) =>
        await access.PortalClientIdAsync(ct) is { } id && id != Guid.Empty ? id : null;

    public async Task<Result<PortalHomeDto>> HomeAsync(CancellationToken ct)
    {
        if (await ClientAsync(ct) is not { } clientId) return Error.Forbidden();
        var lang = me.LanguageCode;
        var now = clock.GetUtcNow();
        var client = await db.Clients.AsNoTracking().FirstAsync(c => c.Id == clientId, ct);

        PortalPackageDto? package = null;
        var cp = await db.ClientPackages.AsNoTracking().Where(p => p.ClientId == clientId && p.Status == ClientPackageStatus.Active && p.EndsAt > now)
            .OrderBy(p => p.StartsAt).FirstOrDefaultAsync(ct);
        if (cp is not null)
        {
            var quotas = JsonSerializer.Deserialize<List<QuotaDto>>(cp.QuotasJson, Json) ?? [];
            var used = await db.Orders.AsNoTracking().Where(o => o.ClientPackageId == cp.Id && o.Status != OrderStatus.Rejected && o.Status != OrderStatus.Cancelled)
                .GroupBy(o => o.ServiceTypeId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
            package = new PortalPackageDto(cp.PackageName.Get(lang), cp.StartsAt, cp.EndsAt, cp.DaysLeft(now), (int)Math.Round((cp.EndsAt - cp.StartsAt).TotalDays),
                quotas.Select(q => new QuotaUseDto(q.ServiceTypeId, q.ServiceName ?? "—", used.GetValueOrDefault(q.ServiceTypeId), q.Count)).ToList());
        }

        var waiting = await db.WorkItems.AsNoTracking().Where(w => w.ClientId == clientId && w.Status == WorkStatus.ClientReview).OrderBy(w => w.SubmittedAt).ToListAsync(ct);
        var waitingIds = waiting.Select(w => (Guid?)w.Id).ToList();
        var files = await db.Files.AsNoTracking().Where(f => f.ClientId == clientId && f.ClientVisible && f.Status == FileStatus.Ready && waitingIds.Contains(f.WorkItemId)).ToListAsync(ct);
        var approvals = waiting.Select(w => new ApprovalDto(w.Id, w.OrderId, w.OrderNumber, w.Title, w.StageName.Get(lang), w.SubmittedAt,
            files.Where(f => f.WorkItemId == w.Id).Select(Files.FileService.ToDto).ToList())).ToList();

        var orders = await db.Orders.AsNoTracking().Where(o => o.ClientId == clientId).OrderByDescending(o => o.Id).Take(30).ToListAsync(ct);
        var ids = orders.Select(o => o.Id).ToList();
        var stages = await db.WorkItems.AsNoTracking().Where(w => ids.Contains(w.OrderId) && w.Status != WorkStatus.Cancelled)
            .Select(w => new { w.OrderId, w.StageOrder, w.Status }).ToListAsync(ct);
        var list = orders.Select(o =>
        {
            var s = stages.Where(x => x.OrderId == o.Id).ToList();
            var percent = o.Status == OrderStatus.Completed ? 100 : s.Count == 0 ? 0 : (int)Math.Round(100.0 * s.Count(x => x.Status == WorkStatus.Done) / s.Count);
            // Plain words for the client — never internal column names.
            var step = o.Status switch
            {
                OrderStatus.Submitted => "submitted",
                OrderStatus.Completed => "completed",
                OrderStatus.Rejected => "rejected",
                OrderStatus.Cancelled => "cancelled",
                _ => s.Any(x => x.Status == WorkStatus.ClientReview) ? "yourApproval" : "inProgress",
            };
            return new PortalOrderDto(o.Id, o.Number, o.Title, o.ServiceName.Get(lang), o.Status, percent, step, o.CreatedAt);
        }).ToList();

        return new PortalHomeDto(client.BusinessName, package, approvals, list);
    }

    public async Task<Result<IReadOnlyList<PortalMessageDto>>> MessagesAsync(Guid orderId, CancellationToken ct)
    {
        if (await ClientAsync(ct) is not { } clientId) return Error.Forbidden();
        if (!await db.Orders.AnyAsync(o => o.Id == orderId && o.ClientId == clientId, ct)) return Error.NotFound("orders.notFound", "سفارش پیدا نشد.");
        var uid = me.UserId;
        var rows = await db.WorkComments.AsNoTracking().Where(c => c.OrderId == orderId && c.ClientId == clientId && c.Visibility == CommentVisibility.Client)
            .OrderBy(c => c.CreatedAt).ToListAsync(ct);
        // Staff messages are signed with the agency's name, not the person's.
        return rows.Select(c => new PortalMessageDto(c.Id, c.FromClient ? c.AuthorName : "آژانس", c.AuthorId == uid, c.Body, c.CreatedAt)).ToList();
    }

    /// <summary>The client writes to the agency about one of its orders. It reaches the admins (only admins talk to clients).</summary>
    public async Task<Result<PortalMessageDto>> SendMessageAsync(Guid orderId, PortalMessageRequest r, CancellationToken ct)
    {
        if (await ClientAsync(ct) is not { } clientId) return Error.Forbidden();
        var items = await db.WorkItems.Where(x => x.OrderId == orderId && x.ClientId == clientId).OrderBy(x => x.StageOrder).ToListAsync(ct);
        if (items.Count == 0) return Error.NotFound("orders.notFound", "سفارش پیدا نشد.");
        var now = clock.GetUtcNow();
        // A cheap brake on a stuck button or a script: 30 messages an hour per client is far above real use.
        if (await db.WorkComments.CountAsync(c => c.ClientId == clientId && c.FromClient && c.CreatedAt > now.AddHours(-1), ct) >= 30)
            return Error.Validation("portal.tooMany", "پیام‌ها زیاد شد؛ کمی بعد دوباره بفرستید.");
        // Attach to the stage in progress (first not finished), else the last stage.
        var w = items.FirstOrDefault(x => x.Status is not (WorkStatus.Done or WorkStatus.Cancelled)) ?? items[^1];
        var name = await db.Clients.Where(c => c.Id == clientId).Select(c => c.BusinessName).FirstAsync(ct);
        var c = WorkComment.Create(w, me.UserId, name, true, CommentVisibility.Client, r.Body ?? "", now);
        if (c.IsFailure) return c.Error!;
        db.WorkComments.Add(c.Value!);
        changes.Record("work", w.Id, clientId, w.AssigneeId, null, "comments");
        var body = c.Value!.Body.Length <= 140 ? c.Value!.Body : c.Value!.Body[..140] + "…";
        await notifier.NotifyAsync(await notifier.StaffWithAsync(Domain.Identity.Permissions.OrdersManage, Domain.Identity.PermissionScope.All, ct),
            "portal.message", $"پیام {name} درباره‌ی سفارش {w.OrderNumber}", body, $"/work?open={w.Id}", false, $"msg:{c.Value!.Id:N}", ct);
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return new PortalMessageDto(c.Value!.Id, name, true, c.Value!.Body, now);
    }

    /// <summary>The client approves the content, or asks for changes with a note (which reaches the team as a client message).</summary>
    public async Task<Result> DecideAsync(Guid workItemId, ApprovalRequest r, CancellationToken ct)
    {
        if (await ClientAsync(ct) is not { } clientId) return Error.Forbidden();
        var w = await db.WorkItems.FirstOrDefaultAsync(x => x.Id == workItemId && x.ClientId == clientId, ct);
        if (w is null) return Error.NotFound("work.notFound", "موردی پیدا نشد.");
        if (!r.Approve && string.IsNullOrWhiteSpace(r.Note)) return Error.Validation("portal.note", "بنویسید چه چیزی باید تغییر کند.");
        var now = clock.GetUtcNow();
        var moved = w.MoveTo(r.Approve ? WorkStatus.Done : WorkStatus.ChangesRequested, WorkActor.Client, now);
        if (moved.IsFailure) return moved.Error!;
        db.MarkModified(w);
        var name = await db.Clients.Where(c => c.Id == clientId).Select(c => c.BusinessName).FirstAsync(ct);
        if (!string.IsNullOrWhiteSpace(r.Note))
        {
            var c = WorkComment.Create(w, me.UserId, name, true, CommentVisibility.Client, r.Note, now);
            if (c.IsFailure) return c.Error!;
            db.WorkComments.Add(c.Value!);
        }
        if (r.Approve)
        {
            var siblings = await db.WorkItems.Where(x => x.OrderId == w.OrderId && x.Id != w.Id && x.Status != WorkStatus.Cancelled).ToListAsync(ct);
            var next = siblings.Where(x => x.StageOrder > w.StageOrder && x.Status == WorkStatus.Waiting).OrderBy(x => x.StageOrder).FirstOrDefault();
            next?.Unlock();
            if (next is not null) changes.Record("work", next.Id, clientId, next.AssigneeId, null, "status");
            if (siblings.All(x => x.Status == WorkStatus.Done))
            {
                var order = await db.Orders.FirstAsync(o => o.Id == w.OrderId, ct);
                order.Complete(now);
                changes.Record("order", order.Id, clientId, null, null, "status");
            }
        }
        audit.Add(r.Approve ? "portal.approved" : "portal.changesRequested", nameof(WorkItem), w.Id.ToString());
        var who = (await notifier.StaffWithAsync(Domain.Identity.Permissions.WorkReview, Domain.Identity.PermissionScope.All, ct)).Concat(w.AssigneeId is { } a ? [a] : []);
        await notifier.NotifyAsync(who, r.Approve ? "portal.approved" : "portal.changes", r.Approve ? $"مشتری تأیید کرد: {w.Title}" : $"مشتری اصلاح خواست: {w.Title}",
            r.Note, $"/work?open={w.Id}", !r.Approve, $"portal:{w.Id:N}:{w.RevisionCount}:{r.Approve}", ct);
        changes.Record("work", w.Id, clientId, w.AssigneeId, null, "status", "comments");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return Result.Success();
    }
}
