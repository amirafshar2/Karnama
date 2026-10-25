using Karnama.Api.Infrastructure;
using Karnama.Application.Common;
using Karnama.Application.Files;
using Karnama.Application.Realtime;
using Karnama.Application.Work;
using Karnama.Domain.Identity;
using Karnama.Domain.Work;
using Microsoft.Net.Http.Headers;

namespace Karnama.Api.Endpoints;

/// <summary>M3: orders, the live work board, comments, resumable file uploads and the change cursor.</summary>
internal static class WorkEndpoints
{
    public static void MapWork(this IEndpointRouteBuilder app)
    {
        // ---- Orders ---- (visibility by scope is enforced in the service: All / Assigned / the portal's own client)
        var orders = app.MapGroup("/api/v1/orders").WithTags("Orders").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        orders.MapGet("/", async (string? cursor, int? limit, string? q, string? status, Guid? clientId, OrderService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(new KeysetQuery(cursor, limit ?? 20, q), CatalogEndpoints.E<OrderStatus>(status), clientId, ct)));
        orders.MapGet("/{id:guid}", async (Guid id, OrderService svc, HttpContext http, CancellationToken ct) => (await svc.GetAsync(id, ct)).ToHttp(http));
        orders.MapPost("/", async (CreateOrderRequest body, OrderService svc, HttpContext http, CancellationToken ct) =>
            (await svc.CreateAsync(body, OrderSource.Admin, ct)).ToHttp(http, r => Results.Created($"/api/v1/orders/{r.Id}", r)))
            .RequirePermission(Permissions.OrdersCreate, PermissionScope.All);
        var manage = app.MapGroup("/api/v1/orders").WithTags("Orders").RequirePermission(Permissions.OrdersManage, PermissionScope.All).AddEndpointFilter<ValidationFilter>();
        manage.MapPost("/{id:guid}/accept", async (Guid id, uint version, OrderService svc, HttpContext http, CancellationToken ct) => (await svc.AcceptAsync(id, version, ct)).ToHttp(http));
        manage.MapPost("/{id:guid}/reject", async (Guid id, RejectOrderRequest body, OrderService svc, HttpContext http, CancellationToken ct) => (await svc.RejectAsync(id, body, ct)).ToHttp(http));
        manage.MapPost("/{id:guid}/cancel", async (Guid id, OrderService svc, HttpContext http, CancellationToken ct) => (await svc.CancelAsync(id, ct)).ToHttp(http));

        // ---- Work board ----
        var work = app.MapGroup("/api/v1/work").WithTags("Work").RequirePermission(Permissions.WorkView, PermissionScope.Assigned).AddEndpointFilter<ValidationFilter>();
        work.MapGet("/board", async (Guid? clientId, Guid? assigneeId, Guid? serviceTypeId, bool? mine, bool? overdue, WorkService svc, CancellationToken ct) =>
            Results.Ok(await svc.BoardAsync(new BoardFilter(clientId, assigneeId, serviceTypeId, mine ?? false, overdue ?? false), ct)));
        work.MapGet("/{id:guid}", async (Guid id, WorkService svc, HttpContext http, CancellationToken ct) => (await svc.GetAsync(id, ct)).ToHttp(http));
        work.MapPost("/{id:guid}/move", async (Guid id, MoveRequest body, WorkService svc, HttpContext http, CancellationToken ct) => (await svc.MoveAsync(id, body, ct)).ToHttp(http));
        work.MapPost("/{id:guid}/assign", async (Guid id, AssignRequest body, WorkService svc, HttpContext http, CancellationToken ct) => (await svc.AssignAsync(id, body, ct)).ToHttp(http));
        work.MapGet("/{id:guid}/suggestions", async (Guid id, WorkService svc, HttpContext http, CancellationToken ct) => (await svc.SuggestAsync(id, ct)).ToHttp(http));
        work.MapPost("/{id:guid}/comments", async (Guid id, CommentRequest body, WorkService svc, HttpContext http, CancellationToken ct) => (await svc.CommentAsync(id, body, ct)).ToHttp(http));

        // ---- Files: resumable upload (start → PATCH chunks with Upload-Offset → HEAD to resume) ----
        var files = app.MapGroup("/api/v1/files").WithTags("Files").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        files.MapPost("/", async (StartUploadRequest body, FileService svc, HttpContext http, CancellationToken ct) =>
            (await svc.StartAsync(body, ct)).ToHttp(http, r => Results.Created($"/api/v1/files/{r.Id}", r)));
        files.MapGet("/{id:guid}/upload", async (Guid id, FileService svc, HttpContext http, CancellationToken ct) => (await svc.StatusAsync(id, ct)).ToHttp(http));
        files.MapMethods("/{id:guid}/upload", ["PATCH"], async (Guid id, HttpContext http, FileService svc, CancellationToken ct) =>
        {
            if (!long.TryParse(http.Request.Headers["Upload-Offset"], out var offset) || offset < 0)
                return Results.Problem(statusCode: 400, title: "Upload-Offset header is required.");
            return (await svc.AppendAsync(id, offset, http.Request.ContentLength, http.Request.Body, ct)).ToHttp(http);
        }).DisableAntiforgery();
        files.MapGet("/{id:guid}", async (Guid id, bool? inline, FileService svc, HttpContext http, CancellationToken ct) =>
        {
            var r = await svc.OpenAsync(id, ct);
            if (r.IsFailure) return r.ToHttp(http);
            var (f, stream) = r.Value;
            // Only images/video/audio/PDF may render inline; everything is served with its sniffed type and nosniff.
            var type = KindToMime(f.Kind);
            var canInline = inline == true && f.Kind is "png" or "jpg" or "gif" or "webp" or "mp4" or "webm" or "mov" or "mp3" or "m4a" or "wav";
            http.Response.Headers.CacheControl = "private, max-age=3600";
            http.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
            return Results.File(stream, type, canInline ? null : f.FileName, enableRangeProcessing: true);
        });
        files.MapPost("/{id:guid}/link", async (Guid id, FileService svc, HttpContext http, CancellationToken ct) => (await svc.LinkAsync(id, ct)).ToHttp(http));
        // Signed, 10-minute link: no Authorization header, so a plain <a href> or Android's download manager can stream it (with resume).
        app.MapGet("/api/v1/files/dl/{token}", async (string token, FileService svc, HttpContext http, CancellationToken ct) =>
        {
            var r = await svc.OpenLinkAsync(token, ct);
            if (r.IsFailure) return r.ToHttp(http);
            var (f, stream) = r.Value;
            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
            http.Response.Headers["Referrer-Policy"] = "no-referrer";
            return Results.File(stream, KindToMime(f.Kind), f.FileName, enableRangeProcessing: true);
        }).WithTags("Files").AllowAnonymous();
        files.MapPost("/{id:guid}/share", async (Guid id, bool visible, FileService svc, HttpContext http, CancellationToken ct) => (await svc.ShareAsync(id, visible, ct)).ToHttp(http));
        files.MapDelete("/{id:guid}", async (Guid id, FileService svc, HttpContext http, CancellationToken ct) => (await svc.DeleteAsync(id, ct)).ToHttp(http));

        // ---- Change cursor (catch up after a reconnect) ----
        app.MapGet("/api/v1/changes", async (long? since, ChangesService svc, CancellationToken ct) => Results.Ok(await svc.SinceAsync(since, ct)))
            .WithTags("Realtime").RequireAuthorization();
    }

    private static string KindToMime(string? kind) => kind switch
    {
        "png" => "image/png", "jpg" => "image/jpeg", "gif" => "image/gif", "webp" => "image/webp", "heic" => "image/heic",
        "mp4" => "video/mp4", "mov" => "video/quicktime", "webm" => "video/webm", "mp3" => "audio/mpeg", "m4a" => "audio/mp4", "wav" => "audio/wav",
        "pdf" => "application/pdf", "zip" => "application/zip", "psd" => "image/vnd.adobe.photoshop",
        _ => "application/octet-stream",
    };
}

/// <summary>The client portal: only users of kind Client linked to a client (checked in the service).</summary>
internal static class PortalEndpoints
{
    public static void MapPortal(this IEndpointRouteBuilder app)
    {
        var portal = app.MapGroup("/api/v1/portal").WithTags("Portal").RequireAuthorization().AddEndpointFilter<ValidationFilter>();
        portal.MapGet("/home", async (Karnama.Application.Portal.PortalService svc, HttpContext http, CancellationToken ct) => (await svc.HomeAsync(ct)).ToHttp(http));
        portal.MapGet("/orders/{id:guid}/messages", async (Guid id, Karnama.Application.Portal.PortalService svc, HttpContext http, CancellationToken ct) => (await svc.MessagesAsync(id, ct)).ToHttp(http));
        portal.MapPost("/approvals/{workItemId:guid}", async (Guid workItemId, Karnama.Application.Portal.ApprovalRequest body, Karnama.Application.Portal.PortalService svc, HttpContext http, CancellationToken ct) =>
            (await svc.DecideAsync(workItemId, body, ct)).ToHttp(http));
        portal.MapPost("/orders", async (CreateOrderRequest body, OrderService svc, HttpContext http, CancellationToken ct) =>
            (await svc.CreateAsync(body, OrderSource.Client, ct)).ToHttp(http, r => Results.Created($"/api/v1/orders/{r.Id}", r)));
    }
}
