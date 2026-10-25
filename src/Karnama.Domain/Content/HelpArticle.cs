using Karnama.Domain.Common;

namespace Karnama.Domain.Content;

/// <summary>Who an article is written for: the client portal gets its own, simpler articles.</summary>
public enum HelpAudience { Everyone = 1, Staff = 2, Clients = 3 }

public sealed record HelpSpec(string Title, string? Slug, string Category, HelpAudience Audience, string? PageKey, string ContentJson, int SortOrder, bool IsPublished);

/// <summary>A help-center article (PROMPT2 §13), written with the blog editor and editable in the panel.</summary>
public sealed class HelpArticle : AuditableEntity
{
    public const int MaxJson = 400_000;

    private HelpArticle() { Title = string.Empty; Slug = string.Empty; Category = string.Empty; ContentJson = "{}"; ContentHtml = string.Empty; PlainText = string.Empty; }

    public string Title { get; private set; }
    public string Slug { get; private set; }
    public string Category { get; private set; }
    public HelpAudience Audience { get; private set; }
    /// <summary>The panel page whose "؟" button links here (e.g. "work", "attendance/calendar").</summary>
    public string? PageKey { get; private set; }
    public string ContentJson { get; private set; }
    /// <summary>Server-rendered, safe HTML (only known editor nodes).</summary>
    public string ContentHtml { get; private set; }
    /// <summary>For search.</summary>
    public string PlainText { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsPublished { get; private set; }

    public static Result<HelpArticle> Create(HelpSpec s, string html, string plain)
    {
        var a = new HelpArticle();
        var r = a.Update(s, html, plain);
        return r.IsSuccess ? a : r.Error!;
    }

    public Result Update(HelpSpec s, string html, string plain)
    {
        var title = (s.Title ?? "").Trim();
        if (title.Length is 0 or > 120) return Error.Validation("help.title", "عنوان الزامی است (حداکثر ۱۲۰ کاراکتر).");
        var cat = (s.Category ?? "").Trim();
        if (cat.Length is 0 or > 40) return Error.Validation("help.category", "دسته الزامی است (حداکثر ۴۰ کاراکتر).");
        if (string.IsNullOrWhiteSpace(s.ContentJson) || s.ContentJson.Length > MaxJson) return Error.Validation("help.content", "متن مقاله خالی است یا خیلی بزرگ است.");
        var slug = Common.Slug.From(string.IsNullOrWhiteSpace(s.Slug) ? title : s.Slug, 80);
        if (slug.Length == 0) return Error.Validation("help.slug", "نشانی مقاله درست نیست.");
        var page = s.PageKey?.Trim().Trim('/');
        if (page is { Length: > 60 }) return Error.Validation("help.page", "نام صفحه خیلی بلند است.");
        Title = title;
        Slug = slug;
        Category = cat;
        Audience = s.Audience;
        PageKey = string.IsNullOrEmpty(page) ? null : page;
        ContentJson = s.ContentJson;
        ContentHtml = html;
        PlainText = plain.Length > 20_000 ? plain[..20_000] : plain;
        SortOrder = s.SortOrder;
        IsPublished = s.IsPublished;
        return Result.Success();
    }

    public void Delete(Guid? actor, DateTimeOffset now) => MarkDeleted(actor, now);
}
