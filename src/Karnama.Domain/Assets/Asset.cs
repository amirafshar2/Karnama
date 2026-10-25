using Karnama.Domain.Common;

namespace Karnama.Domain.Assets;

public enum AssetStatus { Available = 1, Assigned = 2, InRepair = 3, Lost = 4, Retired = 5 }

/// <summary>Laptop, camera, lens, … — editable list in the panel (PROMPT2 §3).</summary>
public sealed class AssetCategory : Entity
{
    private AssetCategory() { Name = string.Empty; }

    public string Name { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Result<AssetCategory> Create(string name, int sortOrder, Guid? id = null)
    {
        var c = new AssetCategory { SortOrder = sortOrder };
        if (id is { } given) c.Id = given;
        var r = c.Update(name, sortOrder, true);
        return r.IsSuccess ? c : r.Error!;
    }

    public Result Update(string name, int sortOrder, bool isActive)
    {
        var n = (name ?? "").Trim();
        if (n.Length is 0 or > 40) return Error.Validation("assets.categoryName", "نام دسته الزامی است (حداکثر ۴۰ کاراکتر).");
        Name = n;
        SortOrder = sortOrder;
        IsActive = isActive;
        return Result.Success();
    }
}

public sealed record AssetSpec(string Name, Guid CategoryId, string? Brand, string? Model, string? Serial, string? Tag, DateOnly? PurchasedOn,
    long? PurchasePrice, DateOnly? WarrantyUntil, string? Note);

/// <summary>
/// A piece of equipment the agency lends to its people. <see cref="HolderId"/> mirrors the one open hand-over
/// (pending or active); the database allows only one such hand-over per asset.
/// </summary>
public sealed class Asset : AuditableEntity
{
    private Asset() { Name = string.Empty; Tag = string.Empty; }

    public string Name { get; private set; }
    public Guid CategoryId { get; private set; }
    public string? Brand { get; private set; }
    public string? Model { get; private set; }
    public string? Serial { get; private set; }
    /// <summary>Property code printed on the QR label; unique.</summary>
    public string Tag { get; private set; }
    public DateOnly? PurchasedOn { get; private set; }
    /// <summary>Rial.</summary>
    public long? PurchasePrice { get; private set; }
    public DateOnly? WarrantyUntil { get; private set; }
    public string? Note { get; private set; }
    public AssetStatus Status { get; private set; } = AssetStatus.Available;
    public Guid? HolderId { get; private set; }
    /// <summary>Last warranty notice sent (days-before threshold), so each threshold is announced once.</summary>
    public int? WarrantyNoticeDays { get; private set; }
    public uint Version { get; private set; }

    public static Result<Asset> Create(AssetSpec s, string fallbackTag)
    {
        var a = new Asset();
        var r = a.Update(s, fallbackTag);
        return r.IsSuccess ? a : r.Error!;
    }

    public Result Update(AssetSpec s, string? fallbackTag = null)
    {
        var name = (s.Name ?? "").Trim();
        if (name.Length is 0 or > 80) return Error.Validation("assets.name", "نام تجهیز الزامی است (حداکثر ۸۰ کاراکتر).");
        var tag = Clean(s.Tag, 30) ?? fallbackTag ?? Tag;
        if (string.IsNullOrWhiteSpace(tag)) return Error.Validation("assets.tag", "کد اموال الزامی است.");
        if (s.PurchasePrice is < 0) return Error.Validation("assets.price", "قیمت خرید نمی‌تواند منفی باشد.");
        if (s.PurchasedOn is { } p && s.WarrantyUntil is { } w && w < p) return Error.Validation("assets.warranty", "پایان گارانتی قبل از تاریخ خرید است.");
        if (WarrantyUntil != s.WarrantyUntil) WarrantyNoticeDays = null;
        Name = name;
        CategoryId = s.CategoryId;
        Brand = Clean(s.Brand, 40);
        Model = Clean(s.Model, 60);
        Serial = Clean(s.Serial, 60);
        Tag = tag;
        PurchasedOn = s.PurchasedOn;
        PurchasePrice = s.PurchasePrice;
        WarrantyUntil = s.WarrantyUntil;
        Note = Clean(s.Note, 1000);
        return Result.Success();
    }

    /// <summary>Manual status for items not in someone's hands (repair, lost, retired, back on the shelf).</summary>
    public Result SetStatus(AssetStatus status)
    {
        if (HolderId is not null || Status == AssetStatus.Assigned)
            return Error.Conflict("assets.held", "این تجهیز دست کسی است؛ اول برگشت را ثبت کنید.");
        if (status == AssetStatus.Assigned) return Error.Validation("assets.status", "برای سپردن از «تحویل به کارگر» استفاده کنید.");
        Status = status;
        return Result.Success();
    }

    public Result CanHandOver() => Status switch
    {
        AssetStatus.Available when HolderId is null => Result.Success(),
        AssetStatus.Assigned => Error.Conflict("assets.held", "این تجهیز الان دست کس دیگری است."),
        _ => Error.Conflict("assets.unavailable", "این تجهیز قابل تحویل نیست (در تعمیر، گم‌شده یا کنار گذاشته).")
    };

    internal void HandTo(Guid userId) { HolderId = userId; Status = AssetStatus.Assigned; }

    internal void Released(AssetStatus next) { HolderId = null; Status = next; }

    public void Delete(Guid? actor, DateTimeOffset now) => MarkDeleted(actor, now);

    public void WarrantyAnnounced(int days) => WarrantyNoticeDays = days;

    private static string? Clean(string? v, int max)
    {
        var t = v?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Length > max ? t[..max] : t;
    }
}

public enum AssignmentState { PendingAcceptance = 1, Active = 2, ReturnSubmitted = 3, Returned = 4, Transferred = 5, Cancelled = 6 }

/// <summary>One hand-over of an asset to a person, from the moment it leaves the shelf until it comes back (or moves on).</summary>
public sealed class AssetAssignment : Entity
{
    private AssetAssignment() { Accessories = []; }

    public Guid AssetId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid HandedBy { get; private set; }
    public DateTimeOffset HandedAt { get; private set; }
    public DateOnly? ExpectedReturn { get; private set; }
    public string? ConditionOut { get; private set; }
    public List<string> Accessories { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public AssignmentState State { get; private set; }

    public DateTimeOffset? ReturnedAt { get; private set; }
    public string? ConditionIn { get; private set; }
    /// <summary>Damage or missing parts reported on return.</summary>
    public string? Damage { get; private set; }
    public Guid? ReturnConfirmedBy { get; private set; }

    /// <summary>Direct hand-over to another person, waiting for them to accept.</summary>
    public Guid? TransferToId { get; private set; }
    public DateTimeOffset? OverdueNoticeOn { get; private set; }
    public uint Version { get; private set; }

    public bool IsOpen => State is AssignmentState.PendingAcceptance or AssignmentState.Active or AssignmentState.ReturnSubmitted;

    public static Result<AssetAssignment> HandOver(Asset asset, Guid userId, Guid by, DateOnly? expectedReturn, string? condition, IEnumerable<string>? accessories,
        DateTimeOffset now, bool alreadyAccepted = false)
    {
        var ok = asset.CanHandOver();
        if (ok.IsFailure) return ok.Error!;
        if (expectedReturn is { } er && er < DateOnly.FromDateTime(now.UtcDateTime.AddHours(3.5)))
            return Error.Validation("assets.expectedReturn", "تاریخ برگشت نمی‌تواند در گذشته باشد.");
        var list = (accessories ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Select(x => x.Length > 60 ? x[..60] : x).Distinct().Take(20).ToList();
        var a = new AssetAssignment
        {
            AssetId = asset.Id, UserId = userId, HandedBy = by, HandedAt = now, ExpectedReturn = expectedReturn,
            ConditionOut = Cut(condition, 500), Accessories = list,
            State = alreadyAccepted ? AssignmentState.Active : AssignmentState.PendingAcceptance,
            AcceptedAt = alreadyAccepted ? now : null,
        };
        asset.HandTo(userId);
        return a;
    }

    public Result Accept(Guid me, DateTimeOffset now)
    {
        if (me != UserId) return Error.Forbidden();
        if (State != AssignmentState.PendingAcceptance) return Error.Conflict("assets.notPending", "این تحویل منتظر تأیید نیست.");
        State = AssignmentState.Active;
        AcceptedAt = now;
        return Result.Success();
    }

    /// <summary>The person refuses (wrong item, not received): the asset goes back on the shelf.</summary>
    public Result Decline(Guid me, Asset asset)
    {
        if (me != UserId) return Error.Forbidden();
        if (State != AssignmentState.PendingAcceptance) return Error.Conflict("assets.notPending", "این تحویل منتظر تأیید نیست.");
        State = AssignmentState.Cancelled;
        asset.Released(AssetStatus.Available);
        return Result.Success();
    }

    /// <summary>Manager takes back a hand-over nobody accepted yet.</summary>
    public Result Cancel(Asset asset)
    {
        if (State != AssignmentState.PendingAcceptance) return Error.Conflict("assets.notPending", "فقط تحویلِ تأییدنشده لغو می‌شود؛ برای بقیه برگشت ثبت کنید.");
        State = AssignmentState.Cancelled;
        asset.Released(AssetStatus.Available);
        return Result.Success();
    }

    /// <summary>The holder brings it back and says in what shape; a manager confirms.</summary>
    public Result SubmitReturn(Guid me, string? condition, string? damage, DateTimeOffset now)
    {
        if (me != UserId) return Error.Forbidden();
        if (State != AssignmentState.Active) return Error.Conflict("assets.notActive", "این تجهیز الان دست شما نیست.");
        State = AssignmentState.ReturnSubmitted;
        ReturnedAt = now;
        ConditionIn = Cut(condition, 500);
        Damage = Cut(damage, 1000);
        TransferToId = null;
        return Result.Success();
    }

    /// <summary>Manager closes the hand-over. <paramref name="next"/>: Available, InRepair (damaged) or Lost.</summary>
    public Result ConfirmReturn(Asset asset, Guid by, AssetStatus next, string? condition, string? damage, DateTimeOffset now)
    {
        if (!IsOpen || State == AssignmentState.PendingAcceptance) return Error.Conflict("assets.notActive", "برگشت فقط برای تجهیزی که تحویل گرفته شده ثبت می‌شود.");
        if (next is not (AssetStatus.Available or AssetStatus.InRepair or AssetStatus.Lost)) return Error.Validation("assets.status", "وضعیت بعد از برگشت درست نیست.");
        if (State == AssignmentState.Active)
        {
            ReturnedAt = now;
            ConditionIn = Cut(condition, 500);
            Damage = Cut(damage, 1000);
        }
        else
        {
            if (condition is not null) ConditionIn = Cut(condition, 500);
            if (damage is not null) Damage = Cut(damage, 1000);
        }
        State = AssignmentState.Returned;
        ReturnConfirmedBy = by;
        TransferToId = null;
        asset.Released(next);
        return Result.Success();
    }

    /// <summary>Holder (or a manager) offers the item straight to a colleague.</summary>
    public Result OfferTransfer(Guid to)
    {
        if (State != AssignmentState.Active) return Error.Conflict("assets.notActive", "فقط تجهیزی که تحویل گرفته شده منتقل می‌شود.");
        if (to == UserId) return Error.Validation("assets.transferSelf", "تجهیز همین الان دست همین نفر است.");
        TransferToId = to;
        return Result.Success();
    }

    public Result CancelTransfer()
    {
        if (TransferToId is null) return Error.Conflict("assets.noTransfer", "انتقالی در جریان نیست.");
        TransferToId = null;
        return Result.Success();
    }

    /// <summary>The receiver accepts: this hand-over closes and a new, already-accepted one starts.</summary>
    public Result<AssetAssignment> AcceptTransfer(Asset asset, Guid me, string? condition, DateTimeOffset now)
    {
        if (TransferToId != me) return Error.Forbidden();
        if (State != AssignmentState.Active) return Error.Conflict("assets.notActive", "این انتقال دیگر معتبر نیست.");
        State = AssignmentState.Transferred;
        ReturnedAt = now;
        ConditionIn = Cut(condition, 500);
        TransferToId = null;
        asset.Released(AssetStatus.Available);
        return HandOver(asset, me, UserId, ExpectedReturn is { } e && e >= DateOnly.FromDateTime(now.UtcDateTime.AddHours(3.5)) ? e : null,
            condition ?? ConditionOut, Accessories, now, alreadyAccepted: true);
    }

    public bool NeedsOverdueNotice(DateOnly today) =>
        State == AssignmentState.Active && ExpectedReturn is { } e && e < today && (OverdueNoticeOn is null || DateOnly.FromDateTime(OverdueNoticeOn.Value.UtcDateTime.AddHours(3.5)) < today);

    public void OverdueAnnounced(DateTimeOffset now) => OverdueNoticeOn = now;

    private static string? Cut(string? v, int max)
    {
        var t = v?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Length > max ? t[..max] : t;
    }
}

public enum AssetEventKind
{
    Created = 1, Updated = 2, HandedOver = 3, Accepted = 4, Declined = 5, Cancelled = 6, ReturnSubmitted = 7, Returned = 8,
    TransferOffered = 9, TransferCancelled = 10, Transferred = 11, StatusChanged = 12, PhotoAdded = 13, Deleted = 14,
}

/// <summary>Append-only history of an asset: who had it, when, in what shape.</summary>
public sealed class AssetEvent
{
    private AssetEvent() { }

    public long Id { get; private set; }
    public Guid AssetId { get; private set; }
    public Guid? AssignmentId { get; private set; }
    public Guid? ActorId { get; private set; }
    public AssetEventKind Kind { get; private set; }
    /// <summary>The person the event is about (receiver, returner).</summary>
    public Guid? UserId { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset At { get; private set; }

    public static AssetEvent Log(Guid assetId, Guid? actor, AssetEventKind kind, DateTimeOffset at, Guid? assignmentId = null, Guid? user = null, string? note = null) => new()
    {
        AssetId = assetId, ActorId = actor, Kind = kind, At = at, AssignmentId = assignmentId, UserId = user,
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim().Length > 1000 ? note.Trim()[..1000] : note.Trim(),
    };
}

public enum AssetPhotoStage { Profile = 1, HandOver = 2, Return = 3 }

/// <summary>Photo of the item itself, or of its condition at hand-over/return. Stored privately, served only to people who may see the asset.</summary>
public sealed class AssetPhoto : Entity
{
    private AssetPhoto() { FileKey = string.Empty; ContentType = string.Empty; }

    public Guid AssetId { get; private set; }
    public Guid? AssignmentId { get; private set; }
    public AssetPhotoStage Stage { get; private set; }
    public string FileKey { get; private set; }
    public string ContentType { get; private set; }
    public long Size { get; private set; }
    public Guid? UploadedBy { get; private set; }
    public DateTimeOffset At { get; private set; }

    public static AssetPhoto Create(Guid assetId, Guid? assignmentId, AssetPhotoStage stage, string key, string contentType, long size, Guid? by, DateTimeOffset at) => new()
    {
        AssetId = assetId, AssignmentId = assignmentId, Stage = stage, FileKey = key, ContentType = contentType, Size = size, UploadedBy = by, At = at,
    };
}
