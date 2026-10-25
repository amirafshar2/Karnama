using System.Text.Json;
using Karnama.Api.Infrastructure;
using Karnama.Application.Abstractions;
using Karnama.Application.Common;
using Karnama.Application.Localization;
using Karnama.Application.Me;
using Karnama.Application.Roles;
using Karnama.Application.Setup;
using Karnama.Domain.Identity;
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
        g.MapPost("/", async (CompleteSetupRequest body, SetupService svc, HttpContext http, CancellationToken ct) =>
            (await svc.CompleteAsync(body, ct)).ToHttp(http)).RequireRateLimiting(RateLimits.Otp);
    }

    public static void MapPublic(this IEndpointRouteBuilder app, bool demoLoginEnabled)
    {
        app.MapGet("/api/v1/public/brand", async (IAppDbContext db, CancellationToken ct) =>
        {
            var json = await db.SiteSettings.AsNoTracking().Where(s => s.Key == "brand").Select(s => s.Json).FirstOrDefaultAsync(ct);
            string? name = null;
            if (json is not null)
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("name", out var n)) name = n.GetString();
            }
            return Results.Ok(new { name = string.IsNullOrWhiteSpace(name) ? null : name, demo = demoLoginEnabled });
        }).WithTags("Public").CacheOutput(OutputCachePolicies.Public);

        app.MapGet("/api/v1/app/version", (IOptions<AppVersionOptions> o) =>
            Results.Ok(new { o.Value.MinSupportedVersion, o.Value.LatestVersion, o.Value.DownloadUrl }))
            .WithTags("Public").CacheOutput(OutputCachePolicies.Public);
    }
}

internal static class OutputCachePolicies
{
    public const string Public = "public";
}
