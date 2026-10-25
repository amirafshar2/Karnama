using Karnama.Api.Infrastructure;
using Karnama.Application.Abstractions;
using Karnama.Application.Catalog;
using Karnama.Application.Clients;
using Karnama.Application.Common;
using Karnama.Application.History;
using Karnama.Application.Onboarding;
using Karnama.Application.Users;
using Karnama.Domain.Catalog;
using Karnama.Domain.Clients;
using Karnama.Domain.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Karnama.Api.Endpoints;

/// <summary>M2: admin CRUD for users, clients, platforms, service types and packages (+ history, Excel, onboarding).</summary>
internal static class CatalogEndpoints
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static IResult File(byte[] bytes, string name) =>
        Results.File(bytes, Xlsx, $"{name}-{DateTime.UtcNow:yyyyMMdd}.xlsx");

    private static KeysetQuery Q(string? cursor, int? limit, string? q, bool? includeDeleted) => new(cursor, limit ?? 20, q, includeDeleted ?? false);

    public static void MapCatalog(this IEndpointRouteBuilder app)
    {
        // ---- Users (staff) ----
        var users = app.MapGroup("/api/v1/users").WithTags("Users")
            .RequirePermission(Permissions.UsersManage, PermissionScope.All).AddEndpointFilter<ValidationFilter>();
        users.MapGet("/", async (string? cursor, int? limit, string? q, bool? includeDeleted, UserKind? kind, UserService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(Q(cursor, limit, q, includeDeleted), kind, ct)));
        users.MapGet("/export", async (UserService svc, CancellationToken ct) => File(await svc.ExportAsync(ct), "users"));
        users.MapGet("/{id:guid}", async (Guid id, UserService svc, HttpContext http, CancellationToken ct) => (await svc.GetAsync(id, ct)).ToHttp(http));
        users.MapPost("/", async (UpsertUserRequest body, UserService svc, HttpContext http, CancellationToken ct) =>
            (await svc.CreateAsync(body, ct)).ToHttp(http, r => Results.Created($"/api/v1/users/{r.Id}", r)));
        users.MapPut("/{id:guid}", async (Guid id, UpsertUserRequest body, UserService svc, HttpContext http, CancellationToken ct) => (await svc.UpdateAsync(id, body, ct)).ToHttp(http));
        users.MapDelete("/{id:guid}", async (Guid id, UserService svc, HttpContext http, CancellationToken ct) => (await svc.DeleteAsync(id, ct)).ToHttp(http));
        users.MapPost("/{id:guid}/restore", async (Guid id, UserService svc, HttpContext http, CancellationToken ct) => (await svc.RestoreAsync(id, ct)).ToHttp(http));

        // ---- Platforms (social networks list) ----
        app.MapGet("/api/v1/platforms", async (bool? includeDeleted, PlatformService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(includeDeleted ?? false, ct))).WithTags("Platforms").RequireAuthorization();
        var platforms = app.MapGroup("/api/v1/platforms").WithTags("Platforms")
            .RequirePermission(Permissions.SettingsManage, PermissionScope.All).AddEndpointFilter<ValidationFilter>();
        platforms.MapPost("/", async (UpsertPlatformRequest body, PlatformService svc, HttpContext http, CancellationToken ct) =>
            (await svc.CreateAsync(body, ct)).ToHttp(http, r => Results.Created($"/api/v1/platforms/{r.Id}", r)));
        platforms.MapPut("/{id:guid}", async (Guid id, UpsertPlatformRequest body, PlatformService svc, HttpContext http, CancellationToken ct) => (await svc.UpdateAsync(id, body, ct)).ToHttp(http));
        platforms.MapDelete("/{id:guid}", async (Guid id, PlatformService svc, HttpContext http, CancellationToken ct) => (await svc.DeleteAsync(id, ct)).ToHttp(http));
        platforms.MapPost("/{id:guid}/restore", async (Guid id, PlatformService svc, HttpContext http, CancellationToken ct) => (await svc.RestoreAsync(id, ct)).ToHttp(http));

        // ---- Service types (form builder + workflow) ----
        app.MapGet("/api/v1/services/options", async (ServiceTypeService svc, CancellationToken ct) =>
            Results.Ok((await svc.OptionsAsync(ct)).Select(o => new { o.Id, o.Name }))).WithTags("Services").RequireAuthorization();
        var services = app.MapGroup("/api/v1/services").WithTags("Services")
            .RequirePermission(Permissions.ServicesManage, PermissionScope.All).AddEndpointFilter<ValidationFilter>();
        services.MapGet("/", async (string? cursor, int? limit, string? q, bool? includeDeleted, ServiceTypeService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(Q(cursor, limit, q, includeDeleted), ct)));
        services.MapGet("/export", async (ServiceTypeService svc, CancellationToken ct) => File(await svc.ExportAsync(ct), "services"));
        services.MapGet("/{id:guid}", async (Guid id, ServiceTypeService svc, HttpContext http, CancellationToken ct) => (await svc.GetAsync(id, ct)).ToHttp(http));
        services.MapPost("/", async (UpsertServiceTypeRequest body, ServiceTypeService svc, HttpContext http, CancellationToken ct) =>
            (await svc.CreateAsync(body, ct)).ToHttp(http, r => Results.Created($"/api/v1/services/{r.Id}", r)));
        services.MapPut("/{id:guid}", async (Guid id, UpsertServiceTypeRequest body, ServiceTypeService svc, HttpContext http, CancellationToken ct) => (await svc.UpdateAsync(id, body, ct)).ToHttp(http));
        services.MapDelete("/{id:guid}", async (Guid id, ServiceTypeService svc, HttpContext http, CancellationToken ct) => (await svc.DeleteAsync(id, ct)).ToHttp(http));
        services.MapPost("/{id:guid}/restore", async (Guid id, ServiceTypeService svc, HttpContext http, CancellationToken ct) => (await svc.RestoreAsync(id, ct)).ToHttp(http));

        // ---- Packages ----
        var packagesRead = app.MapGroup("/api/v1/packages").WithTags("Packages").RequirePermission(Permissions.PackagesView, PermissionScope.All);
        packagesRead.MapGet("/", async (string? cursor, int? limit, string? q, bool? includeDeleted, PackageStatus? status, PackageService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(Q(cursor, limit, q, includeDeleted), status, ct)));
        packagesRead.MapGet("/{id:guid}", async (Guid id, PackageService svc, HttpContext http, CancellationToken ct) => (await svc.GetAsync(id, ct)).ToHttp(http));
        var packages = app.MapGroup("/api/v1/packages").WithTags("Packages")
            .RequirePermission(Permissions.PackagesManage, PermissionScope.All).AddEndpointFilter<ValidationFilter>();
        packages.MapGet("/export", async (PackageService svc, CancellationToken ct) => File(await svc.ExportAsync(ct), "packages"));
        packages.MapPost("/", async (UpsertPackageRequest body, PackageService svc, HttpContext http, CancellationToken ct) =>
            (await svc.CreateAsync(body, ct)).ToHttp(http, r => Results.Created($"/api/v1/packages/{r.Id}", r)));
        packages.MapPut("/{id:guid}", async (Guid id, UpsertPackageRequest body, PackageService svc, HttpContext http, CancellationToken ct) => (await svc.UpdateAsync(id, body, ct)).ToHttp(http));
        packages.MapPost("/{id:guid}/cover", async (Guid id, IFormFile file, PackageService svc, HttpContext http, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return (await svc.SetCoverAsync(id, s, file.Length, ct)).ToHttp(http);
        }).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(3 * 1024 * 1024));
        packages.MapDelete("/{id:guid}", async (Guid id, PackageService svc, HttpContext http, CancellationToken ct) => (await svc.DeleteAsync(id, ct)).ToHttp(http));
        packages.MapPost("/{id:guid}/restore", async (Guid id, PackageService svc, HttpContext http, CancellationToken ct) => (await svc.RestoreAsync(id, ct)).ToHttp(http));

        // ---- Clients ----
        var clientsRead = app.MapGroup("/api/v1/clients").WithTags("Clients").RequirePermission(Permissions.ClientsView, PermissionScope.Assigned);
        clientsRead.MapGet("/", async (string? cursor, int? limit, string? q, bool? includeDeleted, ClientStatus? status, ClientService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(Q(cursor, limit, q, includeDeleted), status, ct)));
        clientsRead.MapGet("/{id:guid}", async (Guid id, ClientService svc, HttpContext http, CancellationToken ct) => (await svc.GetAsync(id, ct)).ToHttp(http));
        var clients = app.MapGroup("/api/v1/clients").WithTags("Clients")
            .RequirePermission(Permissions.ClientsManage, PermissionScope.All).AddEndpointFilter<ValidationFilter>();
        clients.MapGet("/export", async (ClientService svc, CancellationToken ct) => File(await svc.ExportAsync(ct), "clients"));
        clients.MapPost("/", async (UpsertClientRequest body, ClientService svc, HttpContext http, CancellationToken ct) =>
            (await svc.CreateAsync(body, ct)).ToHttp(http, r => Results.Created($"/api/v1/clients/{r.Id}", r)));
        clients.MapPut("/{id:guid}", async (Guid id, UpsertClientRequest body, ClientService svc, HttpContext http, CancellationToken ct) => (await svc.UpdateAsync(id, body, ct)).ToHttp(http));
        clients.MapDelete("/{id:guid}", async (Guid id, ClientService svc, HttpContext http, CancellationToken ct) => (await svc.DeleteAsync(id, ct)).ToHttp(http));
        clients.MapPost("/{id:guid}/restore", async (Guid id, ClientService svc, HttpContext http, CancellationToken ct) => (await svc.RestoreAsync(id, ct)).ToHttp(http));
        clients.MapPost("/{id:guid}/portal", async (Guid id, ClientService svc, HttpContext http, CancellationToken ct) => (await svc.EnablePortalAsync(id, ct)).ToHttp(http));
        clients.MapPost("/{id:guid}/packages", async (Guid id, AssignPackageRequest body, ClientService svc, HttpContext http, CancellationToken ct) =>
            (await svc.AssignPackageAsync(id, body, ct)).ToHttp(http));
        clients.MapPost("/{id:guid}/packages/{cpId:guid}/cancel", async (Guid id, Guid cpId, ClientService svc, HttpContext http, CancellationToken ct) =>
            (await svc.CancelPackageAsync(id, cpId, ct)).ToHttp(http));

        // ---- History & onboarding ----
        app.MapGet("/api/v1/history/{entityType}/{id:guid}", async (string entityType, Guid id, HistoryService svc, HttpContext http, CancellationToken ct) =>
            (await svc.GetAsync(entityType, id, ct)).ToHttp(http)).WithTags("History").RequireAuthorization();
        app.MapGet("/api/v1/onboarding", async (OnboardingService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(ct)))
            .WithTags("Onboarding").RequirePermission(Permissions.SettingsManage, PermissionScope.All);
    }
}
