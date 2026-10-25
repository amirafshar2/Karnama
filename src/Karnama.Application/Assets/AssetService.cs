using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Application.Attendance;
using Karnama.Application.Common;
using Karnama.Application.Files;
using Karnama.Application.Notify;
using Karnama.Domain.Assets;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Assets;

public sealed record CategoryDto(Guid Id, string Name, int SortOrder, bool IsActive, int Count);
public sealed record SaveCategoryRequest(string Name, int SortOrder, bool IsActive);
public sealed record AssetItemDto(Guid Id, string Name, Guid CategoryId, string CategoryName, string? Brand, string? Model, string? Serial, string Tag,
    AssetStatus Status, Guid? HolderId, string? HolderName, AssignmentState? AssignmentState, DateOnly? ExpectedReturn, bool Overdue, DateOnly? WarrantyUntil,
    Guid? CoverPhotoId, uint Version);
public sealed record AssignmentDto(Guid Id, Guid UserId, string UserName, string HandedByName, DateTimeOffset HandedAt, DateOnly? ExpectedReturn,
    string? ConditionOut, IReadOnlyList<string> Accessories, DateTimeOffset? AcceptedAt, AssignmentState State, DateTimeOffset? ReturnedAt, string? ConditionIn,
    string? Damage, Guid? TransferToId, string? TransferToName, uint Version);
public sealed record EventDto(long Id, AssetEventKind Kind, string? ActorName, string? UserName, string? Note, DateTimeOffset At);
public sealed record PhotoDto(Guid Id, AssetPhotoStage Stage, Guid? AssignmentId, DateTimeOffset At);
public sealed record AssetDetailDto(AssetItemDto Asset, DateOnly? PurchasedOn, long? PurchasePrice, string? Note, AssignmentDto? Current,
    IReadOnlyList<AssignmentDto> Past, IReadOnlyList<EventDto> History, IReadOnlyList<PhotoDto> Photos, AssetPermissionsDto Can);
public sealed record AssetPermissionsDto(bool Manage, bool Assign, bool Accept, bool SubmitReturn, bool Transfer, bool AcceptTransfer);
public sealed record MineDto(IReadOnlyList<AssetItemDto> Holding, IReadOnlyList<AssetItemDto> Incoming);
public sealed record PersonDto(Guid Id, string Name);
public sealed record SaveAssetRequest(string Name, Guid CategoryId, string? Brand, string? Model, string? Serial, string? Tag, DateOnly? PurchasedOn,
    long? PurchasePrice, DateOnly? WarrantyUntil, string? Note, uint? Version = null);
public sealed record StatusRequest(AssetStatus Status, string? Note);
public sealed record HandOverRequest(Guid UserId, DateOnly? ExpectedReturn, string? Condition, List<string>? Accessories, uint Version);
public sealed record ReturnRequest(string? Condition, string? Damage);
public sealed record ConfirmReturnRequest(AssetStatus Status, string? Condition, string? Damage);
public sealed record TransferRequest(Guid UserId);
public sealed record AcceptTransferRequest(string? Condition);
public sealed record AssetFilter(string? Q, AssetStatus? Status, Guid? CategoryId, Guid? HolderId);

public sealed class SaveAssetValidator : AbstractValidator<SaveAssetRequest>
{
    public SaveAssetValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Tag).MaximumLength(30);
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0).When(x => x.PurchasePrice is not null);
    }
}

public sealed class HandOverValidator : AbstractValidator<HandOverRequest>
{
    public HandOverValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Condition).MaximumLength(500);
        RuleFor(x => x.Accessories).Must(a => a is null || a.Count <= 20).WithMessage("حداکثر ۲۰ لوازم همراه.");
    }
}

/// <summary>
/// Equipment lent to staff (PROMPT2 §3): catalogue, hand-over with condition + photos and the receiver's digital
/// acceptance, return with damage report and manager confirmation, direct transfer accepted by the receiver,
/// QR lookup, append-only history and alerts. <c>assets.view:Own</c> sees only what they hold or are being offered — anything else is 404.
/// </summary>
public sealed class AssetService(IAppDbContext db, ICurrentUser me, TimeProvider clock, Notifier notifier, IAuditWriter audit, IFileStore files, IChangeFeed changes)
{
    public const long MaxPhotoBytes = 5 * 1024 * 1024;
    private static readonly string[] ImageKinds = ["jpg", "png", "webp"];
    private static Error NotFound => Error.NotFound("assets.notFound", "تجهیز پیدا نشد.");
    private Guid Me => me.UserId ?? Guid.Empty;
    private bool IsStaff => me.Kind == UserKind.Staff;
    private bool CanView => IsStaff && me.Has(Permissions.AssetsView, PermissionScope.Own);
    private bool SeesAll => IsStaff && (me.Has(Permissions.AssetsView, PermissionScope.All) || CanManage || CanAssign);
    private bool CanManage => IsStaff && me.Has(Permissions.AssetsManage, PermissionScope.All);
    private bool CanAssign => IsStaff && me.Has(Permissions.AssetsAssign, PermissionScope.All);

    /// <summary>What this person may open: everything, or only what they hold / are offered.</summary>
    private IQueryable<Asset> Visible() => SeesAll
        ? db.Assets
        : db.Assets.Where(a => a.HolderId == Me || db.AssetAssignments.Any(x => x.AssetId == a.Id && x.TransferToId == Me && x.State == AssignmentState.Active));

    // ================= categories =================

    public async Task<Result<List<CategoryDto>>> CategoriesAsync(CancellationToken ct)
    {
        if (!CanView && !SeesAll) return Error.Forbidden();
        var counts = await db.Assets.AsNoTracking().GroupBy(a => a.CategoryId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return (await db.AssetCategories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct))
            .Select(c => new CategoryDto(c.Id, c.Name, c.SortOrder, c.IsActive, counts.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<Result<CategoryDto>> SaveCategoryAsync(Guid? id, SaveCategoryRequest r, CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        var name = (r.Name ?? "").Trim();
        if (await db.AssetCategories.AnyAsync(c => c.Name == name && c.Id != id, ct)) return Error.Conflict("assets.categoryExists", "دسته‌ای با این نام هست.");
        AssetCategory c;
        if (id is { } given)
        {
            var found = await db.AssetCategories.FirstOrDefaultAsync(x => x.Id == given, ct);
            if (found is null) return Error.NotFound("assets.categoryNotFound", "دسته پیدا نشد.");
            var u = found.Update(name, r.SortOrder, r.IsActive);
            if (u.IsFailure) return u.Error!;
            c = found;
        }
        else
        {
            var created = AssetCategory.Create(name, r.SortOrder);
            if (created.IsFailure) return created.Error!;
            c = created.Value!;
            db.AssetCategories.Add(c);
        }
        audit.Add("assets.categorySaved", nameof(AssetCategory), c.Id.ToString(), r);
        await db.SaveChangesAsync(ct);
        return new CategoryDto(c.Id, c.Name, c.SortOrder, c.IsActive, await db.Assets.CountAsync(a => a.CategoryId == c.Id, ct));
    }

    // ================= list & detail =================

    public async Task<Result<List<AssetItemDto>>> ListAsync(AssetFilter f, CancellationToken ct)
    {
        if (!CanView && !SeesAll) return Error.Forbidden();
        var q = Visible().AsNoTracking();
        if (f.Status is { } s) q = q.Where(a => a.Status == s);
        if (f.CategoryId is { } c) q = q.Where(a => a.CategoryId == c);
        if (f.HolderId is { } h) q = q.Where(a => a.HolderId == h);
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var t = f.Q.Trim();
            q = q.Where(a => a.Name.Contains(t) || a.Tag.Contains(t) || (a.Serial != null && a.Serial.Contains(t)) || (a.Brand != null && a.Brand.Contains(t))
                || (a.Model != null && a.Model.Contains(t)));
        }
        return await ItemsAsync(await q.OrderBy(a => a.Name).Take(500).ToListAsync(ct), ct);
    }

    /// <summary>"پیش من": what I hold (incl. waiting for my acceptance) and what colleagues are handing me.</summary>
    public async Task<Result<MineDto>> MineAsync(CancellationToken ct)
    {
        if (!CanView && !SeesAll) return Error.Forbidden();
        var holding = await db.Assets.AsNoTracking().Where(a => a.HolderId == Me).OrderBy(a => a.Name).ToListAsync(ct);
        var incomingIds = await db.AssetAssignments.AsNoTracking().Where(x => x.TransferToId == Me && x.State == AssignmentState.Active).Select(x => x.AssetId).ToListAsync(ct);
        var incoming = await db.Assets.AsNoTracking().Where(a => incomingIds.Contains(a.Id)).ToListAsync(ct);
        return new MineDto(await ItemsAsync(holding, ct), await ItemsAsync(incoming, ct));
    }

    public async Task<Result<AssetDetailDto>> GetAsync(Guid id, CancellationToken ct)
    {
        if (!CanView && !SeesAll) return Error.Forbidden();
        var a = await Visible().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound;
        return await DetailAsync(a, ct);
    }

    /// <summary>QR label → asset. Same visibility as opening it by id.</summary>
    public async Task<Result<AssetDetailDto>> ByTagAsync(string tag, CancellationToken ct)
    {
        if (!CanView && !SeesAll) return Error.Forbidden();
        var t = (tag ?? "").Trim();
        var a = await Visible().AsNoTracking().FirstOrDefaultAsync(x => x.Tag == t, ct);
        return a is null ? NotFound : await DetailAsync(a, ct);
    }

    private async Task<AssetDetailDto> DetailAsync(Asset a, CancellationToken ct)
    {
        var assignments = await db.AssetAssignments.AsNoTracking().Where(x => x.AssetId == a.Id).OrderByDescending(x => x.HandedAt).Take(100).ToListAsync(ct);
        var events = await db.AssetEvents.AsNoTracking().Where(e => e.AssetId == a.Id).OrderByDescending(e => e.Id).Take(200).ToListAsync(ct);
        var photos = await db.AssetPhotos.AsNoTracking().Where(p => p.AssetId == a.Id).OrderBy(p => p.At).Select(p => new PhotoDto(p.Id, p.Stage, p.AssignmentId, p.At)).ToListAsync(ct);
        var ids = assignments.SelectMany(x => new[] { x.UserId, x.HandedBy, x.TransferToId ?? Guid.Empty })
            .Concat(events.SelectMany(e => new[] { e.ActorId ?? Guid.Empty, e.UserId ?? Guid.Empty })).Where(x => x != Guid.Empty).Distinct().ToList();
        var names = await NamesAsync(ids, ct);
        AssignmentDto Map(AssetAssignment x) => new(x.Id, x.UserId, names.GetValueOrDefault(x.UserId, "—"), names.GetValueOrDefault(x.HandedBy, "—"), x.HandedAt,
            x.ExpectedReturn, x.ConditionOut, x.Accessories, x.AcceptedAt, x.State, x.ReturnedAt, x.ConditionIn, x.Damage, x.TransferToId,
            x.TransferToId is { } t ? names.GetValueOrDefault(t) : null, x.Version);
        var current = assignments.FirstOrDefault(x => x.IsOpen);
        var item = (await ItemsAsync([a], ct))[0];
        var can = new AssetPermissionsDto(
            Manage: CanManage,
            Assign: CanAssign,
            Accept: current is { State: AssignmentState.PendingAcceptance } && current.UserId == Me,
            SubmitReturn: current is { State: AssignmentState.Active } && current.UserId == Me,
            Transfer: current is { State: AssignmentState.Active } && (current.UserId == Me || CanAssign),
            AcceptTransfer: current is { State: AssignmentState.Active } && current.TransferToId == Me);
        // A worker sees the hand-over history only for their own hand-overs; managers see all of it.
        var past = assignments.Where(x => !x.IsOpen && (SeesAll || x.UserId == Me)).Select(Map).ToList();
        var history = events.Where(e => SeesAll || e.UserId == Me || e.ActorId == Me)
            .Select(e => new EventDto(e.Id, e.Kind, e.ActorId is { } ac ? names.GetValueOrDefault(ac) : null, e.UserId is { } u ? names.GetValueOrDefault(u) : null, e.Note, e.At)).ToList();
        return new AssetDetailDto(item, a.PurchasedOn, SeesAll ? a.PurchasePrice : null, a.Note, current is null ? null : Map(current), past, history,
            SeesAll ? photos : photos.Where(p => p.Stage == AssetPhotoStage.Profile || (current is not null && p.AssignmentId == current.Id)).ToList(), can);
    }

    private async Task<List<AssetItemDto>> ItemsAsync(List<Asset> list, CancellationToken ct)
    {
        if (list.Count == 0) return [];
        var ids = list.Select(a => a.Id).ToList();
        var cats = await db.AssetCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var open = await db.AssetAssignments.AsNoTracking().Where(x => ids.Contains(x.AssetId) && (x.State == AssignmentState.PendingAcceptance || x.State == AssignmentState.Active || x.State == AssignmentState.ReturnSubmitted))
            .ToDictionaryAsync(x => x.AssetId, ct);
        var covers = await db.AssetPhotos.AsNoTracking().Where(p => ids.Contains(p.AssetId) && p.Stage == AssetPhotoStage.Profile)
            .GroupBy(p => p.AssetId).Select(g => new { g.Key, Id = g.OrderBy(p => p.At).Select(p => p.Id).First() }).ToDictionaryAsync(x => x.Key, x => x.Id, ct);
        var names = await NamesAsync(list.Where(a => a.HolderId is not null).Select(a => a.HolderId!.Value).Distinct().ToList(), ct);
        var today = Iran.Today(clock.GetUtcNow());
        return list.Select(a =>
        {
            var o = open.GetValueOrDefault(a.Id);
            return new AssetItemDto(a.Id, a.Name, a.CategoryId, cats.GetValueOrDefault(a.CategoryId, "—"), a.Brand, a.Model, a.Serial, a.Tag, a.Status, a.HolderId,
                a.HolderId is { } h ? names.GetValueOrDefault(h) : null, o?.State, o?.ExpectedReturn, o is { State: AssignmentState.Active, ExpectedReturn: { } e } && e < today,
                a.WarrantyUntil, covers.TryGetValue(a.Id, out var cover) ? cover : null, a.Version);
        }).ToList();
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(List<Guid> ids, CancellationToken ct) =>
        ids.Count == 0 ? [] : await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

    /// <summary>Colleagues an asset can be handed or transferred to.</summary>
    public async Task<Result<List<PersonDto>>> PeopleAsync(CancellationToken ct)
    {
        if (!CanView && !SeesAll) return Error.Forbidden();
        return await db.Users.AsNoTracking().Where(u => u.Kind == UserKind.Staff && u.IsActive).OrderBy(u => u.DisplayName)
            .Select(u => new PersonDto(u.Id, u.DisplayName)).ToListAsync(ct);
    }

    // ================= catalogue =================

    public async Task<Result<AssetDetailDto>> CreateAsync(SaveAssetRequest r, CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        if (!await db.AssetCategories.AnyAsync(c => c.Id == r.CategoryId, ct)) return Error.Validation("assets.category", "دسته را انتخاب کنید.");
        var tag = string.IsNullOrWhiteSpace(r.Tag) ? await NextTagAsync(ct) : r.Tag.Trim();
        if (await db.Assets.AnyAsync(a => a.Tag == tag, ct)) return Error.Conflict("assets.tagExists", "این کد اموال قبلاً ثبت شده.");
        var created = Asset.Create(Spec(r), tag);
        if (created.IsFailure) return created.Error!;
        var a = created.Value!;
        db.Assets.Add(a);
        db.AssetEvents.Add(AssetEvent.Log(a.Id, Me, AssetEventKind.Created, clock.GetUtcNow()));
        audit.Add("assets.created", nameof(Asset), a.Id.ToString(), new { a.Name, a.Tag, a.Serial });
        await SaveAsync(a.Id, ct);
        return await DetailAsync(a, ct);
    }

    public async Task<Result<AssetDetailDto>> UpdateAsync(Guid id, SaveAssetRequest r, CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        var a = await db.Assets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound;
        if (r.Version is { } v) db.SetOriginalVersion(a, v);
        if (!await db.AssetCategories.AnyAsync(c => c.Id == r.CategoryId, ct)) return Error.Validation("assets.category", "دسته را انتخاب کنید.");
        var tag = string.IsNullOrWhiteSpace(r.Tag) ? a.Tag : r.Tag.Trim();
        if (tag != a.Tag && await db.Assets.AnyAsync(x => x.Tag == tag && x.Id != id, ct)) return Error.Conflict("assets.tagExists", "این کد اموال قبلاً ثبت شده.");
        var before = new { a.Name, a.Tag, a.Serial, a.PurchasePrice, a.WarrantyUntil };
        var u = a.Update(Spec(r) with { Tag = tag });
        if (u.IsFailure) return u.Error!;
        db.AssetEvents.Add(AssetEvent.Log(a.Id, Me, AssetEventKind.Updated, clock.GetUtcNow()));
        audit.Add("assets.updated", nameof(Asset), a.Id.ToString(), new { before, after = new { a.Name, a.Tag, a.Serial, a.PurchasePrice, a.WarrantyUntil } });
        await SaveAsync(a.Id, ct);
        return await DetailAsync(a, ct);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!CanManage) return Error.Forbidden();
        var a = await db.Assets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound;
        if (a.HolderId is not null) return Error.Conflict("assets.held", "این تجهیز دست کسی است؛ اول برگشت را ثبت کنید.");
        var now = clock.GetUtcNow();
        a.Delete(Me, now);
        db.AssetEvents.Add(AssetEvent.Log(a.Id, Me, AssetEventKind.Deleted, now));
        audit.Add("assets.deleted", nameof(Asset), a.Id.ToString(), new { a.Name, a.Tag });
        await SaveAsync(a.Id, ct);
        return Result.Success();
    }

    public async Task<Result<AssetDetailDto>> SetStatusAsync(Guid id, StatusRequest r, CancellationToken ct)
    {
        if (!CanManage && !CanAssign) return Error.Forbidden();
        var a = await db.Assets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound;
        var from = a.Status;
        var s = a.SetStatus(r.Status);
        if (s.IsFailure) return s.Error!;
        db.AssetEvents.Add(AssetEvent.Log(a.Id, Me, AssetEventKind.StatusChanged, clock.GetUtcNow(), note: $"{StatusName(from)} → {StatusName(r.Status)}{(string.IsNullOrWhiteSpace(r.Note) ? "" : $" — {r.Note}")}"));
        audit.Add("assets.statusChanged", nameof(Asset), a.Id.ToString(), new { from, to = r.Status, r.Note });
        await SaveAsync(a.Id, ct);
        return await DetailAsync(a, ct);
    }

    // ================= hand-over lifecycle =================

    public async Task<Result<AssetDetailDto>> HandOverAsync(Guid id, HandOverRequest r, CancellationToken ct)
    {
        if (!CanAssign) return Error.Forbidden();
        var a = await db.Assets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound;
        db.SetOriginalVersion(a, r.Version);
        var person = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == r.UserId && u.Kind == UserKind.Staff && u.IsActive, ct);
        if (person is null) return Error.Validation("assets.user", "گیرنده باید یکی از همکاران فعال باشد.");
        var now = clock.GetUtcNow();
        var h = AssetAssignment.HandOver(a, r.UserId, Me, r.ExpectedReturn, r.Condition, r.Accessories, now);
        if (h.IsFailure) return h.Error!;
        var x = h.Value!;
        db.AssetAssignments.Add(x);
        db.AssetEvents.Add(AssetEvent.Log(a.Id, Me, AssetEventKind.HandedOver, now, x.Id, r.UserId, Describe(x)));
        audit.Add("assets.handedOver", nameof(Asset), a.Id.ToString(), new { to = r.UserId, r.ExpectedReturn });
        await notifier.NotifyAsync([r.UserId], "assets.handover", $"تجهیز به شما تحویل شد: {a.Name}", "در اپ «تحویل گرفتم» را بزنید.", $"/assets?id={a.Id}", false,
            $"asset-handover:{x.Id:N}", ct);
        var saved = await SaveAsync(a.Id, ct);
        if (saved.IsFailure) return saved.Error!;
        return await DetailAsync(a, ct);
    }

    public Task<Result<AssetDetailDto>> AcceptAsync(Guid assignmentId, CancellationToken ct) =>
        OnAssignmentAsync(assignmentId, async (a, x, now) =>
        {
            var r = x.Accept(Me, now);
            if (r.IsFailure) return r;
            db.AssetEvents.Add(AssetEvent.Log(a.Id, Me, AssetEventKind.Accepted, now, x.Id, Me));
            await notifier.NotifyAsync([x.HandedBy], "assets.accepted", $"تحویل تأیید شد: {a.Name}", null, $"/assets?id={a.Id}", false, $"asset-accepted:{x.Id:N}", ct);
            return Result.Success();
        }, ct);

    public Task<Result<AssetDetailDto>> DeclineAsync(Guid assignmentId, string? note, CancellationToken ct) =>
        OnAssignmentAsync(assignmentId, async (a, x, now) =>
        {
            var r = x.Decline(Me, a);
            if (r.IsFailure) return r;
            db.AssetEvents.Add(AssetEvent.Log(a.Id, Me, AssetEventKind.Declined, now, x.Id, Me, note));
            await notifier.NotifyAsync([x.HandedBy], "assets.declined", $"تحویل رد شد: {a.Name}", note, $"/assets?id={a.Id}", true, $"asset-declined:{x.Id:N}", ct);
            return Result.Success();
        }, ct);

    public Task<Result<AssetDetailDto>> CancelAsync(Guid assignmentId, CancellationToken ct) =>
        OnAssignmentAsync(assignmentId, (a, x, now) =>
        {
            if (!CanAssign) return Task.FromResult<Result>(Error.Forbidden());
            var r = x.Cancel(a);
            if (r.IsSuccess) db.AssetEvents.Add(AssetEvent.Log(a.Id, Me, AssetEventKind.Cancelled, now, x.Id, x.UserId));
            return Task.FromResult(r);
        }, ct);

    public Task<Result<AssetDetailDto>> SubmitReturnAsync(Guid assignmentId, ReturnRequest req, CancellationToken ct) =>
        OnAssignmentAsync(assignmentId, async (a, x, now) =>
        {
            var r = x.SubmitReturn(Me, req.Condition, req.Damage, now);
            if (r.IsFailure) return r;
            db.AssetEvents.Add(AssetEvent.Log(a.Id, Me, AssetEventKind.ReturnSubmitted, now, x.Id, Me, Join(req.Condition, req.Damage)));
            await notifier.NotifyAsync(await notifier.StaffWithAsync(Permissions.AssetsAssign, PermissionScope.All, ct), "assets.return",
                $"برگشت تجهیز منتظر تأیید: {a.Name}", req.Damage is { Length: > 0 } ? $"خرابی/کمبود: {req.Damage}" : null, $"/assets?id={a.Id}", req.Damage is { Length: > 0 },
                $"asset-return:{x.Id:N}", ct);
            return Result.Success();
        }, ct);

    public Task<Result<AssetDetailDto>> ConfirmReturnAsync(Guid assignmentId, ConfirmReturnRequest req, CancellationToken ct) =>
        OnAssignmentAsync(assignmentId, async (a, x, now) =>
        {
            if (!CanAssign) return Error.Forbidden();
            var r = x.ConfirmReturn(a, Me, req.Status, req.Condition, req.Damage, now);
            if (r.IsFailure) return r;
            db.AssetEvents.Add(AssetEvent.Log(a.Id, Me, AssetEventKind.Returned, now, x.Id, x.UserId, Join(x.ConditionIn, x.Damage, req.Status == AssetStatus.Available ? null : StatusName(req.Status))));
            audit.Add("assets.returned", nameof(Asset), a.Id.ToString(), new { from = x.UserId, status = req.Status, x.Damage });
            await notifier.NotifyAsync([x.UserId], "assets.returned", $"برگشت ثبت شد: {a.Name}", null, $"/assets?id={a.Id}", false, $"asset-returned:{x.Id:N}", ct);
            return Result.Success();
        }, ct);

    public Task<Result<AssetDetailDto>> OfferTransferAsync(Guid assignmentId, TransferRequest req, CancellationToken ct) =>
        OnAssignmentAsync(assignmentId, async (a, x, now) =>
        {
            if (x.UserId != Me && !CanAssign) return Error.Forbidden();
            if (!await db.Users.AnyAsync(u => u.Id == req.UserId && u.Kind == UserKind.Staff && u.IsActive, ct))
                return Error.Validation("assets.user", "گیرنده باید یکی از همکاران فعال باشد.");
            var r = x.OfferTransfer(req.UserId);
            if (r.IsFailure) return r;
            db.AssetEvents.Add(AssetEvent.Log(a.Id, Me, AssetEventKind.TransferOffered, now, x.Id, req.UserId));
            await notifier.NotifyAsync([req.UserId], "assets.transfer", $"تجهیز برای شما فرستاده شد: {a.Name}", "وقتی گرفتید «تحویل گرفتم» را بزنید.", $"/assets?id={a.Id}", false,
                $"asset-transfer:{x.Id:N}:{req.UserId:N}", ct);
            return Result.Success();
        }, ct);

    public Task<Result<AssetDetailDto>> CancelTransferAsync(Guid assignmentId, CancellationToken ct) =>
        OnAssignmentAsync(assignmentId, (a, x, now) =>
        {
            if (x.UserId != Me && x.TransferToId != Me && !CanAssign) return Task.FromResult<Result>(Error.Forbidden());
            var to = x.TransferToId;
            var r = x.CancelTransfer();
            if (r.IsSuccess) db.AssetEvents.Add(AssetEvent.Log(a.Id, Me, AssetEventKind.TransferCancelled, now, x.Id, to));
            return Task.FromResult(r);
        }, ct);

    public async Task<Result<AssetDetailDto>> AcceptTransferAsync(Guid assignmentId, AcceptTransferRequest req, CancellationToken ct)
    {
        Guid? newId = null;
        var res = await OnAssignmentAsync(assignmentId, async (a, x, now) =>
        {
            var from = x.UserId;
            var r = x.AcceptTransfer(a, Me, req.Condition, now);
            if (r.IsFailure) return r.Error!;
            // Close the old row first: the database allows one open hand-over per asset.
            await db.SaveChangesAsync(ct);
            db.AssetAssignments.Add(r.Value!);
            newId = r.Value!.Id;
            db.AssetEvents.Add(AssetEvent.Log(a.Id, Me, AssetEventKind.Transferred, now, r.Value!.Id, Me, req.Condition));
            audit.Add("assets.transferred", nameof(Asset), a.Id.ToString(), new { from, to = Me });
            await notifier.NotifyAsync([from], "assets.transferred", $"انتقال انجام شد: {a.Name}", null, $"/assets?id={a.Id}", false, $"asset-transferred:{x.Id:N}", ct);
            return Result.Success();
        }, ct, transactional: true);
        return res;
    }

    private async Task<Result<AssetDetailDto>> OnAssignmentAsync(Guid assignmentId, Func<Asset, AssetAssignment, DateTimeOffset, Task<Result>> act, CancellationToken ct,
        bool transactional = false)
    {
        if (!CanView && !SeesAll) return Error.Forbidden();
        async Task<Result<AssetDetailDto>> Work(CancellationToken token)
        {
            var x = await db.AssetAssignments.FirstOrDefaultAsync(z => z.Id == assignmentId, token);
            if (x is null) return NotFound;
            // Only people involved (or managers) even learn the hand-over exists.
            if (!SeesAll && x.UserId != Me && x.TransferToId != Me) return NotFound;
            var a = await db.Assets.FirstOrDefaultAsync(z => z.Id == x.AssetId, token);
            if (a is null) return NotFound;
            var r = await act(a, x, clock.GetUtcNow());
            if (r.IsFailure) return r.Error!;
            var saved = await SaveAsync(a.Id, token);
            if (saved.IsFailure) return saved.Error!;
            return await DetailAsync(a, token);
        }
        return transactional ? await db.InTransactionAsync(Work, ct) : await Work(ct);
    }

    // ================= photos =================

    public async Task<Result<PhotoDto>> AddPhotoAsync(Guid assetId, AssetPhotoStage stage, Guid? assignmentId, Stream content, long length, CancellationToken ct)
    {
        if (!CanView && !SeesAll) return Error.Forbidden();
        var a = await Visible().AsNoTracking().FirstOrDefaultAsync(x => x.Id == assetId, ct);
        if (a is null) return NotFound;
        if (stage == AssetPhotoStage.Profile)
        {
            if (!CanManage) return Error.Forbidden();
            assignmentId = null;
        }
        else
        {
            var x = assignmentId is { } aid ? await db.AssetAssignments.AsNoTracking().FirstOrDefaultAsync(z => z.Id == aid && z.AssetId == assetId, ct) : null;
            if (x is null) return Error.Validation("assets.photoAssignment", "عکس وضعیت باید به یک تحویل مشخص وصل باشد.");
            if (!CanAssign && x.UserId != Me) return Error.Forbidden();
            if (!x.IsOpen) return Error.Conflict("assets.closed", "این تحویل بسته شده است.");
        }
        if (length <= 0 || length > MaxPhotoBytes) return Error.Validation("assets.photoSize", "عکس حداکثر ۵ مگابایت است (اپ خودش عکس را کوچک می‌کند).");
        if (await db.AssetPhotos.CountAsync(p => p.AssetId == assetId, ct) >= 200) return Error.Validation("assets.photoCount", "برای این تجهیز عکس کافی ثبت شده.");
        var now = clock.GetUtcNow();
        var key = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}";
        await files.AppendAsync(key, 0, content, length, ct);
        var kind = FileService.Sniff(await files.ReadHeadAsync(key, 32, ct));
        if (kind is null || !ImageKinds.Contains(kind))
        {
            await files.DeleteAsync(key, ct);
            return Error.Validation("assets.photoType", "فقط عکس (JPG، PNG یا WebP) قبول می‌شود.");
        }
        var p = AssetPhoto.Create(assetId, assignmentId, stage, key, kind switch { "png" => "image/png", "webp" => "image/webp", _ => "image/jpeg" }, length, Me, now);
        db.AssetPhotos.Add(p);
        db.AssetEvents.Add(AssetEvent.Log(assetId, Me, AssetEventKind.PhotoAdded, now, assignmentId, note: stage switch
        {
            AssetPhotoStage.HandOver => "عکس هنگام تحویل", AssetPhotoStage.Return => "عکس هنگام برگشت", _ => "عکس تجهیز",
        }));
        await db.SaveChangesAsync(ct);
        return new PhotoDto(p.Id, p.Stage, p.AssignmentId, p.At);
    }

    public async Task<Result<(AssetPhoto Photo, Stream Content)>> OpenPhotoAsync(Guid photoId, CancellationToken ct)
    {
        if (!CanView && !SeesAll) return Error.Forbidden();
        var p = await db.AssetPhotos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == photoId, ct);
        if (p is null || !await Visible().AnyAsync(a => a.Id == p.AssetId, ct)) return Error.NotFound();
        if (!SeesAll && p.Stage != AssetPhotoStage.Profile
            && !await db.AssetAssignments.AnyAsync(x => x.Id == p.AssignmentId && (x.UserId == Me || x.TransferToId == Me), ct)) return Error.NotFound();
        return (p, files.OpenRead(p.FileKey));
    }

    // ================= alerts =================

    /// <summary>
    /// Daily: overdue returns (holder + assigners, once a day), warranty ending in 30/7 days or ended (managers, once per
    /// threshold), and equipment still held by someone who no longer works here.
    /// </summary>
    public async Task<int> AlertsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = Iran.Today(now);
        var sent = 0;
        var assigners = await notifier.StaffWithAsync(Permissions.AssetsAssign, PermissionScope.All, ct);
        var managers = await notifier.StaffWithAsync(Permissions.AssetsManage, PermissionScope.All, ct);

        var overdue = await db.AssetAssignments.Where(x => x.State == AssignmentState.Active && x.ExpectedReturn != null && x.ExpectedReturn < today).Take(500).ToListAsync(ct);
        foreach (var x in overdue.Where(x => x.NeedsOverdueNotice(today)))
        {
            var a = await db.Assets.AsNoTracking().FirstOrDefaultAsync(z => z.Id == x.AssetId, ct);
            if (a is null) continue;
            var who = await db.Users.AsNoTracking().Where(u => u.Id == x.UserId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "—";
            await notifier.NotifyAsync([x.UserId], "assets.overdue", $"وقت برگرداندن {a.Name} گذشته", "لطفاً برگردانید یا با مدیر هماهنگ کنید.", $"/assets?id={a.Id}", true,
                $"asset-overdue:{x.Id:N}:{today:yyyyMMdd}", ct);
            await notifier.NotifyAsync(assigners, "assets.overdue", $"برگشت دیر شده: {a.Name}", $"دست {who}", $"/assets?id={a.Id}", false, $"asset-overdue-m:{x.Id:N}:{today:yyyyMMdd}", ct);
            x.OverdueAnnounced(now);
            sent++;
        }

        var soon = today.AddDays(30);
        var warranty = await db.Assets.Where(a => a.WarrantyUntil != null && a.WarrantyUntil <= soon && a.Status != AssetStatus.Retired).Take(500).ToListAsync(ct);
        foreach (var a in warranty)
        {
            var left = a.WarrantyUntil!.Value.DayNumber - today.DayNumber;
            var threshold = left < 0 ? 0 : left <= 7 ? 7 : 30;
            if (a.WarrantyNoticeDays is { } done && done <= threshold) continue;
            await notifier.NotifyAsync(managers, "assets.warranty", left < 0 ? $"گارانتی {a.Name} تمام شد" : $"گارانتی {a.Name} {left} روز دیگر تمام می‌شود",
                $"کد اموال {a.Tag}", $"/assets?id={a.Id}", false, $"asset-warranty:{a.Id:N}:{threshold}", ct);
            a.WarrantyAnnounced(threshold);
            sent++;
        }

        var orphaned = await db.Assets.AsNoTracking().Where(a => a.HolderId != null && db.Users.Any(u => u.Id == a.HolderId && !u.IsActive)).Take(200).ToListAsync(ct);
        foreach (var a in orphaned)
        {
            await notifier.NotifyAsync(assigners, "assets.orphaned", $"تجهیز دست همکار غیرفعال: {a.Name}", "برگشت را پیگیری کنید.", $"/assets?id={a.Id}", true,
                $"asset-orphan:{a.Id:N}:{today:yyyyMMdd}", ct);
            sent++;
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }

    // ================= helpers =================

    private async Task<Result> SaveAsync(Guid assetId, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ux_asset_assignments_open") == true)
        {
            return Error.Conflict("assets.held", "همین الان این تجهیز به کس دیگری تحویل شد.");
        }
        changes.Record("asset", assetId, null, null, null, "changed");
        await changes.PublishAsync(ct);
        return Result.Success();
    }

    private async Task<string> NextTagAsync(CancellationToken ct)
    {
        var n = await db.Assets.IgnoreQueryFilters().CountAsync(ct) + 1;
        string tag;
        do tag = $"KN-{n++:0000}"; while (await db.Assets.IgnoreQueryFilters().AnyAsync(a => a.Tag == tag, ct));
        return tag;
    }

    private static AssetSpec Spec(SaveAssetRequest r) => new(r.Name, r.CategoryId, r.Brand, r.Model, r.Serial, r.Tag, r.PurchasedOn, r.PurchasePrice, r.WarrantyUntil, r.Note);

    private static string Describe(AssetAssignment x) =>
        Join(x.ConditionOut, x.Accessories.Count > 0 ? "همراه: " + string.Join("، ", x.Accessories) : null,
            x.ExpectedReturn is { } e ? $"برگشت تا {Iran.Jalali(e)}" : null) ?? "";

    private static string? Join(params string?[] parts)
    {
        var p = parts.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        return p.Count == 0 ? null : string.Join(" — ", p);
    }

    public static string StatusName(AssetStatus s) => s switch
    {
        AssetStatus.Available => "در انبار", AssetStatus.Assigned => "دست کارگر", AssetStatus.InRepair => "در تعمیر", AssetStatus.Lost => "گم‌شده", _ => "کنار گذاشته",
    };
}
