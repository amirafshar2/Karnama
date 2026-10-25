using System.Text.Json;
using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Application.Catalog;
using Karnama.Application.Common;
using Karnama.Domain.Catalog;
using Karnama.Domain.Clients;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Karnama.Domain.Work;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Work;

public sealed record OrderListItemDto(
    Guid Id, long Number, string Title, Guid ClientId, string ClientName, Guid ServiceTypeId, string ServiceName, OrderStatus Status,
    OrderPriority Priority, OrderSource Source, DateTimeOffset? Deadline, bool OutsidePackage, int StagesDone, int StagesTotal,
    string? CurrentStage, DateTimeOffset CreatedAt, uint Version);

public sealed record FormAnswerDto(string Key, string Label, string Type, JsonElement? Value);

public sealed record OrderStageDto(Guid Id, int Order, string Name, WorkStatus Status, Guid? AssigneeId, string? AssigneeName, DateTimeOffset? Deadline);

public sealed record OrderDto(
    Guid Id, long Number, string Title, string? Note, Guid ClientId, string ClientName, Guid ServiceTypeId, string ServiceName,
    OrderStatus Status, OrderPriority Priority, OrderSource Source, DateTimeOffset? Deadline, bool OutsidePackage, long? Price,
    string? RejectReason, IReadOnlyList<FormAnswerDto> Answers, IReadOnlyList<OrderStageDto> Stages,
    IReadOnlyList<Files.FileDto> Files, DateTimeOffset CreatedAt, uint Version);

public sealed record CreateOrderRequest(
    Guid ClientId, Guid ServiceTypeId, string? Title, string? Note, JsonElement? Answers, OrderPriority Priority, DateTimeOffset? Deadline, bool AutoAccept);

public sealed record RejectOrderRequest(string Reason);

public sealed class CreateOrderValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty().WithMessage("مشتری را انتخاب کنید.");
        RuleFor(x => x.ServiceTypeId).NotEmpty().WithMessage("نوع خدمت را انتخاب کنید.");
        RuleFor(x => x.Title).MaximumLength(Order.MaxTitle).WithMessage("عنوان حداکثر ۱۵۰ کاراکتر است.");
        RuleFor(x => x.Note).MaximumLength(Order.MaxNote).WithMessage("توضیح حداکثر ۴۰۰۰ کاراکتر است.");
        RuleFor(x => x.Priority).IsInEnum();
    }
}

/// <summary>Orders: placed by an admin (for a client) or by the client in the portal; accepting one creates its work items.</summary>
public sealed class OrderService(IAppDbContext db, ICurrentUser me, WorkAccess access, IAuditWriter audit, TimeProvider clock, IChangeFeed changes)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<KeysetPage<OrderListItemDto>> ListAsync(KeysetQuery q, OrderStatus? status, Guid? clientId, CancellationToken ct)
    {
        var query = (await access.VisibleOrdersAsync(ct)).AsNoTracking();
        if (status is { } st) query = query.Where(o => o.Status == st);
        if (clientId is { } cid) query = query.Where(o => o.ClientId == cid);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var like = Keyset.Like(q.Search);
            var digits = new string(q.Search.Where(char.IsAsciiDigit).ToArray());
            long.TryParse(digits, out var number);
            query = query.Where(o => EF.Functions.ILike(o.Title, like) || o.Number == number
                                     || db.Clients.Any(c => c.Id == o.ClientId && EF.Functions.ILike(c.BusinessName, like)));
        }
        var lang = me.LanguageCode;
        var page = await Keyset.PageAsync(query, q, x => x, o => o.Id, ct);
        var ids = page.Items.Select(o => o.Id).ToList();
        var clientIds = page.Items.Select(o => o.ClientId).Distinct().ToList();
        var clients = await db.Clients.IgnoreQueryFilters().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.BusinessName, ct);
        var stages = await db.WorkItems.AsNoTracking().Where(w => ids.Contains(w.OrderId) && w.Status != WorkStatus.Cancelled)
            .Select(w => new { w.OrderId, w.StageOrder, w.StageName, w.Status }).ToListAsync(ct);
        var byOrder = stages.GroupBy(s => s.OrderId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.StageOrder).ToList());
        return new KeysetPage<OrderListItemDto>(page.Items.Select(o =>
        {
            var s = byOrder.GetValueOrDefault(o.Id) ?? [];
            var current = s.FirstOrDefault(x => x.Status != WorkStatus.Done);
            return new OrderListItemDto(o.Id, o.Number, o.Title, o.ClientId, clients.GetValueOrDefault(o.ClientId) ?? "—", o.ServiceTypeId, o.ServiceName.Get(lang),
                o.Status, o.Priority, o.Source, o.Deadline, o.OutsidePackage, s.Count(x => x.Status == WorkStatus.Done), s.Count,
                current?.StageName.Get(lang), o.CreatedAt, o.Version);
        }).ToList(), page.NextCursor);
    }

    public async Task<Result<OrderDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var o = await (await access.VisibleOrdersAsync(ct)).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return o is null ? Error.NotFound("orders.notFound", "سفارش پیدا نشد.") : await ToDtoAsync(o, ct);
    }

    public async Task<Result<OrderDto>> CreateAsync(CreateOrderRequest r, OrderSource source, CancellationToken ct)
    {
        Guid clientId;
        if (source == OrderSource.Client)
        {
            if (await access.PortalClientIdAsync(ct) is not { } own || own == Guid.Empty) return Error.Forbidden();
            clientId = own;
        }
        else
        {
            if (!me.Has(Permissions.OrdersCreate, PermissionScope.All)) return Error.Forbidden();
            clientId = r.ClientId;
        }

        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId, ct);
        if (client is null) return Error.Validation("orders.client", "مشتری پیدا نشد.");
        if (client.Status != ClientStatus.Active) return Error.Validation("orders.clientInactive", "همکاری با این مشتری فعال نیست.");
        var service = await db.ServiceTypes.AsNoTracking().Include(s => s.Stages).FirstOrDefaultAsync(s => s.Id == r.ServiceTypeId, ct);
        if (service is null || !service.IsActive) return Error.Validation("orders.service", "این خدمت فعال نیست.");
        if (service.Stages.Count == 0) return Error.Validation("orders.noStages", "این خدمت هنوز مرحله‌ی کاری ندارد.");

        var answers = FormSchema.ValidateData(FormSchema.Parse(service.FormSchemaJson), r.Answers);
        if (answers.IsFailure) return answers.Error!;

        var now = clock.GetUtcNow();
        var number = await db.NextOrderNumberAsync(ct);
        var created = Order.Create(number, clientId, service.Id, service.Name, service.FormVersion, answers.Value!, r.Title, r.Note, source,
            source == OrderSource.Client ? OrderPriority.Normal : r.Priority, r.Deadline, now);
        if (created.IsFailure) return created.Error!;
        var order = created.Value!;
        db.Orders.Add(order);
        audit.Add("orders.created", nameof(Order), order.Id.ToString(), new { order.Number, source });
        changes.Record("order", order.Id, order.ClientId, null, null, "created");

        if (source == OrderSource.Admin && r.AutoAccept && me.Has(Permissions.OrdersManage, PermissionScope.All))
        {
            var accepted = await AcceptCoreAsync(order, service, ct);
            if (accepted.IsFailure) return accepted.Error!;
        }
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return await ToDtoAsync(order, ct);
    }

    public async Task<Result<OrderDto>> AcceptAsync(Guid id, uint version, CancellationToken ct)
    {
        var result = await db.InTransactionAsync(async t =>
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, t);
            if (order is null) return (Result<Order>)Error.NotFound("orders.notFound", "سفارش پیدا نشد.");
            db.SetOriginalVersion(order, version);
            var service = await db.ServiceTypes.IgnoreQueryFilters().AsNoTracking().Include(s => s.Stages).FirstAsync(s => s.Id == order.ServiceTypeId, t);
            var r = await AcceptCoreAsync(order, service, t);
            if (r.IsFailure) return r.Error!;
            db.MarkModified(order);
            await db.SaveChangesAsync(t);
            return order;
        }, ct);
        if (result.IsFailure) return result.Error!;
        await changes.PublishAsync(ct);
        return await ToDtoAsync(result.Value!, ct);
    }

    /// <summary>Uses package quota if any is left (else marks the order as extra, at the service's base price) and creates the work items.</summary>
    private async Task<Result> AcceptCoreAsync(Order order, ServiceType service, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        Guid? packageId = null;
        var outside = false;
        if (service.ConsumesQuota)
        {
            var active = await db.ClientPackages.AsNoTracking()
                .Where(p => p.ClientId == order.ClientId && p.Status == ClientPackageStatus.Active && p.StartsAt <= now && p.EndsAt > now)
                .OrderBy(p => p.EndsAt).ToListAsync(ct);
            foreach (var cp in active)
            {
                var quota = (JsonSerializer.Deserialize<List<QuotaDto>>(cp.QuotasJson, Json) ?? []).Where(q => q.ServiceTypeId == service.Id).Sum(q => q.Count);
                if (quota == 0) continue;
                var used = await db.Orders.CountAsync(o => o.ClientPackageId == cp.Id && o.ServiceTypeId == service.Id
                                                           && o.Status != OrderStatus.Rejected && o.Status != OrderStatus.Cancelled, ct);
                if (used < quota) { packageId = cp.Id; break; }
            }
            outside = packageId is null;
        }
        var accepted = order.Accept(packageId, outside, service.BasePrice, now);
        if (accepted.IsFailure) return accepted;

        // Stage deadlines: each stage's SLA from now, never later than the order deadline.
        var stages = service.Stages.OrderBy(s => s.Order).ToList();
        var maxRank = await db.WorkItems.MaxAsync(w => (double?)w.Rank, ct) ?? 0;
        var at = now;
        for (var i = 0; i < stages.Count; i++)
        {
            at = stages[i].SlaHours is { } h ? at.AddHours(h) : at;
            DateTimeOffset? deadline = stages[i].SlaHours is null ? null : at;
            if (order.Deadline is { } od && (deadline is null || deadline > od || i == stages.Count - 1)) deadline = od;
            var item = WorkItem.ForStage(order, stages[i], stages.Count, deadline, maxRank + 1000 * (i + 1));
            db.WorkItems.Add(item);
            changes.Record("work", item.Id, order.ClientId, null, null, "created");
        }
        audit.Add("orders.accepted", nameof(Order), order.Id.ToString(), new { order.Number, outside, packageId });
        changes.Record("order", order.Id, order.ClientId, null, null, "status");
        return Result.Success();
    }

    public async Task<Result<OrderDto>> RejectAsync(Guid id, RejectOrderRequest r, CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return Error.NotFound("orders.notFound", "سفارش پیدا نشد.");
        var res = order.Reject(r.Reason);
        if (res.IsFailure) return res.Error!;
        audit.Add("orders.rejected", nameof(Order), id.ToString(), new { order.Number });
        changes.Record("order", order.Id, order.ClientId, null, null, "status");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return await ToDtoAsync(order, ct);
    }

    public async Task<Result<OrderDto>> CancelAsync(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return Error.NotFound("orders.notFound", "سفارش پیدا نشد.");
        var res = order.Cancel();
        if (res.IsFailure) return res.Error!;
        foreach (var w in await db.WorkItems.Where(w => w.OrderId == id).ToListAsync(ct))
        {
            if (!w.IsOpen) continue;
            w.Cancel();
            changes.Record("work", w.Id, w.ClientId, w.AssigneeId, null, "status");
        }
        audit.Add("orders.cancelled", nameof(Order), id.ToString(), new { order.Number });
        changes.Record("order", order.Id, order.ClientId, null, null, "status");
        await db.SaveChangesAsync(ct);
        await changes.PublishAsync(ct);
        return await ToDtoAsync(order, ct);
    }

    internal async Task<OrderDto> ToDtoAsync(Order o, CancellationToken ct)
    {
        var lang = me.LanguageCode;
        var portal = await access.PortalClientIdAsync(ct) is not null;
        var client = await db.Clients.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == o.ClientId).Select(c => c.BusinessName).FirstOrDefaultAsync(ct);
        var schema = await db.ServiceFormVersions.AsNoTracking().Where(v => v.ServiceTypeId == o.ServiceTypeId && v.Version == o.FormVersion)
            .Select(v => v.SchemaJson).FirstOrDefaultAsync(ct);
        var fields = FormSchema.Parse(schema ?? "[]");
        using var doc = JsonDocument.Parse(o.FormDataJson);
        var answers = fields.Where(f => f.Type != "file").Select(f => new FormAnswerDto(f.Key, new LocalizedText(f.Label).Get(lang), f.Type,
            doc.RootElement.TryGetProperty(f.Key, out var v) ? v.Clone() : null)).ToList();

        var items = await db.WorkItems.AsNoTracking().Where(w => w.OrderId == o.Id).OrderBy(w => w.StageOrder).ToListAsync(ct);
        var userIds = items.Where(w => w.AssigneeId != null).Select(w => w.AssigneeId!.Value).Distinct().ToList();
        var names = portal ? [] : await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        // The portal never learns who works on what.
        var stages = items.Where(w => w.Status != WorkStatus.Cancelled).Select(w => new OrderStageDto(w.Id, w.StageOrder, w.StageName.Get(lang), w.Status,
            portal ? null : w.AssigneeId, portal ? null : (w.AssigneeId is { } a ? names.GetValueOrDefault(a) : null), w.Deadline)).ToList();

        var files = db.Files.AsNoTracking().Where(f => f.OrderId == o.Id && f.Status == Domain.Files.FileStatus.Ready);
        if (portal) files = files.Where(f => f.ClientVisible);
        var fileDtos = (await files.OrderBy(f => f.CreatedAt).ToListAsync(ct)).Select(Files.FileService.ToDto).ToList();

        return new OrderDto(o.Id, o.Number, o.Title, o.Note, o.ClientId, client ?? "—", o.ServiceTypeId, o.ServiceName.Get(lang), o.Status, o.Priority,
            o.Source, o.Deadline, o.OutsidePackage, portal ? null : o.Price, o.RejectReason, answers, stages, fileDtos, o.CreatedAt, o.Version);
    }
}
