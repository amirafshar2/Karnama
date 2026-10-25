using System.Text.Json;
using FluentValidation;
using Karnama.Application.Abstractions;
using Karnama.Application.Blog;
using Karnama.Domain.Common;
using Karnama.Domain.Content;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Help;

public sealed record HelpItemDto(Guid Id, string Title, string Slug, string Category, HelpAudience Audience, string? PageKey, string Excerpt, int SortOrder, bool IsPublished);
public sealed record HelpArticleDto(Guid Id, string Title, string Slug, string Category, HelpAudience Audience, string? PageKey, string ContentHtml, string ContentJson,
    int SortOrder, bool IsPublished, DateTimeOffset UpdatedAt, bool CanEdit);
public sealed record SaveHelpRequest(string Title, string? Slug, string Category, HelpAudience Audience, string? PageKey, string ContentJson, int SortOrder, bool IsPublished);

public sealed class SaveHelpValidator : AbstractValidator<SaveHelpRequest>
{
    public SaveHelpValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(40);
        RuleFor(x => x.PageKey).MaximumLength(60);
        RuleFor(x => x.ContentJson).NotEmpty().MaximumLength(HelpArticle.MaxJson);
    }
}

/// <summary>
/// Help center (PROMPT2 §13): searchable articles by category, the article behind each page's "؟" button, and editing
/// for content managers. Staff read staff + everyone articles; clients read client + everyone articles; drafts only editors.
/// </summary>
public sealed class HelpService(IAppDbContext db, ICurrentUser me, IAuditWriter audit)
{
    private static Error NotFound => Error.NotFound("help.notFound", "مقاله پیدا نشد.");
    private bool CanEdit => me.Kind == UserKind.Staff && me.Has(Permissions.ContentManage, PermissionScope.All);

    private IQueryable<HelpArticle> Readable()
    {
        var q = db.HelpArticles.AsNoTracking();
        if (!CanEdit) q = q.Where(a => a.IsPublished);
        if (CanEdit) return q;
        return me.Kind == UserKind.Client
            ? q.Where(a => a.Audience == HelpAudience.Everyone || a.Audience == HelpAudience.Clients)
            : q.Where(a => a.Audience == HelpAudience.Everyone || a.Audience == HelpAudience.Staff);
    }

    public async Task<List<HelpItemDto>> ListAsync(string? q, string? category, CancellationToken ct)
    {
        var query = Readable();
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(a => a.Category == category);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim().Replace('ي', 'ی').Replace('ك', 'ک');
            if (t.Length > 100) t = t[..100];
            query = query.Where(a => EF.Functions.ILike(a.Title, $"%{t}%") || EF.Functions.ILike(a.PlainText, $"%{t}%"));
        }
        var list = await query.OrderBy(a => a.SortOrder).ThenBy(a => a.Title).Take(200).ToListAsync(ct);
        return list.Select(a => new HelpItemDto(a.Id, a.Title, a.Slug, a.Category, a.Audience, a.PageKey, Excerpt(a.PlainText), a.SortOrder, a.IsPublished)).ToList();
    }

    public async Task<Result<HelpArticleDto>> GetAsync(string slug, CancellationToken ct)
    {
        var a = await Readable().FirstOrDefaultAsync(x => x.Slug == slug, ct);
        return a is null ? NotFound : Dto(a);
    }

    /// <summary>The article for a page's "؟" panel: the longest page key that matches the path.</summary>
    public async Task<HelpItemDto?> ForPageAsync(string page, CancellationToken ct)
    {
        var key = (page ?? "").Trim().Trim('/');
        if (key.Length is 0 or > 100) return null;
        var candidates = await Readable().Where(a => a.PageKey != null).ToListAsync(ct);
        var best = candidates.Where(a => key == a.PageKey || key.StartsWith(a.PageKey + "/", StringComparison.Ordinal))
            .OrderByDescending(a => a.PageKey!.Length).ThenBy(a => a.SortOrder).FirstOrDefault();
        return best is null ? null : new HelpItemDto(best.Id, best.Title, best.Slug, best.Category, best.Audience, best.PageKey, Excerpt(best.PlainText), best.SortOrder, best.IsPublished);
    }

    public async Task<Result<HelpArticleDto>> SaveAsync(Guid? id, SaveHelpRequest r, CancellationToken ct)
    {
        if (!CanEdit) return Error.Forbidden();
        RenderOutput rendered;
        try
        {
            rendered = TiptapRenderer.Render(r.ContentJson);
        }
        catch (JsonException)
        {
            return Error.Validation("help.content", "متن مقاله خراب است؛ صفحه را دوباره باز کنید.");
        }
        var spec = new HelpSpec(r.Title, r.Slug, r.Category, r.Audience, r.PageKey, r.ContentJson, r.SortOrder, r.IsPublished);
        HelpArticle a;
        if (id is { } given)
        {
            var found = await db.HelpArticles.FirstOrDefaultAsync(x => x.Id == given, ct);
            if (found is null) return NotFound;
            var u = found.Update(spec, rendered.Html.ToString(), rendered.Plain.ToString());
            if (u.IsFailure) return u.Error!;
            a = found;
        }
        else
        {
            var c = HelpArticle.Create(spec, rendered.Html.ToString(), rendered.Plain.ToString());
            if (c.IsFailure) return c.Error!;
            a = c.Value!;
            db.HelpArticles.Add(a);
        }
        if (await db.HelpArticles.AnyAsync(x => x.Slug == a.Slug && x.Id != a.Id, ct)) return Error.Conflict("help.slugExists", "مقاله‌ای با همین نشانی هست؛ عنوان یا نشانی را عوض کنید.");
        audit.Add(id is null ? "help.created" : "help.updated", nameof(HelpArticle), a.Id.ToString(), new { a.Title, a.Slug, a.IsPublished });
        await db.SaveChangesAsync(ct);
        return Dto(a);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!CanEdit) return Error.Forbidden();
        var a = await db.HelpArticles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound;
        a.Delete(me.UserId, DateTimeOffset.UtcNow);
        audit.Add("help.deleted", nameof(HelpArticle), a.Id.ToString(), new { a.Title });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private HelpArticleDto Dto(HelpArticle a) => new(a.Id, a.Title, a.Slug, a.Category, a.Audience, a.PageKey, a.ContentHtml, CanEdit ? a.ContentJson : "",
        a.SortOrder, a.IsPublished, a.UpdatedAt ?? a.CreatedAt, CanEdit);

    private static string Excerpt(string plain)
    {
        var t = plain.Replace('\n', ' ').Trim();
        return t.Length <= 160 ? t : t[..160].TrimEnd() + "…";
    }
}
