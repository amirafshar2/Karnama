using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Application.Notify;
using Karnama.Application.Work;
using Karnama.Domain.Catalog;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Karnama.Domain.Publishing;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Publishing;

public sealed record PublishJobDto(Guid Id, Guid ClientId, string ClientName, Guid? OrderId, long? OrderNumber, Guid PlatformId, string PlatformName, string Handle,
    PageManagement Management, DateTimeOffset PublishAt, string Caption, IReadOnlyList<string> Hashtags, IReadOnlyList<Files.FileDto> Files, PublishStatus Status,
    DateTimeOffset? PublishedAt, string? PostUrl, bool PublishedByClient, uint Version);
public sealed record UpsertPublishRequest(Guid ClientId, Guid? OrderId, Guid PlatformId, string Handle, DateTimeOffset PublishAt, string Caption, List<string>? Hashtags,
    List<Guid>? FileIds, uint? Version);
public sealed record MarkPublishedRequest(string? PostUrl);

public sealed class UpsertPublishValidator : AbstractValidator<UpsertPublishRequest>
{
    public UpsertPublishValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty().WithMessage("مشتری را انتخاب کنید.");
        RuleFor(x => x.PlatformId).NotEmpty().WithMessage("شبکه را انتخاب کنید.");
        RuleFor(x => x.Caption).MaximumLength(PublishJob.MaxCaption).WithMessage("کپشن حداکثر ۲۲۰۰ کاراکتر است.");
    }
}

/// <summary>Publishing plan and content calendar (docs/PROMPT.md §6.5): schedule, remind on time, confirm, overdue.</summary>
public sealed class PublishService(IAppDbContext db, ICurrentUser me, WorkAccess access, Notifier notifier, IAuditWriter audit, TimeProvider clock, IChangeFeed changes)
{
    public static readonly TimeSpan OverdueAfter = TimeSpan.FromHours(2);
    private bool CanManage => me.Kind == UserKind.Staff && me.Has(Permissions.OrdersManage, PermissionScope.All);
    private bool CanSee => me.Kind == UserKind.Staff && (me.Has(Permissions.OrdersView, PermissionScope.All) || me.Has(Permissions.WorkView, PermissionScope.All));

    public async Task<Result<IReadOnlyList<PublishJobDto>>> ListAsync(DateTimeOffset from, DateTimeOffset to, Guid? clientId, CancellationToken ct)
    {
        if (to <= from || (to - from).TotalDays > 45) return Error.Validation("publish.range", "بازه حداکثر ۴۵ روز.");
        var q = db.PublishJobs.AsNoTracking().Where(j => j.PublishAt >= from && j.PublishAt < to && j.Status != PublishStatus.Cancelled);
        if (await access.PortalClientIdAsync(ct) is { } own) q = q.Where(j => j.ClientId == own);
        else if (!CanSee) return Error.Forbidden();
        else if (clientId is { } c) q = q.Where(j => j.ClientId == c);
        return Result.Success(await DtosAsync(await q.OrderBy(j => j.PublishAt).Take(500).ToListAsync(ct), ct));
    }

    /// <summary>For the client portal: due now or soon (next 7 days), anything overdue, and what was published in the last day.</summary>
    public async Task<Result<IReadOnlyList<PublishJobDto>>> PortalAsync(CancellationToken ct)
    {
        if (await access.PortalClientIdAsync(ct) is not { } own || own == Guid.Empty) return Error.Forbidden();
        var now = clock.GetUtcNow();
        var until = now.AddDays(7);
        var since = now.AddDays(-1);
        var rows = await db.PublishJobs.AsNoTracking()
            .Where(j => j.ClientId == own && (j.Status == PublishStatus.Due || j.Status == PublishStatus.Overdue || (j.Status == PublishStatus.Scheduled && j.PublishAt < until)
                || (j.Status == PublishStatus.Published && j.PublishedAt > since)))
            .OrderBy(j => j.PublishAt).Take(50).ToListAsync(ct);
        return Result.Success(await DtosAsync(rows, ct));
    }

    public async Task<Result<PublishJobDto>> CreateAsync(UpsertPublishRequest r, CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        var check = await CheckAsync(r, ct);
        if (check.IsFailure) return check.Error!;
        var created = PublishJob.Create(r.ClientId, r.OrderId, r.PlatformId, r.Handle, check.Value, r.PublishAt, r.Caption, r.Hashtags ?? [], r.FileIds ?? [], clock.GetUtcNow());
        if (created.IsFailure) return created.Error!;
        db.PublishJobs.Add(created.Value!);
        await ShareForClientAsync(created.Value!, ct);
        audit.Add("publish.created", nameof(PublishJob), created.Value!.Id.ToString(), new { r.ClientId, r.PublishAt });
        changes.Record("publish", created.Value.Id, r.ClientId, null, null, "created");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return (await DtosAsync([created.Value], ct))[0];
    }

    public async Task<Result<PublishJobDto>> UpdateAsync(Guid id, UpsertPublishRequest r, CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        var j = await db.PublishJobs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (j is null) return Error.NotFound();
        if (r.Version is not { } v) return Error.Validation("common.versionRequired", "نسخه‌ی رکورد لازم است؛ صفحه را تازه کنید.");
        db.SetOriginalVersion(j, v);
        var check = await CheckAsync(r with { ClientId = j.ClientId }, ct);
        if (check.IsFailure) return check.Error!;
        var u = j.Update(r.PlatformId, r.Handle, check.Value, r.PublishAt, r.Caption, r.Hashtags ?? [], r.FileIds ?? [], clock.GetUtcNow());
        if (u.IsFailure) return u.Error!;
        db.MarkModified(j);
        await ShareForClientAsync(j, ct);
        audit.Add("publish.updated", nameof(PublishJob), id.ToString(), new { r.PublishAt });
        changes.Record("publish", j.Id, j.ClientId, null, null, "updated");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return (await DtosAsync([j], ct))[0];
    }

    public async Task<Result> CancelAsync(Guid id, CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        var j = await db.PublishJobs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (j is null) return Error.NotFound();
        var c = j.Cancel();
        if (c.IsFailure) return c;
        audit.Add("publish.cancelled", nameof(PublishJob), id.ToString());
        changes.Record("publish", j.Id, j.ClientId, null, null, "status");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return Result.Success();
    }

    /// <summary>The team (agency pages) or the client (own pages) confirms it's live, optionally with the post link.</summary>
    public async Task<Result<PublishJobDto>> MarkPublishedAsync(Guid id, MarkPublishedRequest r, CancellationToken ct)
    {
        var j = await db.PublishJobs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (j is null) return Error.NotFound();
        var portal = await access.PortalClientIdAsync(ct);
        if (portal is { } own) { if (j.ClientId != own) return Error.NotFound(); }
        else if (!(me.Kind == UserKind.Staff && (CanManage || me.Has(Permissions.WorkRun, PermissionScope.Assigned)))) return Error.Forbidden();
        var res = j.MarkPublished(me.UserId ?? Guid.Empty, portal is not null, r.PostUrl, clock.GetUtcNow());
        if (res.IsFailure) return res.Error!;
        audit.Add("publish.published", nameof(PublishJob), id.ToString(), new { byClient = portal is not null });
        changes.Record("publish", j.Id, j.ClientId, null, null, "status");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return (await DtosAsync([j], ct))[0];
    }

    /// <summary>Worker, every minute: reminders on time, overdue after two hours. Idempotent (status changes once).</summary>
    public async Task<(int Due, int Overdue)> TickAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = 0; var over = 0;
        var jobs = await db.PublishJobs.Where(j => (j.Status == PublishStatus.Scheduled && j.PublishAt <= now) || (j.Status == PublishStatus.Due && j.PublishAt <= now - OverdueAfter)).Take(200).ToListAsync(ct);
        var clientIds = jobs.Select(j => j.ClientId).Distinct().ToList();
        var clients = await db.Clients.IgnoreQueryFilters().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var managers = jobs.Count == 0 ? [] : await notifier.StaffWithAsync(Permissions.OrdersManage, PermissionScope.All, ct);
        foreach (var j in jobs)
        {
            var name = clients.GetValueOrDefault(j.ClientId)?.BusinessName ?? "";
            if (j.MarkDue(now))
            {
                due++;
                if (j.Management == PageManagement.ClientManaged && clients.GetValueOrDefault(j.ClientId)?.PortalUserId is { } portalUser)
                    await notifier.NotifyAsync([portalUser], "publish.reminder", "وقت انتشار پست رسید", "فایل و کپشن آماده است؛ بعد از انتشار «منتشر کردم» را بزنید.", "/dashboard", true, $"pub-due:{j.Id:N}", ct);
                else
                    await notifier.NotifyAsync(managers, "publish.due", $"وقت انتشار برای {name}", $"@{j.Handle}", "/publishing", false, $"pub-due:{j.Id:N}", ct);
                changes.Record("publish", j.Id, j.ClientId, null, null, "status");
            }
            else if (j.MarkOverdue(now, OverdueAfter))
            {
                over++;
                await notifier.NotifyAsync(managers, "publish.overdue", $"انتشار عقب افتاد: {name}", $"@{j.Handle} — ۲ ساعت از زمان انتشار گذشته و تأیید نشده.", "/publishing", true, $"pub-over:{j.Id:N}", ct);
                changes.Record("publish", j.Id, j.ClientId, null, null, "status");
            }
        }
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return (due, over);
    }

    /// <summary>Client-managed page: the client publishes, so it must be able to download the files.</summary>
    private async Task ShareForClientAsync(PublishJob j, CancellationToken ct)
    {
        if (j.Management != PageManagement.ClientManaged || j.FileIds.Count == 0) return;
        foreach (var f in await db.Files.Where(f => j.FileIds.Contains(f.Id) && !f.ClientVisible).ToListAsync(ct)) f.ShareWithClient(true);
    }

    private async Task<Result<PageManagement>> CheckAsync(UpsertPublishRequest r, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.ClientId, ct);
        if (client is null) return Error.Validation("publish.client", "مشتری پیدا نشد.");
        var account = client.Accounts.FirstOrDefault(a => a.PlatformId == r.PlatformId && string.Equals(a.Handle, r.Handle?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (account is null) return Error.Validation("publish.account", "این پیج برای این مشتری ثبت نشده است.");
        if (r.OrderId is { } oid && !await db.Orders.AnyAsync(o => o.Id == oid && o.ClientId == r.ClientId, ct)) return Error.Validation("publish.order", "سفارش مال این مشتری نیست.");
        if (r.FileIds is { Count: > 0 } f && await db.Files.CountAsync(x => f.Contains(x.Id) && x.ClientId == r.ClientId && x.Status == Domain.Files.FileStatus.Ready, ct) != f.Distinct().Count())
            return Error.Validation("publish.files", "یکی از فایل‌ها مال این مشتری نیست.");
        return account.Management;
    }

    private async Task<IReadOnlyList<PublishJobDto>> DtosAsync(IReadOnlyList<PublishJob> rows, CancellationToken ct)
    {
        var lang = me.LanguageCode;
        var clientIds = rows.Select(r => r.ClientId).Distinct().ToList();
        var clients = await db.Clients.IgnoreQueryFilters().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.BusinessName, ct);
        var platforms = await db.Platforms.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name.Get(lang), ct);
        var orderIds = rows.Where(r => r.OrderId != null).Select(r => r.OrderId!.Value).Distinct().ToList();
        var numbers = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Number, ct);
        var fileIds = rows.SelectMany(r => r.FileIds).Distinct().ToList();
        var portal = await access.PortalClientIdAsync(ct) is not null;
        var files = await db.Files.AsNoTracking().Where(f => fileIds.Contains(f.Id) && f.Status == Domain.Files.FileStatus.Ready).ToDictionaryAsync(f => f.Id, ct);
        return rows.Select(j => new PublishJobDto(j.Id, j.ClientId, clients.GetValueOrDefault(j.ClientId) ?? "—", j.OrderId, j.OrderId is { } o ? numbers.GetValueOrDefault(o) : null,
            j.PlatformId, platforms.GetValueOrDefault(j.PlatformId) ?? "—", j.Handle, j.Management, j.PublishAt, j.Caption, j.Hashtags,
            // The client gets the files of its own publish job (that's the point of the reminder) — and nothing else.
            j.FileIds.Where(files.ContainsKey).Select(id => Files.FileService.ToDto(files[id])).ToList(),
            j.Status, j.PublishedAt, j.PostUrl, j.PublishedByClient, j.Version)).ToList();
    }
}
