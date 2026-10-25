using Karnama.Api.Infrastructure;
using Karnama.Application.Common;
using Karnama.Application.Site;
using Karnama.Domain.Crm;
using Karnama.Domain.Identity;

namespace Karnama.Api.Endpoints;

/// <summary>N10: the public website's data, home-page content editing, portfolio admin and consultation requests.</summary>
internal static class SiteEndpoints
{
    public static void MapSite(this IEndpointRouteBuilder app)
    {
        var pub = app.MapGroup("/api/v1/public/site").WithTags("Public").AllowAnonymous().AddEndpointFilter<ValidationFilter>();
        var read = pub.MapGroup("").CacheOutput(PublicCache.Policy).DisableRateLimiting();
        read.MapGet("/home", async (PublicSiteService svc, CancellationToken ct) => Results.Ok(await svc.HomeAsync(ct)));
        read.MapGet("/packages", async (PublicSiteService svc, CancellationToken ct) => Results.Ok(await svc.PackagesAsync(ct)));
        read.MapGet("/packages/{slug}", async (string slug, PublicSiteService svc, CancellationToken ct) => await svc.PackageAsync(slug, ct) is { } p ? Results.Ok(p) : Results.NotFound());
        read.MapGet("/cases", async (PublicSiteService svc, CancellationToken ct) => Results.Ok(await svc.CasesAsync(ct)));
        read.MapGet("/cases/{slug}", async (string slug, PublicSiteService svc, CancellationToken ct) => await svc.CaseAsync(slug, ct) is { } c ? Results.Ok(c) : Results.NotFound());
        read.MapGet("/sitemap", async (PublicSiteService svc, CancellationToken ct) => Results.Ok(await svc.SitemapAsync(ct)));
        pub.MapPost("/leads", async (LeadRequest body, PublicSiteService svc, HttpContext http, CancellationToken ct) =>
        {
            var r = await svc.LeadAsync(body, ct);
            return r.IsSuccess ? Results.Accepted() : r.ToHttp(http);
        }).RequireRateLimiting(RateLimits.PublicForm);

        var home = app.MapGroup("/api/v1/site").WithTags("Site").RequirePermission(Permissions.ContentManage, PermissionScope.All).AddEndpointFilter<ValidationFilter>();
        home.MapGet("/home", async (SiteContentStore store, CancellationToken ct) => Results.Ok(await store.GetAsync(ct)));
        home.MapPut("/home", async (HomeContent body, SiteContentStore store, HttpContext http, CancellationToken ct) => (await store.SaveAsync(body, ct)).ToHttp(http));

        var cases = app.MapGroup("/api/v1/cases").WithTags("Cases").RequirePermission(Permissions.ContentManage, PermissionScope.All).AddEndpointFilter<ValidationFilter>();
        cases.MapGet("/", async (CaseStudyService svc, HttpContext http, CancellationToken ct) => (await svc.ListAsync(ct)).ToHttp(http));
        cases.MapGet("/{id:guid}", async (Guid id, CaseStudyService svc, HttpContext http, CancellationToken ct) => (await svc.GetAsync(id, ct)).ToHttp(http));
        cases.MapPost("/", async (SaveCaseRequest body, CaseStudyService svc, HttpContext http, CancellationToken ct) =>
            (await svc.SaveAsync(null, body, ct)).ToHttp(http, r => Results.Created($"/api/v1/cases/{r.Id}", r)));
        cases.MapPut("/{id:guid}", async (Guid id, SaveCaseRequest body, CaseStudyService svc, HttpContext http, CancellationToken ct) => (await svc.SaveAsync(id, body, ct)).ToHttp(http));
        cases.MapPost("/{id:guid}/publish", async (Guid id, PublishCaseRequest body, CaseStudyService svc, HttpContext http, CancellationToken ct) => (await svc.PublishAsync(id, body, ct)).ToHttp(http));
        cases.MapDelete("/{id:guid}", async (Guid id, CaseStudyService svc, HttpContext http, CancellationToken ct) => (await svc.DeleteAsync(id, ct)).ToHttp(http));
        cases.MapPost("/media", async (IFormFile file, bool? consent, CaseStudyService svc, HttpContext http, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return (await svc.UploadAsync(s, file.Length, consent == true, ct)).ToHttp(http);
        }).DisableAntiforgery().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(3 * 1024 * 1024));
        // The signed consent is private: only content managers can open it, never cached by proxies.
        cases.MapGet("/consent/{file}", (string file, CaseStudyService svc, HttpContext http) =>
        {
            var r = svc.OpenConsent(file);
            if (r.IsFailure) return r.ToHttp(http);
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.Stream(r.Value!.Stream, r.Value.ContentType);
        });

        var leads = app.MapGroup("/api/v1/leads").WithTags("Leads").RequirePermission(Permissions.LeadsManage, PermissionScope.All).AddEndpointFilter<ValidationFilter>();
        leads.MapGet("/", async (string? cursor, int? limit, string? q, string? status, PublicSiteService svc, HttpContext http, CancellationToken ct) =>
            (await svc.LeadsAsync(new KeysetQuery(cursor, limit ?? 30, q), CatalogEndpoints.E<LeadStatus>(status), ct)).ToHttp(http));
        leads.MapPut("/{id:guid}", async (Guid id, UpdateLeadRequest body, PublicSiteService svc, HttpContext http, CancellationToken ct) => (await svc.UpdateLeadAsync(id, body, ct)).ToHttp(http));
    }
}
