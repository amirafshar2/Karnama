using System.Text.RegularExpressions;
using Karnama.Domain.Common;

namespace Karnama.Domain.Content;

public enum PostStatus { Draft = 1, InReview = 2, Scheduled = 3, Published = 4, Archived = 5 }

public sealed record TocItem(int Level, string Id, string Text);
public sealed record SeoCheck(string Key, string Level, string? Detail);   // Level: good | warn | bad
public sealed record FaqItem(string Question, string Answer);

/// <summary>What the automatic SEO pass produced for a post (docs/PROMPT2.md §18). Fields the author locked are not overwritten.</summary>
public sealed record SeoResult(
    string SeoTitle, string MetaDescription, string Slug, string FocusKeyword,
    IReadOnlyList<TocItem> Toc, int WordCount, int ReadingMinutes, int Score, IReadOnlyList<SeoCheck> Checks,
    IReadOnlyList<FaqItem> Faq, int AltFilled, string Html, string Markdown, string PlainText, string? FirstImage);

public sealed class BlogCategory : Entity
{
    private BlogCategory() { Name = string.Empty; Slug = string.Empty; }
    public string Name { get; private set; }
    public string Slug { get; private set; }
    public string? Description { get; private set; }
    public int SortOrder { get; private set; }

    public static Result<BlogCategory> Create(string name, string? description, int sort)
    {
        var c = new BlogCategory();
        var r = c.Update(name, description, sort);
        return r.IsSuccess ? c : r.Error!;
    }

    public Result Update(string name, string? description, int sort)
    {
        name = name?.Trim() ?? "";
        if (name.Length is < 2 or > 60) return Error.Validation("blog.category", "نام دسته بین ۲ تا ۶۰ کاراکتر.");
        Name = name; Slug = Common.Slug.From(name, 60); Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()[..Math.Min(300, description.Trim().Length)];
        SortOrder = sort;
        return Result.Success();
    }
}

/// <summary>Old URL of a post whose slug changed after publishing → permanent redirect.</summary>
public sealed class BlogSlugRedirect
{
    private BlogSlugRedirect() { OldSlug = string.Empty; }
    public BlogSlugRedirect(string oldSlug, Guid postId, DateTimeOffset now) { OldSlug = oldSlug; PostId = postId; CreatedAt = now; }
    public string OldSlug { get; private set; }
    public Guid PostId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>
/// A blog post. The editor JSON is the source; HTML (whitelisted, rendered on the server), Markdown and every SEO field
/// are derived on save. The author may override title/description/keyword/slug — then that field is locked.
/// </summary>
public sealed partial class BlogPost : AuditableEntity
{
    public const int MaxJson = 1_000_000;
    private BlogPost()
    {
        Title = string.Empty; Slug = string.Empty; ContentJson = "{}"; ContentHtml = string.Empty; ContentMarkdown = string.Empty; PlainText = string.Empty;
        SeoTitle = string.Empty; MetaDescription = string.Empty; FocusKeyword = string.Empty;
    }

    public string Title { get; private set; }
    public string Slug { get; private set; }
    public PostStatus Status { get; private set; }
    public Guid AuthorId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public List<string> Tags { get; private set; } = [];
    public string ContentJson { get; private set; }
    public string ContentHtml { get; private set; }
    public string ContentMarkdown { get; private set; }
    public string PlainText { get; private set; }
    public string? CoverImage { get; private set; }
    public string? CoverAlt { get; private set; }

    public string SeoTitle { get; private set; }
    public bool SeoTitleLocked { get; private set; }
    public string MetaDescription { get; private set; }
    public bool MetaLocked { get; private set; }
    public string FocusKeyword { get; private set; }
    public bool KeywordLocked { get; private set; }
    public bool SlugLocked { get; private set; }
    public List<TocItem> Toc { get; private set; } = [];
    public List<SeoCheck> SeoChecks { get; private set; } = [];
    public List<FaqItem> Faq { get; private set; } = [];
    public int SeoScore { get; private set; }
    public int WordCount { get; private set; }
    public int ReadingMinutes { get; private set; }

    public DateTimeOffset? PublishAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    /// <summary>Last real content change (sitemap lastmod, Article dateModified).</summary>
    public DateTimeOffset ModifiedAt { get; private set; }
    public uint Version { get; private set; }

    public bool IsLive => Status == PostStatus.Published && !IsDeleted;

    [GeneratedRegex("^[\\p{L}\\p{N}]+(?:-[\\p{L}\\p{N}]+)*$")]
    private static partial Regex SlugPattern();

    public static Result<BlogPost> Create(Guid authorId, string title, DateTimeOffset now)
    {
        var p = new BlogPost { AuthorId = authorId, Status = PostStatus.Draft, ModifiedAt = now };
        var r = p.SetTitle(title);
        return r.IsSuccess ? p : r.Error!;
    }

    private Result SetTitle(string title)
    {
        title = title?.Trim() ?? "";
        if (title.Length is < 3 or > 150) return Error.Validation("blog.title", "عنوان بین ۳ تا ۱۵۰ کاراکتر.");
        Title = title;
        return Result.Success();
    }

    public sealed record EditSpec(string Title, string ContentJson, Guid? CategoryId, IReadOnlyList<string> Tags, string? CoverImage, string? CoverAlt,
        string? SeoTitle, string? MetaDescription, string? FocusKeyword, string? Slug);

    /// <summary>Content and the author's manual overrides. A non-empty override locks that field; an empty one unlocks it (back to automatic).</summary>
    public Result Edit(EditSpec s, DateTimeOffset now)
    {
        if (Status == PostStatus.Archived) return Error.Conflict("blog.archived", "این مطلب بایگانی شده است.");
        var t = SetTitle(s.Title);
        if (t.IsFailure) return t;
        if (string.IsNullOrWhiteSpace(s.ContentJson) || s.ContentJson.Length > MaxJson) return Error.Validation("blog.content", "متن مطلب خالی است یا خیلی بزرگ است.");
        if (s.CoverAlt is { Length: > 200 }) return Error.Validation("blog.alt", "متن جایگزین عکس حداکثر ۲۰۰ کاراکتر.");
        ContentJson = s.ContentJson; CategoryId = s.CategoryId;
        Tags = s.Tags.Select(x => x.Trim().TrimStart('#')).Where(x => x.Length is > 0 and <= 40).Distinct().Take(15).ToList();
        CoverImage = string.IsNullOrWhiteSpace(s.CoverImage) ? null : s.CoverImage; CoverAlt = string.IsNullOrWhiteSpace(s.CoverAlt) ? null : s.CoverAlt.Trim();

        SeoTitleLocked = !string.IsNullOrWhiteSpace(s.SeoTitle);
        if (SeoTitleLocked) { if (s.SeoTitle!.Trim().Length > 70) return Error.Validation("blog.seoTitle", "عنوان سئو حداکثر ۷۰ کاراکتر."); SeoTitle = s.SeoTitle.Trim(); }
        MetaLocked = !string.IsNullOrWhiteSpace(s.MetaDescription);
        if (MetaLocked) { if (s.MetaDescription!.Trim().Length > 170) return Error.Validation("blog.meta", "توضیح متا حداکثر ۱۷۰ کاراکتر."); MetaDescription = s.MetaDescription.Trim(); }
        KeywordLocked = !string.IsNullOrWhiteSpace(s.FocusKeyword);
        if (KeywordLocked) FocusKeyword = s.FocusKeyword!.Trim()[..Math.Min(60, s.FocusKeyword.Trim().Length)];
        SlugLocked = !string.IsNullOrWhiteSpace(s.Slug);
        if (SlugLocked)
        {
            var slug = Common.Slug.From(s.Slug, 80);
            if (!SlugPattern().IsMatch(slug)) return Error.Validation("blog.slug", "نامک فقط حروف، عدد و خط تیره.");
            Slug = slug;
        }
        ModifiedAt = now;
        return Result.Success();
    }

    /// <summary>Applies the automatic SEO pass. Returns the previous slug when a live post's URL changed (for a 301).</summary>
    public string? ApplySeo(SeoResult r, string uniqueSlug)
    {
        ContentHtml = r.Html; ContentMarkdown = r.Markdown; PlainText = r.PlainText;
        if (!SeoTitleLocked) SeoTitle = r.SeoTitle;
        if (!MetaLocked) MetaDescription = r.MetaDescription;
        if (!KeywordLocked) FocusKeyword = r.FocusKeyword;
        Toc = r.Toc.ToList(); WordCount = r.WordCount; ReadingMinutes = r.ReadingMinutes; SeoScore = r.Score; SeoChecks = r.Checks.ToList(); Faq = r.Faq.ToList();
        if (CoverImage is null && r.FirstImage is not null) CoverImage = r.FirstImage;
        string? old = null;
        if (Slug != uniqueSlug)
        {
            if (Status == PostStatus.Published && Slug.Length > 0) old = Slug;
            Slug = uniqueSlug;
        }
        return old;
    }

    public Result Submit()
    {
        if (Status is not (PostStatus.Draft)) return Error.Conflict("blog.state", "فقط پیش‌نویس برای بازبینی فرستاده می‌شود.");
        Status = PostStatus.InReview;
        return Result.Success();
    }

    /// <summary>Publish now, or schedule for later.</summary>
    public Result Publish(DateTimeOffset now, DateTimeOffset? at)
    {
        if (Status is PostStatus.Archived) return Error.Conflict("blog.archived", "این مطلب بایگانی شده است.");
        if (WordCount < 30) return Error.Validation("blog.tooShort", "متن خیلی کوتاه است (کمتر از ۳۰ کلمه).");
        if (at is { } t && t > now.AddMinutes(1))
        {
            Status = PostStatus.Scheduled; PublishAt = t;
            return Result.Success();
        }
        Status = PostStatus.Published; PublishAt = null;
        PublishedAt ??= now;
        return Result.Success();
    }

    public bool PublishIfDue(DateTimeOffset now)
    {
        if (Status != PostStatus.Scheduled || PublishAt is not { } at || at > now) return false;
        Status = PostStatus.Published; PublishedAt ??= at; PublishAt = null;
        return true;
    }

    public Result Unpublish()
    {
        if (Status is not (PostStatus.Published or PostStatus.Scheduled or PostStatus.InReview)) return Error.Conflict("blog.state", "این مطلب منتشر نشده است.");
        Status = PostStatus.Draft; PublishAt = null;
        return Result.Success();
    }

    public void Archive(Guid? by, DateTimeOffset now)
    {
        Status = PostStatus.Archived;
        MarkDeleted(by, now);
    }
}
