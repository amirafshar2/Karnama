using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Application.Common;
using Karnama.Domain.Common;
using Karnama.Domain.Content;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Blog;

public sealed record PostListItemDto(Guid Id, string Title, string Slug, PostStatus Status, Guid AuthorId, string AuthorName, string? CategoryName,
    int SeoScore, int WordCount, DateTimeOffset? PublishedAt, DateTimeOffset? PublishAt, DateTimeOffset ModifiedAt);

public sealed record PostDto(Guid Id, string Title, string Slug, PostStatus Status, Guid AuthorId, string AuthorName, Guid? CategoryId, IReadOnlyList<string> Tags,
    string ContentJson, string? CoverImage, string? CoverUrl, string? CoverAlt,
    string SeoTitle, bool SeoTitleLocked, string MetaDescription, bool MetaLocked, string FocusKeyword, bool KeywordLocked, bool SlugLocked,
    IReadOnlyList<TocItem> Toc, IReadOnlyList<SeoCheck> SeoChecks, int SeoScore, int WordCount, int ReadingMinutes,
    DateTimeOffset? PublishAt, DateTimeOffset? PublishedAt, DateTimeOffset ModifiedAt, uint Version, bool CanEdit, bool CanPublish);

public sealed record SavePostRequest(string Title, string ContentJson, Guid? CategoryId, List<string>? Tags, string? CoverImage, string? CoverAlt,
    string? SeoTitle, string? MetaDescription, string? FocusKeyword, string? Slug, uint? Version);

public sealed record SeoPreviewRequest(Guid? PostId, string Title, string ContentJson, string? SeoTitle, string? MetaDescription, string? FocusKeyword);
public sealed record LinkSuggestionDto(string Title, string Url, string Reason);
public sealed record SeoPreviewDto(string SeoTitle, string MetaDescription, string Slug, string FocusKeyword, IReadOnlyList<SeoCheck> Checks, int Score,
    int WordCount, int ReadingMinutes, IReadOnlyList<TocItem> Toc, int AltFilled, IReadOnlyList<LinkSuggestionDto> Links);
public sealed record PublishPostRequest(DateTimeOffset? At, uint Version);
public sealed record CategoryDto(Guid Id, string Name, string Slug, string? Description, int SortOrder, int Posts);
public sealed record UpsertCategoryRequest(string Name, string? Description, int SortOrder);
public sealed record MarkdownRequest(string Markdown);

public sealed record PublicCardDto(string Title, string Slug, string Excerpt, string? CoverUrl, string? CoverAlt, string? CategoryName, string? CategorySlug,
    string AuthorName, DateTimeOffset PublishedAt, int ReadingMinutes);
public sealed record PublicPostDto(Guid Id, string Title, string Slug, string Html, IReadOnlyList<TocItem> Toc, IReadOnlyList<FaqItem> Faq,
    string SeoTitle, string MetaDescription, string FocusKeyword, string? CoverUrl, string? CoverAlt, string? CategoryName, string? CategorySlug,
    IReadOnlyList<string> Tags, string AuthorName, DateTimeOffset PublishedAt, DateTimeOffset ModifiedAt, int ReadingMinutes, int WordCount,
    IReadOnlyList<PublicCardDto> Related);
public sealed record PublicPostLookup(PublicPostDto? Post, string? RedirectTo);
public sealed record PublicListDto(IReadOnlyList<PublicCardDto> Items, int Page, int Pages, IReadOnlyList<CategoryDto> Categories);
public sealed record SitemapEntry(string Path, DateTimeOffset Modified);

public sealed class SavePostValidator : AbstractValidator<SavePostRequest>
{
    public SavePostValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(150).WithMessage("عنوان الزامی است (حداکثر ۱۵۰ کاراکتر).");
        RuleFor(x => x.ContentJson).NotEmpty().MaximumLength(BlogPost.MaxJson).WithMessage("متن مطلب خیلی بزرگ است.");
        RuleFor(x => x.SeoTitle).MaximumLength(70).WithMessage("عنوان سئو حداکثر ۷۰ کاراکتر.");
        RuleFor(x => x.MetaDescription).MaximumLength(170).WithMessage("توضیح متا حداکثر ۱۷۰ کاراکتر.");
        RuleFor(x => x.Slug).MaximumLength(80).WithMessage("نامک حداکثر ۸۰ کاراکتر.");
    }
}

/// <summary>
/// The blog (docs/PROMPT2.md §12.3 and §18): writers draft and submit, editors publish or schedule; every save runs the
/// automatic SEO pass on the server, and the public site reads only live posts.
/// </summary>
public sealed class BlogService(IAppDbContext db, ICurrentUser me, IAuditWriter audit, TimeProvider clock, IMediaStore media, IContentRenderer renderer,
    Settings.BrandService brand, IPublicCache cache)
{
    private bool CanWriteAll => me.Kind == UserKind.Staff && me.Has(Permissions.BlogWrite, PermissionScope.All);
    private bool CanWrite => me.Kind == UserKind.Staff && me.Has(Permissions.BlogWrite, PermissionScope.Own);
    private bool CanPublish => me.Kind == UserKind.Staff && me.Has(Permissions.BlogPublish, PermissionScope.All);
    private Guid Me => me.UserId ?? Guid.Empty;

    /// <summary>Writers edit their own drafts; editors (publish or write:All) edit anything.</summary>
    private bool CanEdit(BlogPost p) => CanPublish || CanWriteAll || (CanWrite && p.AuthorId == Me && p.Status is PostStatus.Draft or PostStatus.InReview);

    private IQueryable<BlogPost> Visible() => CanPublish || CanWriteAll ? db.BlogPosts : db.BlogPosts.Where(p => p.AuthorId == Me);

    public async Task<Result<KeysetPage<PostListItemDto>>> ListAsync(KeysetQuery q, PostStatus? status, CancellationToken ct)
    {
        if (!CanWrite && !CanPublish) return Error.Forbidden();
        var query = Visible().AsNoTracking();
        if (status is { } s) query = query.Where(p => p.Status == s);
        if (!string.IsNullOrWhiteSpace(q.Search)) query = query.Where(p => EF.Functions.ILike(p.Title, "%" + q.Search.Trim() + "%"));
        var page = await Keyset.PageAsync(query, q, x => x, x => x.Id, ct);
        var rows = page.Items;
        var authors = await NamesAsync(rows.Select(r => r.AuthorId), ct);
        var cats = await db.BlogCategories.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var items = rows.Select(p => new PostListItemDto(p.Id, p.Title, p.Slug, p.Status, p.AuthorId, authors.GetValueOrDefault(p.AuthorId) ?? "—",
            p.CategoryId is { } cid ? cats.GetValueOrDefault(cid) : null, p.SeoScore, p.WordCount, p.PublishedAt, p.PublishAt, p.ModifiedAt)).ToList();
        return new KeysetPage<PostListItemDto>(items, page.NextCursor);
    }

    public async Task<Result<PostDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await Visible().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("blog.notFound", "مطلب پیدا نشد.");
        return await DtoAsync(p, ct);
    }

    public async Task<Result<PostDto>> CreateAsync(SavePostRequest r, CancellationToken ct)
    {
        if (!CanWrite) return Error.Forbidden();
        var created = BlogPost.Create(Me, r.Title, clock.GetUtcNow());
        if (created.IsFailure) return created.Error!;
        var p = created.Value!;
        var applied = await ApplyAsync(p, r, ct);
        if (applied.IsFailure) return applied.Error!;
        db.BlogPosts.Add(p);
        audit.Add("blog.created", nameof(BlogPost), p.Id.ToString());
        await db.SaveChangesAsync(ct);
        return await DtoAsync(p, ct);
    }

    public async Task<Result<PostDto>> UpdateAsync(Guid id, SavePostRequest r, CancellationToken ct)
    {
        var p = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("blog.notFound", "مطلب پیدا نشد.");
        if (!CanEdit(p)) return Error.Forbidden("blog.locked", "مطلب منتشرشده را فقط ویراستار تغییر می‌دهد.");
        if (r.Version is not { } v) return Error.Validation("common.versionRequired", "نسخه‌ی رکورد لازم است؛ صفحه را تازه کنید.");
        db.SetOriginalVersion(p, v);
        var applied = await ApplyAsync(p, r, ct);
        if (applied.IsFailure) return applied.Error!;
        audit.Add("blog.updated", nameof(BlogPost), p.Id.ToString());
        await db.SaveChangesAsync(ct);
        if (p.Status == PostStatus.Published) await cache.EvictAsync(ct);
        return await DtoAsync(p, ct);
    }

    /// <summary>Edit + automatic SEO + a unique slug (+ a 301 from the old URL if a live post moved).</summary>
    private async Task<Result> ApplyAsync(BlogPost p, SavePostRequest r, CancellationToken ct)
    {
        if (r.CategoryId is { } cat && !await db.BlogCategories.AnyAsync(c => c.Id == cat, ct)) return Error.Validation("blog.category", "دسته پیدا نشد.");
        if (r.CoverImage is { Length: > 0 } cover && media.OpenPublic(cover) is not { } opened) return Error.Validation("blog.cover", "عکس کاور پیدا نشد.");
        else if (r.CoverImage is { Length: > 0 }) media.OpenPublic(r.CoverImage)?.Stream.Dispose();
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(r.ContentJson, new System.Text.Json.JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (System.Text.Json.JsonException) { return Error.Validation("blog.content", "متن ویرایشگر خراب است؛ صفحه را تازه کنید."); }

        var e = p.Edit(new BlogPost.EditSpec(r.Title, r.ContentJson, r.CategoryId, r.Tags ?? [], r.CoverImage, r.CoverAlt, r.SeoTitle, r.MetaDescription, r.FocusKeyword, r.Slug), clock.GetUtcNow());
        if (e.IsFailure) return e;
        var b = await brand.GetAsync(ct);
        var seo = SeoAutomation.Run(p.Title, p.ContentJson, b.Name, p.SeoTitleLocked ? p.SeoTitle : null, p.MetaLocked ? p.MetaDescription : null, p.KeywordLocked ? p.FocusKeyword : null);
        var wanted = p.SlugLocked ? p.Slug : seo.Slug;
        if (string.IsNullOrEmpty(wanted)) wanted = "post";
        var unique = await UniqueSlugAsync(wanted, p.Id, ct);
        var old = p.ApplySeo(seo, unique);
        if (old is not null && !await db.BlogSlugRedirects.AnyAsync(x => x.OldSlug == old, ct))
            db.BlogSlugRedirects.Add(new BlogSlugRedirect(old, p.Id, clock.GetUtcNow()));
        // A redirect that pointed at the new slug would loop.
        await db.BlogSlugRedirects.Where(x => x.OldSlug == unique).ExecuteDeleteAsync(ct);
        return Result.Success();
    }

    private async Task<string> UniqueSlugAsync(string slug, Guid self, CancellationToken ct)
    {
        var taken = await db.BlogPosts.IgnoreQueryFilters().Where(x => x.Id != self && (x.Slug == slug || x.Slug.StartsWith(slug + "-"))).Select(x => x.Slug).ToListAsync(ct);
        var redirects = await db.BlogSlugRedirects.Where(x => x.PostId != self && (x.OldSlug == slug || x.OldSlug.StartsWith(slug + "-"))).Select(x => x.OldSlug).ToListAsync(ct);
        var all = taken.Concat(redirects).ToHashSet(StringComparer.Ordinal);
        if (!all.Contains(slug)) return slug;
        for (var i = 2; ; i++) if (!all.Contains($"{slug}-{i}")) return $"{slug}-{i}";
    }

    public async Task<Result<SeoPreviewDto>> PreviewAsync(SeoPreviewRequest r, CancellationToken ct)
    {
        if (!CanWrite && !CanPublish) return Error.Forbidden();
        if (r.ContentJson.Length > BlogPost.MaxJson) return Error.Validation("blog.content", "متن مطلب خیلی بزرگ است.");
        SeoResult seo;
        try
        {
            var b = await brand.GetAsync(ct);
            seo = SeoAutomation.Run(r.Title ?? "", r.ContentJson, b.Name, Blank(r.SeoTitle), Blank(r.MetaDescription), Blank(r.FocusKeyword));
        }
        catch (System.Text.Json.JsonException) { return Error.Validation("blog.content", "متن ویرایشگر خراب است."); }
        var keyword = Blank(r.FocusKeyword) ?? seo.FocusKeyword;
        var links = await SuggestLinksAsync(r.PostId, seo.PlainText + " " + r.Title, keyword, seo.Html, ct);
        return new SeoPreviewDto(Blank(r.SeoTitle) ?? seo.SeoTitle, Blank(r.MetaDescription) ?? seo.MetaDescription, seo.Slug, keyword, seo.Checks, seo.Score,
            seo.WordCount, seo.ReadingMinutes, seo.Toc, seo.AltFilled, links);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Internal links: other live posts whose keyword or title words appear in this text, and the matching category pages.</summary>
    private async Task<IReadOnlyList<LinkSuggestionDto>> SuggestLinksAsync(Guid? self, string text, string keyword, string html, CancellationToken ct)
    {
        var norm = SeoAutomation.Normalize(text);
        var words = SeoAutomation.Words(text).ToHashSet(StringComparer.Ordinal);
        var kwWords = SeoAutomation.Words(keyword).ToHashSet(StringComparer.Ordinal);
        var posts = await db.BlogPosts.AsNoTracking().Where(p => p.Status == PostStatus.Published && p.Id != self)
            .OrderByDescending(p => p.PublishedAt).Take(300).Select(p => new { p.Title, p.Slug, p.FocusKeyword, p.Tags }).ToListAsync(ct);
        var scored = new List<(double Score, LinkSuggestionDto Dto)>();
        foreach (var p in posts)
        {
            var url = "/blog/" + p.Slug;
            if (html.Contains($"href=\"{url}\"", StringComparison.Ordinal)) continue;   // already linked
            double score = 0;
            string reason = "";
            if (p.FocusKeyword.Length > 0 && norm.Contains(SeoAutomation.Normalize(p.FocusKeyword), StringComparison.Ordinal)) { score += 5; reason = p.FocusKeyword; }
            var shared = SeoAutomation.Words(p.Title).Where(w => w.Length > 2 && words.Contains(w)).Distinct().ToList();
            score += shared.Count + shared.Count(kwWords.Contains) * 2 + p.Tags.Count(t => words.Contains(SeoAutomation.Normalize(t).ToLowerInvariant()));
            if (reason.Length == 0 && shared.Count > 0) reason = string.Join("، ", shared.Take(3));
            if (score >= 2) scored.Add((score, new LinkSuggestionDto(p.Title, url, reason)));
        }
        return scored.OrderByDescending(s => s.Score).Take(5).Select(s => s.Dto).ToList();
    }

    public async Task<Result<PostDto>> SubmitAsync(Guid id, CancellationToken ct)
    {
        var p = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("blog.notFound", "مطلب پیدا نشد.");
        if (p.AuthorId != Me && !CanPublish) return Error.Forbidden();
        var r = p.Submit();
        if (r.IsFailure) return r.Error!;
        audit.Add("blog.submitted", nameof(BlogPost), p.Id.ToString());
        await db.SaveChangesAsync(ct);
        return await DtoAsync(p, ct);
    }

    public async Task<Result<PostDto>> PublishAsync(Guid id, PublishPostRequest r, CancellationToken ct)
    {
        if (!CanPublish) return Error.Forbidden("blog.publishOnly", "انتشار با ویراستار است.");
        var p = await db.BlogPosts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("blog.notFound", "مطلب پیدا نشد.");
        db.SetOriginalVersion(p, r.Version);
        var res = p.Publish(clock.GetUtcNow(), r.At);
        if (res.IsFailure) return res.Error!;
        audit.Add(p.Status == PostStatus.Scheduled ? "blog.scheduled" : "blog.published", nameof(BlogPost), p.Id.ToString(), new { r.At });
        await db.SaveChangesAsync(ct);
        await cache.EvictAsync(ct);
        return await DtoAsync(p, ct);
    }

    public async Task<Result<PostDto>> UnpublishAsync(Guid id, CancellationToken ct)
    {
        if (!CanPublish) return Error.Forbidden();
        var p = await db.BlogPosts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("blog.notFound", "مطلب پیدا نشد.");
        var res = p.Unpublish();
        if (res.IsFailure) return res.Error!;
        audit.Add("blog.unpublished", nameof(BlogPost), p.Id.ToString());
        await db.SaveChangesAsync(ct);
        await cache.EvictAsync(ct);
        return await DtoAsync(p, ct);
    }

    public async Task<Result> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var p = await Visible().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("blog.notFound", "مطلب پیدا نشد.");
        if (!CanPublish && !(p.AuthorId == Me && p.Status is PostStatus.Draft)) return Error.Forbidden();
        p.Archive(me.UserId, clock.GetUtcNow());
        audit.Add("blog.archived", nameof(BlogPost), p.Id.ToString());
        await db.SaveChangesAsync(ct);
        await cache.EvictAsync(ct);
        return Result.Success();
    }

    /// <summary>Worker, every minute: scheduled posts go live.</summary>
    public async Task<int> PublishDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = await db.BlogPosts.Where(p => p.Status == PostStatus.Scheduled && p.PublishAt <= now).Take(100).ToListAsync(ct);
        foreach (var p in due) p.PublishIfDue(now);
        if (due.Count > 0) { await db.SaveChangesAsync(ct); await cache.EvictAsync(ct); }
        return due.Count;
    }

    public async Task<Result<MediaUploaded>> UploadAsync(Stream content, long length, CancellationToken ct)
    {
        if (!CanWrite && !CanPublish) return Error.Forbidden();
        var saved = await media.SaveImageAsync(content, length, "blog", ct);
        if (saved.IsFailure) return saved.Error!;
        return new MediaUploaded(saved.Value!, media.PublicUrl(saved.Value!));
    }

    public Result<string> MarkdownToHtml(MarkdownRequest r)
    {
        if (!CanWrite && !CanPublish) return Error.Forbidden();
        if (r.Markdown is null || r.Markdown.Length > 200_000) return Error.Validation("blog.markdown", "متن خیلی بزرگ است.");
        return renderer.MarkdownToSafeHtml(r.Markdown);
    }

    // ---- Categories ----
    public async Task<IReadOnlyList<CategoryDto>> CategoriesAsync(CancellationToken ct)
    {
        var counts = await db.BlogPosts.AsNoTracking().Where(p => p.Status == PostStatus.Published && p.CategoryId != null)
            .GroupBy(p => p.CategoryId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        return (await db.BlogCategories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct))
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.Description, c.SortOrder, counts.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<Result<CategoryDto>> SaveCategoryAsync(Guid? id, UpsertCategoryRequest r, CancellationToken ct)
    {
        if (!CanPublish) return Error.Forbidden();
        BlogCategory c;
        if (id is { } cid)
        {
            c = (await db.BlogCategories.FirstOrDefaultAsync(x => x.Id == cid, ct))!;
            if (c is null) return Error.NotFound();
            var u = c.Update(r.Name, r.Description, r.SortOrder);
            if (u.IsFailure) return u.Error!;
        }
        else
        {
            var created = BlogCategory.Create(r.Name, r.Description, r.SortOrder);
            if (created.IsFailure) return created.Error!;
            c = created.Value!;
            db.BlogCategories.Add(c);
        }
        if (await db.BlogCategories.AnyAsync(x => x.Slug == c.Slug && x.Id != c.Id, ct)) return Error.Conflict("blog.categoryExists", "دسته‌ای با همین نام هست.");
        await db.SaveChangesAsync(ct);
        return new CategoryDto(c.Id, c.Name, c.Slug, c.Description, c.SortOrder, 0);
    }

    public async Task<Result> DeleteCategoryAsync(Guid id, CancellationToken ct)
    {
        if (!CanPublish) return Error.Forbidden();
        await db.BlogPosts.IgnoreQueryFilters().Where(p => p.CategoryId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, (Guid?)null), ct);
        await db.BlogCategories.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        return Result.Success();
    }

    // ---- Public site (anonymous; only live posts) ----
    private IQueryable<BlogPost> Live() => db.BlogPosts.AsNoTracking().Where(p => p.Status == PostStatus.Published);

    public async Task<PublicListDto> PublicListAsync(int page, string? category, string? tag, CancellationToken ct)
    {
        const int size = 12;
        var q = Live();
        var cats = await CategoriesAsync(ct);
        if (!string.IsNullOrWhiteSpace(category))
        {
            var c = cats.FirstOrDefault(x => x.Slug == category);
            q = c is null ? q.Where(_ => false) : q.Where(p => p.CategoryId == c.Id);
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var withTag = (await q.Select(p => new { p.Id, p.Tags }).ToListAsync(ct)).Where(x => x.Tags.Contains(tag.Trim())).Select(x => x.Id).ToList();
            q = q.Where(p => withTag.Contains(p.Id));
        }
        var total = await q.CountAsync(ct);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)size));
        page = Math.Clamp(page, 1, pages);
        var rows = await q.OrderByDescending(p => p.PublishedAt).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PublicListDto(await CardsAsync(rows, ct), page, pages, cats.Where(c => c.Posts > 0).ToList());
    }

    public async Task<PublicPostLookup> PublicGetAsync(string slug, CancellationToken ct)
    {
        slug = Slug.From(slug, 200);
        var p = await Live().FirstOrDefaultAsync(x => x.Slug == slug, ct);
        if (p is null)
        {
            var moved = await db.BlogSlugRedirects.AsNoTracking().Where(r => r.OldSlug == slug)
                .Join(Live(), r => r.PostId, x => x.Id, (r, x) => x.Slug).FirstOrDefaultAsync(ct);
            return new PublicPostLookup(null, moved);
        }
        var authors = await NamesAsync([p.AuthorId], ct);
        var cat = p.CategoryId is { } cid ? await db.BlogCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, ct) : null;
        // Tags are a JSON column: rank the recent posts in memory (same category, shared tags), newest first.
        var recent = await Live().Where(x => x.Id != p.Id).OrderByDescending(x => x.PublishedAt).Take(200)
            .Select(x => new { x.Id, x.CategoryId, x.Tags }).ToListAsync(ct);
        var pick = recent.Select((x, i) => (x.Id, Score: (p.CategoryId != null && x.CategoryId == p.CategoryId ? 2 : 0) + x.Tags.Count(p.Tags.Contains), i))
            .OrderByDescending(x => x.Score).ThenBy(x => x.i).Take(3).Select(x => x.Id).ToList();
        var related = (await Live().Where(x => pick.Contains(x.Id)).ToListAsync(ct)).OrderBy(x => pick.IndexOf(x.Id)).ToList();
        return new PublicPostLookup(new PublicPostDto(p.Id, p.Title, p.Slug, p.ContentHtml, p.Toc, p.Faq, p.SeoTitle, p.MetaDescription, p.FocusKeyword,
            p.CoverImage is null ? null : media.PublicUrl(p.CoverImage), p.CoverAlt ?? p.Title, cat?.Name, cat?.Slug, p.Tags, authors.GetValueOrDefault(p.AuthorId) ?? "",
            p.PublishedAt ?? p.ModifiedAt, p.ModifiedAt, p.ReadingMinutes, p.WordCount, await CardsAsync(related, ct)), null);
    }

    public async Task<IReadOnlyList<SitemapEntry>> SitemapAsync(CancellationToken ct)
    {
        var posts = await Live().Select(p => new { p.Slug, p.ModifiedAt }).ToListAsync(ct);
        var list = posts.Select(p => new SitemapEntry("/blog/" + p.Slug, p.ModifiedAt)).ToList();
        var newest = posts.Count == 0 ? (DateTimeOffset?)null : posts.Max(p => p.ModifiedAt);
        if (newest is { } n) list.Add(new SitemapEntry("/blog", n));
        foreach (var c in (await CategoriesAsync(ct)).Where(c => c.Posts > 0)) list.Add(new SitemapEntry("/blog?category=" + Uri.EscapeDataString(c.Slug), newest ?? clock.GetUtcNow()));
        return list;
    }

    private async Task<IReadOnlyList<PublicCardDto>> CardsAsync(IReadOnlyList<BlogPost> rows, CancellationToken ct)
    {
        var authors = await NamesAsync(rows.Select(r => r.AuthorId), ct);
        var cats = await db.BlogCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        return rows.Select(p =>
        {
            var c = p.CategoryId is { } id ? cats.GetValueOrDefault(id) : null;
            return new PublicCardDto(p.Title, p.Slug, p.MetaDescription, p.CoverImage is null ? null : media.PublicUrl(p.CoverImage), p.CoverAlt ?? p.Title,
                c?.Name, c?.Slug, authors.GetValueOrDefault(p.AuthorId) ?? "", p.PublishedAt ?? p.ModifiedAt, p.ReadingMinutes);
        }).ToList();
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => list.Contains(u.Id)).Select(u => new { u.Id, u.DisplayName }).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    private async Task<PostDto> DtoAsync(BlogPost p, CancellationToken ct)
    {
        var authors = await NamesAsync([p.AuthorId], ct);
        return new PostDto(p.Id, p.Title, p.Slug, p.Status, p.AuthorId, authors.GetValueOrDefault(p.AuthorId) ?? "—", p.CategoryId, p.Tags, p.ContentJson,
            p.CoverImage, p.CoverImage is null ? null : media.PublicUrl(p.CoverImage), p.CoverAlt,
            p.SeoTitle, p.SeoTitleLocked, p.MetaDescription, p.MetaLocked, p.FocusKeyword, p.KeywordLocked, p.SlugLocked,
            p.Toc, p.SeoChecks, p.SeoScore, p.WordCount, p.ReadingMinutes, p.PublishAt, p.PublishedAt, p.ModifiedAt, p.Version, CanEdit(p), CanPublish);
    }
}

public sealed record MediaUploaded(string Id, string Url);
