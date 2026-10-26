using System.Text.Json;
using Karnama.Api.Infrastructure;
using Karnama.Application.Abstractions;
using Karnama.Application.Common;
using Karnama.Application.Localization;
using Karnama.Application.Me;
using Karnama.Application.Roles;
using Karnama.Application.Settings;
using Karnama.Application.Setup;
using Karnama.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Karnama.Api.Endpoints;

internal static class AppEndpoints
{
    public static void MapMe(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/me").WithTags("Me").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        g.MapGet("/", async (MeService svc, HttpContext http, CancellationToken ct) => (await svc.GetAsync(ct)).ToHttp(http));
        g.MapPatch("/", async (UpdateMeRequest body, MeService svc, HttpContext http, CancellationToken ct) => (await svc.UpdateAsync(body, ct)).ToHttp(http));
    }

    public static void MapRoles(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/permissions", () => Results.Ok(RoleService.Catalog()))
            .WithTags("Roles").RequirePermission(Permissions.RolesManage, PermissionScope.All);

        var g = app.MapGroup("/api/v1/roles").WithTags("Roles")
            .RequirePermission(Permissions.RolesManage, PermissionScope.All)
            .AddEndpointFilter<ValidationFilter>();

        g.MapGet("/", async (string? cursor, int? limit, string? q, bool? includeDeleted, RoleService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(new KeysetQuery(cursor, limit ?? 20, q, includeDeleted ?? false), ct)));
        g.MapGet("/{id:guid}", async (Guid id, RoleService svc, HttpContext http, CancellationToken ct) => (await svc.GetAsync(id, ct)).ToHttp(http));
        g.MapPost("/", async (UpsertRoleRequest body, RoleService svc, HttpContext http, CancellationToken ct) =>
            (await svc.CreateAsync(body, ct)).ToHttp(http, r => Results.Created($"/api/v1/roles/{r.Id}", r)));
        g.MapPut("/{id:guid}", async (Guid id, UpsertRoleRequest body, RoleService svc, HttpContext http, CancellationToken ct) =>
            (await svc.UpdateAsync(id, body, ct)).ToHttp(http));
        g.MapDelete("/{id:guid}", async (Guid id, uint? version, RoleService svc, HttpContext http, CancellationToken ct) =>
            (await svc.DeleteAsync(id, version, ct)).ToHttp(http));
        g.MapPost("/{id:guid}/restore", async (Guid id, RoleService svc, HttpContext http, CancellationToken ct) =>
            (await svc.RestoreAsync(id, ct)).ToHttp(http));
    }

    public static void MapI18n(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/i18n").WithTags("I18n");
        g.MapGet("/languages", async (LocalizationService svc, CancellationToken ct) => Results.Ok(await svc.GetLanguagesAsync(ct)))
            .CacheOutput(OutputCachePolicies.Public);
        var langs = app.MapGroup("/api/v1/i18n/languages/admin").WithTags("I18n").RequirePermission(Permissions.LanguagesManage, PermissionScope.All).AddEndpointFilter<ValidationFilter>();
        langs.MapGet("/", async (LanguageAdminService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));
        langs.MapPost("/", async (UpsertLanguageRequest body, LanguageAdminService svc, Microsoft.AspNetCore.OutputCaching.IOutputCacheStore store, HttpContext http, CancellationToken ct) =>
        {
            var r = await svc.CreateAsync(body, ct);
            if (r.IsSuccess) await store.EvictByTagAsync("public", ct);   // the public language list is output-cached
            return r.ToHttp(http, l => Results.Created($"/api/v1/i18n/languages/admin/{l.Code}", l));
        });
        langs.MapPut("/{code}", async (string code, UpsertLanguageRequest body, LanguageAdminService svc, Microsoft.AspNetCore.OutputCaching.IOutputCacheStore store, HttpContext http, CancellationToken ct) =>
        {
            var r = await svc.UpdateAsync(code, body, ct);
            if (r.IsSuccess) await store.EvictByTagAsync("public", ct);
            return r.ToHttp(http);
        });
        g.MapGet("/{code}", async (string code, LocalizationService svc, HttpContext http, CancellationToken ct) =>
        {
            if (code.Length > 10) return Results.NotFound();
            var bundle = await svc.GetBundleAsync(code, ct);
            if (bundle is null) return Results.NotFound();
            http.Response.Headers.ETag = bundle.ETag;
            http.Response.Headers.CacheControl = "no-cache";
            return http.Request.Headers.IfNoneMatch.ToString() == bundle.ETag
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.Ok(new { bundle.Language, bundle.Texts });
        });
    }

    public static void MapSetup(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/setup").WithTags("Setup").AddEndpointFilter<ValidationFilter>();
        g.MapGet("/status", async (SetupService svc, CancellationToken ct) => Results.Ok(await svc.GetStatusAsync(ct)));
        g.MapPost("/", async (CompleteSetupRequest body, SetupService svc, IOutputCacheStore cache, HttpContext http, CancellationToken ct) =>
        {
            var result = await svc.CompleteAsync(body, ct);
            if (result.IsSuccess) await cache.EvictByTagAsync("public", ct); // the brand name may have changed
            return result.ToHttp(http);
        }).RequireRateLimiting(RateLimits.Otp);
    }

    public static void MapSettings(this IEndpointRouteBuilder app)
    {
        var brand = app.MapGroup("/api/v1/settings/brand").WithTags("Settings")
            .RequirePermission(Permissions.SettingsManage, PermissionScope.All)
            .AddEndpointFilter<ValidationFilter>();

        brand.MapPut("/", async (UpdateBrandRequest body, BrandService svc, IOutputCacheStore cache, HttpContext http, CancellationToken ct) =>
        {
            var r = await svc.UpdateAsync(body, ct);
            await cache.EvictByTagAsync("public", ct);
            return r.ToHttp(http);
        });
        brand.MapPost("/logo", async (IFormFile file, BrandService svc, IOutputCacheStore cache, HttpContext http, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            var r = await svc.SetLogoAsync(stream, Path.GetFileName(file.FileName), file.Length, ct);
            await cache.EvictByTagAsync("public", ct);
            return r.ToHttp(http);
        }).DisableAntiforgery() // bearer-token API, no cookie auth on this route
          .WithMetadata(new RequestSizeLimitAttribute(3 * 1024 * 1024));
        brand.MapDelete("/logo", async (BrandService svc, IOutputCacheStore cache, HttpContext http, CancellationToken ct) =>
        {
            var r = await svc.RemoveLogoAsync(ct);
            await cache.EvictByTagAsync("public", ct);
            return r.ToHttp(http);
        });

        // UI texts: languages.manage or content.manage.
        var texts = app.MapGroup("/api/v1/translations").WithTags("Settings")
            .RequireAuthorization(p => p.RequireAssertion(ctx =>
            {
                var me = ctx.Resource is HttpContext h ? h.RequestServices.GetRequiredService<ICurrentUser>() : null;
                return me is not null && (me.Has(Permissions.LanguagesManage, PermissionScope.All) || me.Has(Permissions.ContentManage, PermissionScope.All));
            }))
            .AddEndpointFilter<ValidationFilter>();
        texts.MapGet("/{lang}", async (string lang, TranslationAdminService svc, HttpContext http, CancellationToken ct) =>
            (await svc.ListAsync(lang, ct)).ToHttp(http));
        texts.MapPut("/{lang}/{key}", async (string lang, string key, SetTranslationRequest body, TranslationAdminService svc, HttpContext http, CancellationToken ct) =>
            (await svc.SetAsync(lang, key, body.Value, ct)).ToHttp(http));
        texts.MapDelete("/{lang}/{key}", async (string lang, string key, TranslationAdminService svc, HttpContext http, CancellationToken ct) =>
            (await svc.ResetAsync(lang, key, ct)).ToHttp(http));
    }

    public static void MapPublic(this IEndpointRouteBuilder app, bool demoLoginEnabled)
    {
        app.MapGet("/api/v1/public/brand", async (BrandService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(ct)))
            .WithTags("Public").CacheOutput(OutputCachePolicies.Public);

        // Public brand files (logo). Ids are regex-checked and resolved inside the storage root only.
        app.MapGet("/media/public/{folder}/{file}", (string folder, string file, IMediaStore media, HttpContext http) =>
        {
            var opened = media.OpenPublic($"{folder}/{file}");
            if (opened is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable"; // names are random and never reused
            // Range requests let browsers (Safari above all) seek and start videos without downloading the whole file.
            return Results.Stream(opened.Value.Stream, opened.Value.ContentType, enableRangeProcessing: true);
        }).WithTags("Public");

        app.MapGet("/api/v1/app/version", (IOptions<AppVersionOptions> o) =>
            Results.Ok(new { o.Value.MinSupportedVersion, o.Value.LatestVersion, o.Value.DownloadUrl }))
            .WithTags("Public").CacheOutput(OutputCachePolicies.Public);
    }
}

internal static class OutputCachePolicies
{
    public const string Public = "public";
}
