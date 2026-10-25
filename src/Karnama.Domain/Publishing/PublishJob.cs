using Karnama.Domain.Catalog;
using Karnama.Domain.Common;

namespace Karnama.Domain.Publishing;

public enum PublishStatus { Scheduled, Due, Published, Overdue, Cancelled }

/// <summary>
/// One post to publish: where (network + page), when, caption and hashtags, which files (docs/PROMPT.md §6.5).
/// AgencyManaged pages: the team publishes and records the link. ClientManaged: the client is reminded on time
/// (with download and copy-caption) and confirms; two hours later without a confirmation → Overdue.
/// </summary>
public sealed class PublishJob : AuditableEntity
{
    public const int MaxCaption = 2200;   // Instagram's limit
    private PublishJob() { Handle = string.Empty; Caption = string.Empty; Hashtags = []; FileIds = []; }

    public Guid ClientId { get; private set; }
    public Guid? OrderId { get; private set; }
    public Guid PlatformId { get; private set; }
    public string Handle { get; private set; }
    public PageManagement Management { get; private set; }
    public DateTimeOffset PublishAt { get; private set; }
    public string Caption { get; private set; }
    public List<string> Hashtags { get; private set; }
    public List<Guid> FileIds { get; private set; }
    public PublishStatus Status { get; private set; }
    public DateTimeOffset? DueNotifiedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public string? PostUrl { get; private set; }
    public Guid? PublishedBy { get; private set; }
    public bool PublishedByClient { get; private set; }
    public uint Version { get; private set; }

    public static Result<PublishJob> Create(Guid clientId, Guid? orderId, Guid platformId, string handle, PageManagement management, DateTimeOffset publishAt,
        string caption, IEnumerable<string> hashtags, IEnumerable<Guid> fileIds, DateTimeOffset now)
    {
        var j = new PublishJob { ClientId = clientId, OrderId = orderId, Status = PublishStatus.Scheduled };
        var r = j.Update(platformId, handle, management, publishAt, caption, hashtags, fileIds, now);
        return r.IsSuccess ? j : r.Error!;
    }

    public Result Update(Guid platformId, string handle, PageManagement management, DateTimeOffset publishAt, string caption, IEnumerable<string> hashtags,
        IEnumerable<Guid> fileIds, DateTimeOffset now)
    {
        if (Status is PublishStatus.Published or PublishStatus.Cancelled) return Error.Conflict("publish.closed", "این انتشار بسته شده است.");
        caption = caption?.Trim() ?? "";
        if (caption.Length > MaxCaption) return Error.Validation("publish.caption", "کپشن حداکثر ۲۲۰۰ کاراکتر است.");
        if (string.IsNullOrWhiteSpace(handle)) return Error.Validation("publish.handle", "پیج را انتخاب کنید.");
        var tags = hashtags.Select(h => h.Trim().TrimStart('#')).Where(h => h.Length is > 0 and <= 60 && !h.Any(char.IsWhiteSpace)).Distinct().Take(30).ToList();
        if (publishAt < now.AddMinutes(-5) && Status == PublishStatus.Scheduled && PublishAt != publishAt)
            return Error.Validation("publish.past", "زمان انتشار نمی‌تواند در گذشته باشد.");
        PlatformId = platformId; Handle = handle.Trim(); Management = management; Caption = caption; Hashtags = tags; FileIds = fileIds.Distinct().Take(20).ToList();
        if (PublishAt != publishAt) { PublishAt = publishAt; if (Status is PublishStatus.Due or PublishStatus.Overdue) { Status = PublishStatus.Scheduled; DueNotifiedAt = null; } }
        return Result.Success();
    }

    /// <summary>Time has come: the reminder goes out once.</summary>
    public bool MarkDue(DateTimeOffset now)
    {
        if (Status != PublishStatus.Scheduled || now < PublishAt) return false;
        Status = PublishStatus.Due;
        DueNotifiedAt = now;
        return true;
    }

    public bool MarkOverdue(DateTimeOffset now, TimeSpan grace)
    {
        if (Status != PublishStatus.Due || now < PublishAt + grace) return false;
        Status = PublishStatus.Overdue;
        return true;
    }

    public Result MarkPublished(Guid by, bool byClient, string? url, DateTimeOffset now)
    {
        if (Status is PublishStatus.Published or PublishStatus.Cancelled) return Error.Conflict("publish.closed", "این انتشار بسته شده است.");
        if (byClient && Management != PageManagement.ClientManaged) return Error.Forbidden("publish.agencyPage", "این پیج را آژانس منتشر می‌کند.");
        if (url is { Length: > 0 } && !(Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https"))
            return Error.Validation("publish.url", "لینک پست باید با https:// شروع شود.");
        Status = PublishStatus.Published;
        PublishedAt = now; PublishedBy = by; PublishedByClient = byClient; PostUrl = string.IsNullOrWhiteSpace(url) ? null : url;
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status == PublishStatus.Published) return Error.Conflict("publish.done", "منتشرشده را نمی‌شود لغو کرد.");
        Status = PublishStatus.Cancelled;
        return Result.Success();
    }

    public void Delete(Guid? actor, DateTimeOffset now) => MarkDeleted(actor, now);
}
