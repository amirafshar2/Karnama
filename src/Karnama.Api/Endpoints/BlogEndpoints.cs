using Karnama.Api.Infrastructure;
using Karnama.Application.Blog;
using Karnama.Application.Common;
using Karnama.Domain.Content;
using Karnama.Domain.Identity;

namespace Karnama.Api.Endpoints;

/// <summary>N9: blog admin (write/review/publish, media, automatic SEO preview) and the public, read-only blog API.</summary>
internal static class BlogEndpoints
{
    public static void MapBlog(this IEndpointRouteBuilder app)
    {
        // Who may do what is decided in BlogService (own drafts vs. editors); here only "signed-in staff with some blog right".
        var blog = app.MapGroup("/api/v1/blog").WithTags("Blog").RequirePermission(Permissions.BlogWrite, PermissionScope.Own).AddEndpointFilter<ValidationFilter>();
        blog.MapGet("/posts", async (string? cursor, int? limit, string? q, string? status, BlogService svc, HttpContext http, CancellationToken ct) =>
            (await svc.ListAsync(new KeysetQuery(cursor, limit ?? 20, q), CatalogEndpoints.E<PostStatus>(status), ct)).ToHttp(http));
        blog.MapGet("/posts/{id:guid}", async (Guid id, BlogService svc, HttpContext http, CancellationToken ct) => (await svc.GetAsync(id, ct)).ToHttp(http));
        blog.MapPost("/posts", async (SavePostRequest body, BlogService svc, HttpContext http, CancellationToken ct) =>
            (await svc.CreateAsync(body, ct)).ToHttp(http, r => Results.Created($"/api/v1/blog/posts/{r.Id}", r)));
        blog.MapPut("/posts/{id:guid}", async (Guid id, SavePostRequest body, BlogService svc, HttpContext http, CancellationToken ct) => (await svc.UpdateAsync(id, body, ct)).ToHttp(http));
        blog.MapPost("/posts/{id:guid}/submit", async (Guid id, BlogService svc, HttpContext http, CancellationToken ct) => (await svc.SubmitAsync(id, ct)).ToHttp(http));
        blog.MapPost("/posts/{id:guid}/publish", async (Guid id, PublishPostRequest body, BlogService svc, HttpContext http, CancellationToken ct) => (await svc.PublishAsync(id, body, ct)).ToHttp(http));
        blog.MapPost("/posts/{id:guid}/unpublish", async (Guid id, BlogService svc, HttpContext http, CancellationToken ct) => (await svc.UnpublishAsync(id, ct)).ToHttp(http));
        blog.MapDelete("/posts/{id:guid}", async (Guid id, BlogService svc, HttpContext http, CancellationToken ct) => (await svc.ArchiveAsync(id, ct)).ToHttp(http));
        blog.MapPost("/seo-preview", async (SeoPreviewRequest body, BlogService svc, HttpContext http, CancellationToken ct) => (await svc.PreviewAsync(body, ct)).ToHttp(http));
        blog.MapPost("/markdown", (MarkdownRequest body, BlogService svc, HttpContext http) => svc.MarkdownToHtml(body).ToHttp(http, html => Results.Ok(new { html })));
        blog.MapPost("/media", async (IFormFile file, BlogService svc, HttpContext http, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            return (await svc.UploadAsync(stream, file.Length, ct)).ToHttp(http);
        }).DisableAntiforgery().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(3 * 1024 * 1024));
        blog.MapGet("/categories", async (BlogService svc, CancellationToken ct) => Results.Ok(await svc.CategoriesAsync(ct)));
        blog.MapPost("/categories", async (UpsertCategoryRequest body, BlogService svc, HttpContext http, CancellationToken ct) => (await svc.SaveCategoryAsync(null, body, ct)).ToHttp(http));
        blog.MapPut("/categories/{id:guid}", async (Guid id, UpsertCategoryRequest body, BlogService svc, HttpContext http, CancellationToken ct) => (await svc.SaveCategoryAsync(id, body, ct)).ToHttp(http));
        blog.MapDelete("/categories/{id:guid}", async (Guid id, BlogService svc, HttpContext http, CancellationToken ct) => (await svc.DeleteCategoryAsync(id, ct)).ToHttp(http));

        // ---- Public (anonymous, server-rendered site reads these) ----
        var pub = app.MapGroup("/api/v1/public/blog").WithTags("Public").AllowAnonymous();
        pub.MapGet("/", async (int? page, string? category, string? tag, BlogService svc, CancellationToken ct) =>
            Results.Ok(await svc.PublicListAsync(page ?? 1, category, tag, ct)));
        pub.MapGet("/posts/{slug}", async (string slug, BlogService svc, CancellationToken ct) =>
        {
            var r = await svc.PublicGetAsync(slug, ct);
            if (r.Post is not null) return Results.Ok(r.Post);
            return r.RedirectTo is { } to ? Results.Ok(new { redirectTo = to }) : Results.NotFound();
        });
        pub.MapGet("/sitemap", async (BlogService svc, CancellationToken ct) => Results.Ok(await svc.SitemapAsync(ct)));
    }
}
